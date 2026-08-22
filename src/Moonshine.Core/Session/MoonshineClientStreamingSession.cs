using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Moonshine.Core.Audio;
using Moonshine.Core.Feedback;
using Moonshine.Core.Media;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;
using Moonshine.Protocol.Video;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;

namespace Moonshine.Core.Session;

/// <summary>
/// State machine states for a Moonshine client streaming session.
/// </summary>
public enum ClientSessionState
{
    /// <summary>Session created and configured but not yet started.</summary>
    Created = 0,

    /// <summary>Connecting transport sockets to remote host.</summary>
    Connecting = 1,

    /// <summary>Protocol handshake and stream capability negotiation in flight.</summary>
    Negotiating = 2,

    /// <summary>Media reception, jitter buffering, decoding, playback, and input active.</summary>
    Streaming = 3,

    /// <summary>Streaming active but experiencing packet loss or network degradation.</summary>
    Degraded = 4,

    /// <summary>Gracefully stopping streaming and draining workers.</summary>
    Draining = 5,

    /// <summary>Session completely closed and all resources deterministically released.</summary>
    Closed = 6,

    /// <summary>Unrecoverable socket, codec, or network failure occurred.</summary>
    Faulted = 7
}

/// <summary>
/// Configuration parameters for a Moonshine client streaming session.
/// </summary>
public sealed record ClientSessionConfig
{
    public IPAddress HostAddress { get; init; } = IPAddress.Loopback;
    public int HostVideoPort { get; init; } = 48011;
    public int HostAudioPort { get; init; } = 48012;
    public int HostControlFeedbackPort { get; init; } = 48013;
    public int LocalVideoPort { get; init; }
    public int LocalAudioPort { get; init; }
    public int LocalControlFeedbackPort { get; init; }
    public ulong SessionId { get; init; } = 1;
    public uint StreamId { get; init; } = 1;
    public uint AudioSampleRate { get; init; } = 48000;
    public AudioChannelConfiguration AudioTopology { get; init; } = AudioChannelConfiguration.Stereo;
    public bool EnableFec { get; init; } = true;
    public int FecDataShards { get; init; } = 10;
    public int FecParityShards { get; init; } = 2;
    public int MtuPayloadSize { get; init; } = 1188;
    public int MaxJitterFrames { get; init; } = 16;
    public int FeedbackIntervalMs { get; init; } = 50;

    public static ClientSessionConfig Default => new();
}

/// <summary>
/// Telemetry metrics snapshot for an active client streaming session.
/// </summary>
public readonly record struct ClientSessionMetrics(
    ulong TotalVideoPacketsReceived,
    ulong TotalVideoFramesCompleted,
    ulong TotalAudioPacketsReceived,
    ulong TotalAudioFramesDecoded,
    ulong TotalFecRecoveredPackets,
    ulong TotalLostPackets,
    ulong TotalInputPacketsSent,
    double AverageJitterUs,
    uint RoundTripTimeUs,
    ClientSessionState State,
    string? LastError = null);

/// <summary>
/// Production Moonshine client streaming session orchestrator.
/// Ingests Moonshine-native UDP video frames into <see cref="MoonshineMediaReassemblyPipeline"/>,
/// decodes and renders Opus audio via <see cref="MoonshineClientAudioPipeline"/>,
/// reports 20 Hz periodic loss/jitter statistics via <see cref="MoonshineFeedbackReporter"/>,
/// and transmits low-latency client inputs.
/// Operates with zero GameStream, RTSP, or RTP dependencies and enforces zero GC allocations on steady-state hot paths.
/// </summary>
public sealed class MoonshineClientStreamingSession : IAsyncDisposable, IDisposable
{
    private readonly ClientSessionConfig _config;
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _cts = new();

    private MoonshineMediaReassemblyPipeline? _reassemblyPipeline;
    private MoonshineClientAudioPipeline? _audioPipeline;
    private MoonshineFeedbackReporter? _feedbackReporter;

    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private Socket? _controlSocket;

    private IPEndPoint _hostVideoEndpoint;
    private IPEndPoint _hostAudioEndpoint;
    private IPEndPoint _hostControlEndpoint;

    private Task? _videoReceiveTask;
    private Task? _audioReceiveTask;
    private Task? _controlKeepAliveTask;

    private ClientSessionState _state = ClientSessionState.Created;
    private string? _lastError;
    private bool _disposed;

    // Metrics counters
    private ulong _totalVideoPacketsReceived;
    private ulong _totalVideoFramesCompleted;
    private ulong _totalAudioPacketsReceived;
    private ulong _totalInputPacketsSent;
    private uint _inputSequenceNumber;

    private readonly bool _ownsReassembly;
    private readonly bool _ownsAudio;

    public ClientSessionConfig Config => _config;
    public ClientSessionState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    public string? LastError
    {
        get
        {
            lock (_stateLock) return _lastError;
        }
    }

    public bool IsStreaming => State == ClientSessionState.Streaming;

    public int BoundLocalVideoPort => (_videoSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalAudioPort => (_audioSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalControlPort => (_controlSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    public Action<MoonshineFrameDesc>? OnVideoFrameReassembled { get; set; }
    public Action<ClientSessionState, string?>? OnStateChanged { get; set; }

    public ClientSessionMetrics Metrics
    {
        get
        {
            var reassemblyMetrics = _reassemblyPipeline?.Metrics ?? default;
            var audioMetrics = _audioPipeline?.Metrics ?? default;

            return new ClientSessionMetrics(
                Interlocked.Read(ref _totalVideoPacketsReceived),
                Interlocked.Read(ref _totalVideoFramesCompleted),
                Interlocked.Read(ref _totalAudioPacketsReceived),
                audioMetrics?.FramesDecoded ?? 0,
                reassemblyMetrics.PacketsRecoveredFec,
                reassemblyMetrics.PacketsLost,
                Interlocked.Read(ref _totalInputPacketsSent),
                reassemblyMetrics.AverageJitterMicroseconds,
                _feedbackReporter?.RoundTripTimeUs ?? 0,
                State,
                LastError);
        }
    }

    public MoonshineClientStreamingSession(
        ClientSessionConfig? config = null,
        MoonshineMediaReassemblyPipeline? reassemblyPipeline = null,
        MoonshineClientAudioPipeline? audioPipeline = null)
    {
        _config = config ?? ClientSessionConfig.Default;
        _hostVideoEndpoint = new IPEndPoint(_config.HostAddress, _config.HostVideoPort);
        _hostAudioEndpoint = new IPEndPoint(_config.HostAddress, _config.HostAudioPort);
        _hostControlEndpoint = new IPEndPoint(_config.HostAddress, _config.HostControlFeedbackPort);

        _reassemblyPipeline = reassemblyPipeline;
        _ownsReassembly = reassemblyPipeline == null;

        _audioPipeline = audioPipeline;
        _ownsAudio = audioPipeline == null;
    }

    /// <summary>
    /// Starts the client streaming session, binding UDP media/control sockets and launching reception workers.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is ClientSessionState.Streaming or ClientSessionState.Connecting or ClientSessionState.Negotiating)
            {
                return;
            }

            TransitionStateLocked(ClientSessionState.Connecting);
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Initialise sockets
            InitSockets();

            // 2. Initialise Video Reassembly Pipeline
            if (_reassemblyPipeline == null)
            {
                int fecData = _config.EnableFec ? _config.FecDataShards : 0;
                int fecParity = _config.EnableFec ? _config.FecParityShards : 0;
                _reassemblyPipeline = new MoonshineMediaReassemblyPipeline(
                    maxFrames: _config.MaxJitterFrames,
                    fecDataShards: fecData,
                    fecParityShards: fecParity,
                    mtuPayloadSize: _config.MtuPayloadSize);
            }

            // 3. Initialise Client Audio Pipeline
            if (_audioPipeline == null)
            {
                _audioPipeline = new MoonshineClientAudioPipeline(
                    sampleRate: _config.AudioSampleRate,
                    channels: _config.AudioTopology,
                    isExclusive: false,
                    startBackgroundWorker: true);
            }

            // 4. Initialise Feedback Reporter
            _feedbackReporter = new MoonshineFeedbackReporter(
                streamId: _config.StreamId,
                sessionId: _config.SessionId,
                reportIntervalMs: _config.FeedbackIntervalMs,
                reassemblyPipeline: _reassemblyPipeline,
                socket: _controlSocket,
                remoteFeedbackEndpoint: _hostControlEndpoint);

            // 5. Start background worker loops
            _videoReceiveTask = Task.Run(VideoReceiveLoopAsync, CancellationToken.None);
            _audioReceiveTask = Task.Run(AudioReceiveLoopAsync, CancellationToken.None);
            _controlKeepAliveTask = Task.Run(ControlKeepAliveLoopAsync, CancellationToken.None);

            // 6. Transition to Streaming state
            lock (_stateLock)
            {
                TransitionStateLocked(ClientSessionState.Streaming);
            }
        }
        // ALLOWED_EXCEPTION: Fails closed on any network or initialisation fault and releases resources.
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _lastError = ex.Message;
                TransitionStateLocked(ClientSessionState.Faulted, ex.Message);
            }

            await CleanupResourcesAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void InitSockets()
    {
        _videoSocket?.Dispose();
        _videoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 2 * 1024 * 1024,
            SendBufferSize = 256 * 1024,
            ExclusiveAddressUse = false
        };
        _videoSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _videoSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalVideoPort));

        _audioSocket?.Dispose();
        _audioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 512 * 1024,
            SendBufferSize = 128 * 1024,
            ExclusiveAddressUse = false
        };
        _audioSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _audioSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalAudioPort));

        _controlSocket?.Dispose();
        _controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 256 * 1024,
            SendBufferSize = 256 * 1024,
            ExclusiveAddressUse = false
        };
        _controlSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _controlSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalControlFeedbackPort));
    }

    private async Task VideoReceiveLoopAsync()
    {
        byte[] buffer = new byte[65536];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket = _videoSocket;
            if (socket == null) break;

            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    remoteEp,
                    _cts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize) continue;

                ReadOnlySpan<byte> datagram = buffer.AsSpan(0, result.ReceivedBytes);
                Interlocked.Increment(ref _totalVideoPacketsReceived);

                if (_reassemblyPipeline != null)
                {
                    int ingestResult = _reassemblyPipeline.IngestDatagram(datagram);
                    if (ingestResult == 1)
                    {
                        // Frame complete!
                        Interlocked.Increment(ref _totalVideoFramesCompleted);

                        if (_reassemblyPipeline.TryPopFrame(out MoonshineFrameDesc frame))
                        {
                            OnVideoFrameReassembled?.Invoke(frame);
                        }
                    }

                    // Record packet stats for QoS feedback reporting
                    if (MoonshineProtocolCodec.TryReadHeader(datagram, out var mshnHdr) == MoonshineErrorCode.Success &&
                        datagram.Length >= MoonshineVideoPacketiser.TotalHeaderOverhead)
                    {
                        if (MoonshineVideoPacketCodec.TryReadHeader(datagram[MoonshineProtocolConstants.HeaderSize..], out var videoHdr))
                        {
                            _feedbackReporter?.RecordPacketReceived(
                                frameIndex: videoHdr.FrameIndex,
                                packetBytes: (uint)result.ReceivedBytes,
                                senderTimestampUs: mshnHdr.TimestampUs,
                                isCompleteFrame: ingestResult == 1);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Handle transient UDP socket errors without crashing streaming loop.
            catch (SocketException ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetFaultedState($"Video socket error: {ex.Message}");
                break;
            }
            // ALLOWED_EXCEPTION: Catch unexpected exceptions in receive loop to transition to Faulted safely.
            catch (Exception ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetFaultedState($"Video receive error: {ex.Message}");
                break;
            }
        }
    }

    private async Task AudioReceiveLoopAsync()
    {
        byte[] buffer = new byte[16384];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket = _audioSocket;
            if (socket == null) break;

            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    remoteEp,
                    _cts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize + MoonshineAudioPacketCodec.HeaderSize) continue;

                ReadOnlySpan<byte> datagram = buffer.AsSpan(0, result.ReceivedBytes);
                Interlocked.Increment(ref _totalAudioPacketsReceived);

                _audioPipeline?.IngestMoonshinePacket(datagram);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Handle transient UDP socket errors without crashing audio loop.
            catch (SocketException ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetFaultedState($"Audio socket error: {ex.Message}");
                break;
            }
            // ALLOWED_EXCEPTION: Catch unexpected exceptions in receive loop to transition to Faulted safely.
            catch (Exception ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetFaultedState($"Audio receive error: {ex.Message}");
                break;
            }
        }
    }

    private async Task ControlKeepAliveLoopAsync()
    {
        byte[] keepAliveBuffer = new byte[MoonshineProtocolConstants.HeaderSize];
        uint seq = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cts.Token).ConfigureAwait(false);

                Socket? socket = _controlSocket;
                if (socket == null) break;

                var header = new MoonshinePacketHeader(
                    Magic: MoonshineProtocolConstants.Magic,
                    Version: MoonshineProtocolConstants.Version10,
                    MessageType: MoonshineMessageType.KeepAlive,
                    PayloadSize: 0,
                    SequenceNumber: ++seq,
                    SessionId: _config.SessionId,
                    TimestampUs: (ulong)Stopwatch.GetTimestamp());

                if (MoonshineProtocolCodec.TryWriteHeader(in header, keepAliveBuffer))
                {
                    socket.SendTo(keepAliveBuffer, _hostControlEndpoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Keep-alive packet drops on transient network glitches are tolerated.
            catch (SocketException)
            {
            }
        }
    }

    /// <summary>
    /// Explicitly requests an immediate IDR keyframe from the streaming host.
    /// </summary>
    public void RequestIdrKeyframe(uint reasonCode = 1)
    {
        _feedbackReporter?.SendIdrRequest(reasonCode);
    }

    /// <summary>
    /// Dispatches a low-latency keyboard input event to the remote host.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SendKeyboardInput(ushort keyCode, ushort scanCode, bool isDown, byte modifiers = 0)
    {
        if (_disposed || State != ClientSessionState.Streaming || _controlSocket == null) return false;

        Span<byte> datagram = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 12];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputKeyboard,
            PayloadSize: 12,
            SequenceNumber: unchecked(++_inputSequenceNumber),
            SessionId: _config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var payload = new MoonshineInputKeyboardPayload
        {
            KeyCode = keyCode,
            ScanCode = scanCode,
            IsDown = (byte)(isDown ? 1 : 0),
            Modifiers = modifiers,
            Reserved = 0,
            TimestampOffsetUs = 0
        };

        if (MoonshineProtocolCodec.TryWriteHeader(in header, datagram) &&
            MoonshineProtocolCodec.TryWriteKeyboardInput(in payload, datagram[MoonshineProtocolConstants.HeaderSize..]))
        {
            try
            {
                _controlSocket.SendTo(datagram, _hostControlEndpoint);
                Interlocked.Increment(ref _totalInputPacketsSent);
                return true;
            }
            // ALLOWED_EXCEPTION: Transient socket exception on UDP send is ignored.
            catch (SocketException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Dispatches a low-latency mouse input event to the remote host.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SendMouseInput(int x, int y, short wheelY = 0, short wheelX = 0, ushort buttonFlags = 0, bool isAbsolute = false)
    {
        if (_disposed || State != ClientSessionState.Streaming || _controlSocket == null) return false;

        Span<byte> datagram = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 20];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputMouse,
            PayloadSize: 20,
            SequenceNumber: unchecked(++_inputSequenceNumber),
            SessionId: _config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var payload = new MoonshineInputMousePayload
        {
            X = x,
            Y = y,
            WheelDeltaY = wheelY,
            WheelDeltaX = wheelX,
            ButtonFlags = buttonFlags,
            IsAbsolute = (byte)(isAbsolute ? 1 : 0),
            Reserved = 0,
            TimestampOffsetUs = 0
        };

        if (MoonshineProtocolCodec.TryWriteHeader(in header, datagram) &&
            MoonshineProtocolCodec.TryWriteMouseInput(in payload, datagram[MoonshineProtocolConstants.HeaderSize..]))
        {
            try
            {
                _controlSocket.SendTo(datagram, _hostControlEndpoint);
                Interlocked.Increment(ref _totalInputPacketsSent);
                return true;
            }
            // ALLOWED_EXCEPTION: Transient socket exception on UDP send is ignored.
            catch (SocketException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Dispatches a low-latency gamepad input state to the remote host.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SendGamepadInput(
        byte gamepadIndex,
        ushort buttonMask,
        byte leftTrigger,
        byte rightTrigger,
        short thumbLx,
        short thumbLy,
        short thumbRx,
        short thumbRy,
        ushort motorLeft = 0,
        ushort motorRight = 0)
    {
        if (_disposed || State != ClientSessionState.Streaming || _controlSocket == null) return false;

        Span<byte> datagram = stackalloc byte[MoonshineProtocolConstants.HeaderSize + 24];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputGamepad,
            PayloadSize: 24,
            SequenceNumber: unchecked(++_inputSequenceNumber),
            SessionId: _config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var payload = new MoonshineInputGamepadPayload
        {
            GamepadIndex = gamepadIndex,
            Reserved = 0,
            ButtonMask = buttonMask,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            ThumbLx = thumbLx,
            ThumbLy = thumbLy,
            ThumbRx = thumbRx,
            ThumbRy = thumbRy,
            MotorLeft = motorLeft,
            MotorRight = motorRight,
            TimestampOffsetUs = 0,
            Reserved2 = 0
        };

        if (MoonshineProtocolCodec.TryWriteHeader(in header, datagram) &&
            MoonshineProtocolCodec.TryWriteGamepadInput(in payload, datagram[MoonshineProtocolConstants.HeaderSize..]))
        {
            try
            {
                _controlSocket.SendTo(datagram, _hostControlEndpoint);
                Interlocked.Increment(ref _totalInputPacketsSent);
                return true;
            }
            // ALLOWED_EXCEPTION: Transient socket exception on UDP send is ignored.
            catch (SocketException)
            {
                return false;
            }
        }

        return false;
    }

    private void TransitionStateLocked(ClientSessionState newState, string? error = null)
    {
        if (_state == newState) return;
        _state = newState;
        if (error != null) _lastError = error;
        OnStateChanged?.Invoke(newState, error);
    }

    private void SetFaultedState(string reason)
    {
        lock (_stateLock)
        {
            if (_state is ClientSessionState.Closed or ClientSessionState.Draining) return;
            TransitionStateLocked(ClientSessionState.Faulted, reason);
        }
    }

    /// <summary>
    /// Gracefully stops streaming and releases background workers.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is ClientSessionState.Closed or ClientSessionState.Draining) return;
            TransitionStateLocked(ClientSessionState.Draining);
        }

        // Send Teardown control datagram to host before disconnecting
        try
        {
            if (_controlSocket != null)
            {
                byte[] teardownBuffer = new byte[MoonshineProtocolConstants.HeaderSize];
                var header = new MoonshinePacketHeader(
                    Magic: MoonshineProtocolConstants.Magic,
                    Version: MoonshineProtocolConstants.Version10,
                    MessageType: MoonshineMessageType.Teardown,
                    PayloadSize: 0,
                    SequenceNumber: unchecked(++_inputSequenceNumber),
                    SessionId: _config.SessionId,
                    TimestampUs: (ulong)Stopwatch.GetTimestamp());

                if (MoonshineProtocolCodec.TryWriteHeader(in header, teardownBuffer))
                {
                    _controlSocket.SendTo(teardownBuffer, _hostControlEndpoint);
                }
            }
        }
        // ALLOWED_EXCEPTION: Ignore socket failures during best-effort teardown transmission.
        catch (SocketException)
        {
        }

        await CleanupResourcesAsync().ConfigureAwait(false);

        lock (_stateLock)
        {
            TransitionStateLocked(ClientSessionState.Closed);
        }
    }

    private async ValueTask CleanupResourcesAsync()
    {
        _cts.Cancel();

        // Await worker tasks
        var tasks = new List<Task>();
        if (_videoReceiveTask != null) tasks.Add(_videoReceiveTask);
        if (_audioReceiveTask != null) tasks.Add(_audioReceiveTask);
        if (_controlKeepAliveTask != null) tasks.Add(_controlKeepAliveTask);

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Task cancellation during shutdown is expected.
            catch (OperationCanceledException)
            {
            }
        }

        _feedbackReporter?.Dispose();
        _feedbackReporter = null;

        _videoSocket?.Dispose();
        _videoSocket = null;

        _audioSocket?.Dispose();
        _audioSocket = null;

        _controlSocket?.Dispose();
        _controlSocket = null;

        if (_ownsReassembly)
        {
            _reassemblyPipeline?.Dispose();
            _reassemblyPipeline = null;
        }

        if (_ownsAudio)
        {
            _audioPipeline?.Dispose();
            _audioPipeline = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
