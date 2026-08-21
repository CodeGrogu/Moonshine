using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Moonshine.Core.Transport;

/// <summary>
/// Production-grade asynchronous framed TCP transport for Moonshine reliable control,
/// session negotiation, and remote host management channels.
/// Enforces length-prefix framing, Nagle disabling (TCP_NODELAY), and strict fault propagation.
/// </summary>
public sealed class MoonshineReliableTransport : IMoonshineReliableTransport
{
    private readonly Socket _socket;
    private readonly CancellationTokenSource _cts = new();
    private int _state;
    private int _disposed;

    // Telemetry counters
    private ulong _bytesSent;
    private ulong _bytesReceived;
    private ulong _packetsSent;
    private ulong _packetsReceived;
    private ulong _socketFaults;

    public event TransportFaultHandler? OnTransportFault;

    public TransportState State => (TransportState)Volatile.Read(ref _state);

    public TransportMetrics Metrics => new(
        BytesSent: Volatile.Read(ref _bytesSent),
        BytesReceived: Volatile.Read(ref _bytesReceived),
        PacketsSent: Volatile.Read(ref _packetsSent),
        PacketsReceived: Volatile.Read(ref _packetsReceived),
        PacketsDropped: 0,
        SocketFaults: Volatile.Read(ref _socketFaults),
        CurrentQueueDepth: 0,
        PeakQueueDepth: 0,
        AverageLatencyUs: 0.0);

    public MoonshineReliableTransport(
        Socket? existingConnectedSocket = null,
        int receiveBufferSize = 1024 * 1024,
        int sendBufferSize = 1024 * 1024)
    {
        if (existingConnectedSocket is not null)
        {
            _socket = existingConnectedSocket;
            _socket.NoDelay = true;
            _state = (int)TransportState.Connected;
        }
        else
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                ReceiveBufferSize = receiveBufferSize,
                SendBufferSize = sendBufferSize,
                Blocking = false
            };
            _state = (int)TransportState.Uninitialised;
        }
    }

    public async ValueTask ConnectAsync(EndPoint remoteEndPoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        Volatile.Write(ref _state, (int)TransportState.Connecting);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            await _socket.ConnectAsync(remoteEndPoint, linkedCts.Token).ConfigureAwait(false);
            Volatile.Write(ref _state, (int)TransportState.Connected);
        }
        // ALLOWED_EXCEPTION: Record socket fault on connection failure and rethrow exception to caller.
        catch (Exception ex)
        {
            Interlocked.Increment(ref _socketFaults);
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            OnTransportFault?.Invoke(ex, TransportState.Faulted);
            throw;
        }
    }

    public async ValueTask<bool> SendFramedMessageAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if ((TransportState)Volatile.Read(ref _state) != TransportState.Connected) return false;

        byte[] lengthPrefix = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)message.Length);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

            // Send 4-byte length prefix
            int headerSent = await _socket.SendAsync(lengthPrefix, SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
            if (headerSent != 4) return false;

            // Send payload bytes
            int payloadSent = 0;
            while (payloadSent < message.Length)
            {
                int sent = await _socket.SendAsync(message[payloadSent..], SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
                if (sent <= 0) return false;
                payloadSent += sent;
            }

            Interlocked.Add(ref _bytesSent, (ulong)(4 + message.Length));
            Interlocked.Increment(ref _packetsSent);
            return true;
        }
        // ALLOWED_EXCEPTION: Record socket fault on transmission failure and signal error to caller.
        catch (Exception ex)
        {
            Interlocked.Increment(ref _socketFaults);
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            OnTransportFault?.Invoke(ex, TransportState.Faulted);
            return false;
        }
    }

    public async ValueTask<int> ReceiveFramedMessageAsync(
        Memory<byte> destination,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if ((TransportState)Volatile.Read(ref _state) != TransportState.Connected) return -1;

        byte[] lengthPrefix = new byte[4];
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

            // 1. Read exact 4-byte length prefix
            int headerBytesRead = 0;
            while (headerBytesRead < 4)
            {
                int read = await _socket.ReceiveAsync(lengthPrefix.AsMemory(headerBytesRead, 4 - headerBytesRead), SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    Volatile.Write(ref _state, (int)TransportState.Disconnected);
                    return 0; // Remote peer closed connection
                }
                headerBytesRead += read;
            }

            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(lengthPrefix);
            if (destination.Length < (int)payloadLength)
            {
                throw new InvalidOperationException($"Destination buffer too small: requires {payloadLength} bytes, provided {destination.Length} bytes.");
            }

            // 2. Read full payload
            int payloadBytesRead = 0;
            while (payloadBytesRead < (int)payloadLength)
            {
                int read = await _socket.ReceiveAsync(destination.Slice(payloadBytesRead, (int)payloadLength - payloadBytesRead), SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    Volatile.Write(ref _state, (int)TransportState.Disconnected);
                    return 0;
                }
                payloadBytesRead += read;
            }

            Interlocked.Add(ref _bytesReceived, (ulong)(4 + payloadLength));
            Interlocked.Increment(ref _packetsReceived);
            return payloadBytesRead;
        }
        // ALLOWED_EXCEPTION: Record socket fault on framed message receive failure and signal error to caller.
        catch (Exception ex)
        {
            if (_cts.IsCancellationRequested) return 0;

            Interlocked.Increment(ref _socketFaults);
            Volatile.Write(ref _state, (int)TransportState.Faulted);
            OnTransportFault?.Invoke(ex, TransportState.Faulted);
            return -1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Volatile.Write(ref _state, (int)TransportState.Disconnected);
        _cts.Cancel();

        try
        {
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
        }
        // ALLOWED_EXCEPTION: Suppress shutdown errors if socket was already reset by remote host.
        catch (SocketException)
        {
        }

        _socket.Dispose();
        _cts.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
