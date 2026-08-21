using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Moonshine.Core.Transport;

/// <summary>
/// Production-grade asynchronous Windows UDP datagram transport engine for Moonshine media,
/// input, audio backchannel, and QoS feedback channels.
/// Provides zero-allocation hot paths, scatter/gather buffer framing, and granular telemetry.
/// </summary>
public sealed class MoonshineDatagramTransport : IMoonshineDatagramTransport
{
    private const int DefaultMaxPacketSize = 2048;
    private readonly Socket _socket;
    private readonly int _localPort;
    private readonly byte[] _receiveBuffer;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private int _state;
    private int _disposed;

    // Telemetry counters
    private ulong _bytesSent;
    private ulong _bytesReceived;
    private ulong _packetsSent;
    private ulong _packetsReceived;
    private ulong _packetsDropped;
    private ulong _socketFaults;
    private long _totalLatencyTicks;
    private long _latencySampleCount;

    public event DatagramReceivedHandler? OnDatagramReceived;
    public event TransportFaultHandler? OnTransportFault;

    public int LocalPort => _localPort;
    public TransportState State => (TransportState)Volatile.Read(ref _state);

    public TransportMetrics Metrics
    {
        get
        {
            long sampleCount = Volatile.Read(ref _latencySampleCount);
            long totalTicks = Volatile.Read(ref _totalLatencyTicks);
            double avgLatencyUs = sampleCount > 0
                ? (double)totalTicks / sampleCount * (1_000_000.0 / Stopwatch.Frequency)
                : 0.0;

            return new TransportMetrics(
                BytesSent: Volatile.Read(ref _bytesSent),
                BytesReceived: Volatile.Read(ref _bytesReceived),
                PacketsSent: Volatile.Read(ref _packetsSent),
                PacketsReceived: Volatile.Read(ref _packetsReceived),
                PacketsDropped: Volatile.Read(ref _packetsDropped),
                SocketFaults: Volatile.Read(ref _socketFaults),
                CurrentQueueDepth: 0,
                PeakQueueDepth: 0,
                AverageLatencyUs: avgLatencyUs);
        }
    }

    public MoonshineDatagramTransport(
        int bindPort = 0,
        int receiveBufferSize = 8 * 1024 * 1024,
        int sendBufferSize = 2 * 1024 * 1024,
        bool dontFragment = true)
    {
        _receiveBuffer = GC.AllocateArray<byte>(DefaultMaxPacketSize, pinned: true);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = receiveBufferSize,
            SendBufferSize = sendBufferSize,
            Blocking = false
        };

        if (dontFragment && OperatingSystem.IsWindows())
        {
            try
            {
                _socket.DontFragment = true;
            }
            // ALLOWED_EXCEPTION: Some loopback virtual adapters do not support the DF bit; ignore gracefully.
            catch (SocketException)
            {
            }
        }

        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(IPAddress.Any, bindPort));

        _localPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        _state = (int)TransportState.Connected;
    }

    public void StartReceiving()
    {
        if (_receiveTask is not null) return;

        _receiveTask = Task.Factory.StartNew(
            ReceiveLoopAsync,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private async Task ReceiveLoopAsync()
    {
        var remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                SocketReceiveFromResult result = await _socket.ReceiveFromAsync(
                    _receiveBuffer.AsMemory(0, DefaultMaxPacketSize),
                    SocketFlags.None,
                    remoteEndPoint,
                    _cts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes > 0)
                {
                    Interlocked.Add(ref _bytesReceived, (ulong)result.ReceivedBytes);
                    Interlocked.Increment(ref _packetsReceived);

                    OnDatagramReceived?.Invoke(
                        _receiveBuffer.AsMemory(0, result.ReceivedBytes),
                        result.RemoteEndPoint);
                }
            }
            // ALLOWED_EXCEPTION: Gracefully terminate background receive loop on task cancellation.
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Propagate background socket error to callers via fault event and transition state.
            catch (Exception ex)
            {
                if (_cts.IsCancellationRequested) break;

                Interlocked.Increment(ref _socketFaults);
                Volatile.Write(ref _state, (int)TransportState.Faulted);
                OnTransportFault?.Invoke(ex, TransportState.Faulted);
                break;
            }
        }

        if ((TransportState)Volatile.Read(ref _state) != TransportState.Faulted)
        {
            Volatile.Write(ref _state, (int)TransportState.Disconnected);
        }
    }

    public async ValueTask<bool> SendDatagramAsync(
        ReadOnlyMemory<byte> datagram,
        EndPoint destination,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        long startTick = Stopwatch.GetTimestamp();
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            int sent = await _socket.SendToAsync(datagram, SocketFlags.None, destination, linkedCts.Token).ConfigureAwait(false);

            Interlocked.Add(ref _bytesSent, (ulong)sent);
            Interlocked.Increment(ref _packetsSent);

            long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
            Interlocked.Add(ref _totalLatencyTicks, elapsedTicks);
            Interlocked.Increment(ref _latencySampleCount);

            return sent == datagram.Length;
        }
        // ALLOWED_EXCEPTION: Record dropped packet telemetry and propagate socket fault to callers.
        catch (Exception ex)
        {
            Interlocked.Increment(ref _packetsDropped);
            Interlocked.Increment(ref _socketFaults);
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            OnTransportFault?.Invoke(ex, TransportState.Faulted);
            return false;
        }
    }

    public async ValueTask<bool> SendDatagramGatherAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        EndPoint destination,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        int totalLength = header.Length + payload.Length;
        byte[] pooled = ArrayPool<byte>.Shared.Rent(totalLength);

        long startTick = Stopwatch.GetTimestamp();
        try
        {
            header.Span.CopyTo(pooled.AsSpan(0, header.Length));
            payload.Span.CopyTo(pooled.AsSpan(header.Length, payload.Length));

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            int sent = await _socket.SendToAsync(
                pooled.AsMemory(0, totalLength),
                SocketFlags.None,
                destination,
                linkedCts.Token).ConfigureAwait(false);

            Interlocked.Add(ref _bytesSent, (ulong)sent);
            Interlocked.Increment(ref _packetsSent);

            long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
            Interlocked.Add(ref _totalLatencyTicks, elapsedTicks);
            Interlocked.Increment(ref _latencySampleCount);

            return sent == totalLength;
        }
        // ALLOWED_EXCEPTION: Record dropped packet telemetry and propagate socket fault to callers.
        catch (Exception ex)
        {
            Interlocked.Increment(ref _packetsDropped);
            Interlocked.Increment(ref _socketFaults);
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            OnTransportFault?.Invoke(ex, TransportState.Faulted);
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Volatile.Write(ref _state, (int)TransportState.Disconnected);
        _cts.Cancel();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Suppress task cancellation exceptions during graceful disposal.
            catch (Exception)
            {
            }
        }

        _socket.Dispose();
        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
