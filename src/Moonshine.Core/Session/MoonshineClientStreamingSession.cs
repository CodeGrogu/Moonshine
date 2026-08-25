using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Moonshine.Core.Audio;
using Moonshine.Core.Control;
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
    public bool PerformHandshake { get; init; }
    public int HandshakeTimeoutMs { get; init; } = 3000;
    public MoonshineVideoCodec VideoCodec { get; init; } = MoonshineVideoCodec.Hevc;
    public MoonshineColorFormat ColorFormat { get; init; } = MoonshineColorFormat.Nv12;
    public uint VideoWidth { get; init; } = 1920;
    public uint VideoHeight { get; init; } = 1080;
    public uint VideoFps { get; init; } = 60;
    public uint VideoBitrateKbps { get; init; } = 20000;
    public uint AudioBitrateKbps { get; init; } = 128;
    public bool EnableMicrophoneUplink { get; init; }
    public int HostMicPort { get; init; } = 48002;
    public int LocalMicPort { get; init; }
    public double ConnectionTimeoutSeconds { get; init; } = 5.0;
    public bool AutoReconnect { get; init; }

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
    private ClientMicrophoneCapturePipeline? _micCapturePipeline;
    private MoonshineRemoteHostControlClient? _controlClient;

    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private Socket? _controlSocket;
    private Socket? _micSocket;

    private IPEndPoint _hostVideoEndpoint;
    private IPEndPoint _hostAudioEndpoint;
    private IPEndPoint _hostControlEndpoint;
    private IPEndPoint _hostMicEndpoint;

    private Task? _videoReceiveTask;
    private Task? _audioReceiveTask;
    private Task? _controlKeepAliveTask;
    private Task? _controlReceiveTask;

    private ClientSessionState _state = ClientSessionState.Created;
    private string? _lastError;
    private bool _disposed;

    // Metrics counters
    private ulong _totalVideoPacketsReceived;
    private ulong _totalVideoFramesCompleted;
    private ulong _totalAudioPacketsReceived;
    private ulong _totalInputPacketsSent;
    private uint _inputSequenceNumber;

    // Degraded state evaluation & health monitoring
    private int _degradedConsecutiveCleanWindows;
    private long _windowPacketsReceived;
    private long _lastWindowTimestampQpc;
    private long _lastHostActivityTimestampQpc;

    private readonly bool _ownsReassembly;
    private readonly bool _ownsAudio;
    private readonly bool _ownsMicCapture;
    private readonly MoonshineProtocolStateMachine _protocolStateMachine;

    public ClientSessionConfig Config => _config;
    public MoonshineProtocolStateMachine ProtocolStateMachine => _protocolStateMachine;
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

    public bool IsStreaming => State is ClientSessionState.Streaming or ClientSessionState.Degraded;

    public int BoundLocalVideoPort => (_videoSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalAudioPort => (_audioSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalControlPort => (_controlSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalMicPort => (_micSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    public Action<MoonshineFrameDesc>? OnVideoFrameReassembled { get; set; }
    public Action<ClientSessionState, string?>? OnStateChanged { get; set; }
    public MoonshineRemoteHostControlClient? ControlClient => _controlClient;

    public ClientSessionMetrics Metrics
    {
        get
        {
            var reassemblyMetrics = _reassemblyPipeline?.Metrics ?? default;
            var audioMetrics = _audioPipeline?.Metrics;

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
        MoonshineClientAudioPipeline? audioPipeline = null,
        ClientMicrophoneCapturePipeline? micCapturePipeline = null)
    {
        _config = config ?? ClientSessionConfig.Default;
        _hostVideoEndpoint = new IPEndPoint(_config.HostAddress, _config.HostVideoPort);
        _hostAudioEndpoint = new IPEndPoint(_config.HostAddress, _config.HostAudioPort);
        _hostControlEndpoint = new IPEndPoint(_config.HostAddress, _config.HostControlFeedbackPort);
        _hostMicEndpoint = new IPEndPoint(_config.HostAddress, _config.HostMicPort);

        _reassemblyPipeline = reassemblyPipeline;
        _ownsReassembly = reassemblyPipeline == null;

        _audioPipeline = audioPipeline;
        _ownsAudio = audioPipeline == null;

        _micCapturePipeline = micCapturePipeline;
        _ownsMicCapture = micCapturePipeline == null;
        _lastWindowTimestampQpc = Stopwatch.GetTimestamp();
        _protocolStateMachine = new MoonshineProtocolStateMachine(_config.SessionId, (uint)_config.MtuPayloadSize);
    }

    /// <summary>
    /// Starts the client streaming session, binding UDP media/control sockets, performing protocol negotiation (if configured), and launching reception workers.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is ClientSessionState.Streaming or ClientSessionState.Connecting or ClientSessionState.Negotiating or ClientSessionState.Degraded)
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

            // 2. Initialise Remote Host Control Client
            _controlClient = new MoonshineRemoteHostControlClient(_controlSocket, _hostControlEndpoint, _config.SessionId);

            // 3. Perform protocol handshake and stream capabilities negotiation if configured
            if (_config.PerformHandshake)
            {
                lock (_stateLock)
                {
                    TransitionStateLocked(ClientSessionState.Negotiating);
                }

                await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Initialise Video Reassembly Pipeline
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

            // 4. Initialise Client Audio Pipeline
            if (_audioPipeline == null)
            {
                _audioPipeline = new MoonshineClientAudioPipeline(
                    sampleRate: _config.AudioSampleRate,
                    channels: _config.AudioTopology,
                    isExclusive: false,
                    startBackgroundWorker: true);
            }

            // 5. Initialise Feedback Reporter
            _feedbackReporter = new MoonshineFeedbackReporter(
                streamId: _config.StreamId,
                sessionId: _config.SessionId,
                reportIntervalMs: _config.FeedbackIntervalMs,
                reassemblyPipeline: _reassemblyPipeline,
                socket: _controlSocket,
                remoteFeedbackEndpoint: _hostControlEndpoint);

            // 6. Initialise Client Microphone Capture Pipeline
            if (_config.EnableMicrophoneUplink)
            {
                _hostMicEndpoint = new IPEndPoint(_config.HostAddress, _config.HostMicPort);
                if (_micCapturePipeline == null)
                {
                    _micCapturePipeline = new ClientMicrophoneCapturePipeline(
                        sampleRate: 48000,
                        channels: 1,
                        bitrate: 32000,
                        frameDurationMs: 10,
                        streamId: _config.StreamId,
                        sessionId: _config.SessionId);
                }
            }

            // 7. Start background worker loops
            _videoReceiveTask = Task.Run(VideoReceiveLoopAsync, CancellationToken.None);
            _audioReceiveTask = Task.Run(AudioReceiveLoopAsync, CancellationToken.None);
            _controlKeepAliveTask = Task.Run(ControlKeepAliveLoopAsync, CancellationToken.None);
            _controlReceiveTask = Task.Run(ControlReceiveLoopAsync, CancellationToken.None);

            // 8. Transition to Streaming state
            lock (_stateLock)
            {
                TransitionStateLocked(ClientSessionState.Streaming);
            }
        }
        // ALLOWED_EXCEPTION: Fails closed on any network or initialisation fault and releases resources cleanly.
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

        if (_config.EnableMicrophoneUplink)
        {
            _micSocket?.Dispose();
            _micSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveBufferSize = 128 * 1024,
                SendBufferSize = 128 * 1024,
                ExclusiveAddressUse = false
            };
            _micSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _micSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalMicPort));
        }
    }

    private async Task PerformHandshakeAsync(CancellationToken cancellationToken)
    {
        if (_controlSocket == null) throw new InvalidOperationException("Control socket not initialised.");

        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(_config.HandshakeTimeoutMs);

        // 1. Send Hello
        _protocolStateMachine.RecordHelloSent();
        byte[] helloBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 32];
        var helloHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.Hello,
            PayloadSize: 32,
            SequenceNumber: 1,
            SessionId: _config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var helloPayload = new MoonshineHelloPayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 0,
            CapabilitiesMask = MoonshineCapabilities.Hevc | MoonshineCapabilities.ReedSolomonFec | MoonshineCapabilities.HighPollRateInput,
            ClientNonce = (ulong)Stopwatch.GetTimestamp(),
            ClientUuid = new MoonshineUuid128(Guid.NewGuid())
        };

        MoonshineProtocolCodec.TryWriteHeader(in helloHeader, helloBuffer);
        MoonshineProtocolCodec.TryWriteHello(in helloPayload, helloBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

        byte[] respBuffer = new byte[1024];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        // Send Hello & await HelloResponse
        await _controlSocket.SendToAsync(helloBuffer, SocketFlags.None, _hostControlEndpoint, handshakeCts.Token).ConfigureAwait(false);

        MoonshineHelloResponsePayload helloResp = default;
        bool receivedHelloResp = false;
        int packetsExamined = 0;
        int malformedPackets = 0;

        while (!receivedHelloResp && !handshakeCts.Token.IsCancellationRequested)
        {
            if (++packetsExamined > MoonshineProtocolStateMachine.MaxHandshakeExaminedPackets)
            {
                _protocolStateMachine.Fault("Handshake exceeded maximum allowed packet inspections.");
                throw new InvalidOperationException("Handshake exceeded maximum examined packet ceiling.");
            }

            SocketReceiveFromResult result = await _controlSocket.ReceiveFromAsync(respBuffer.AsMemory(), SocketFlags.None, remoteEp, handshakeCts.Token).ConfigureAwait(false);
            if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("Handshake exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("Handshake exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(respBuffer.AsSpan(0, result.ReceivedBytes), out var respHeader);
            if (err != MoonshineErrorCode.Success)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("Handshake exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("Handshake exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            err = _protocolStateMachine.IngestPacketHeader(in respHeader, (ulong)Stopwatch.GetTimestamp());
            if (err != MoonshineErrorCode.Success)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("Handshake exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("Handshake exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            if (respHeader.MessageType == MoonshineMessageType.HelloResponse)
            {
                if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize + 48)
                {
                    _protocolStateMachine.Fault("Truncated HelloResponse received from host.");
                    throw new InvalidOperationException("Invalid HelloResponse received from host.");
                }

                err = MoonshineProtocolCodec.TryReadHelloResponse(respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 48), out helloResp);
                if (err != MoonshineErrorCode.Success || helloResp.ServerVersionMajor < 1)
                {
                    _protocolStateMachine.Fault("Incompatible host protocol version in HelloResponse.");
                    throw new InvalidOperationException("Incompatible host protocol version.");
                }

                ulong assignedSessionId = helloResp.AssignedSessionId != 0 ? helloResp.AssignedSessionId : _config.SessionId;
                _protocolStateMachine.RecordHelloResponseReceived(assignedSessionId);
                receivedHelloResp = true;
                break;
            }
            else if (respHeader.MessageType is MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck)
            {
                // Ignore interleaved keepalive during handshake
                continue;
            }
            else
            {
                // Out-of-order unexpected handshake message
                _protocolStateMachine.Fault($"Expected HelloResponse but received {respHeader.MessageType}.");
                throw new InvalidOperationException($"Expected HelloResponse but received {respHeader.MessageType}.");
            }
        }

        if (!receivedHelloResp)
        {
            _protocolStateMachine.Fault("Timed out awaiting HelloResponse from host.");
            throw new TimeoutException("Timed out awaiting HelloResponse from host.");
        }

        // 2. Send SessionSetup
        _protocolStateMachine.RecordSessionSetupSent();
        byte[] setupBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 40];
        var setupHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SessionSetup,
            PayloadSize: 40,
            SequenceNumber: 2,
            SessionId: helloResp.AssignedSessionId != 0 ? helloResp.AssignedSessionId : _config.SessionId,
            TimestampUs: (ulong)Stopwatch.GetTimestamp());

        var setupPayload = new MoonshineSessionSetupPayload
        {
            VideoWidth = _config.VideoWidth,
            VideoHeight = _config.VideoHeight,
            VideoFps = _config.VideoFps,
            VideoBitrateKbps = _config.VideoBitrateKbps,
            VideoCodec = _config.VideoCodec,
            VideoColorFormat = _config.ColorFormat,
            AudioChannels = (byte)_config.AudioTopology,
            AudioCodec = MoonshineAudioCodec.Opus,
            AudioSampleRate = _config.AudioSampleRate,
            AudioBitrateKbps = _config.AudioBitrateKbps,
            ClientUdpVideoPort = (ushort)BoundLocalVideoPort,
            ClientUdpAudioPort = (ushort)BoundLocalAudioPort,
            ClientUdpFeedbackPort = (ushort)BoundLocalControlPort,
            Reserved = 0,
            MtuPayloadSize = (uint)_config.MtuPayloadSize
        };

        MoonshineProtocolCodec.TryWriteHeader(in setupHeader, setupBuffer);
        MoonshineProtocolCodec.TryWriteSessionSetup(in setupPayload, setupBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

        await _controlSocket.SendToAsync(setupBuffer, SocketFlags.None, _hostControlEndpoint, handshakeCts.Token).ConfigureAwait(false);

        // Await SessionSetupResponse
        bool receivedSetupResp = false;
        packetsExamined = 0;
        malformedPackets = 0;

        while (!receivedSetupResp && !handshakeCts.Token.IsCancellationRequested)
        {
            if (++packetsExamined > MoonshineProtocolStateMachine.MaxHandshakeExaminedPackets)
            {
                _protocolStateMachine.Fault("SessionSetup exceeded maximum allowed packet inspections.");
                throw new InvalidOperationException("SessionSetup exceeded maximum examined packet ceiling.");
            }

            SocketReceiveFromResult result = await _controlSocket.ReceiveFromAsync(respBuffer.AsMemory(), SocketFlags.None, remoteEp, handshakeCts.Token).ConfigureAwait(false);
            if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("SessionSetup exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("SessionSetup exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(respBuffer.AsSpan(0, result.ReceivedBytes), out var setupRespHeader);
            if (err != MoonshineErrorCode.Success)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("SessionSetup exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("SessionSetup exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            err = _protocolStateMachine.IngestPacketHeader(in setupRespHeader, (ulong)Stopwatch.GetTimestamp());
            if (err != MoonshineErrorCode.Success)
            {
                if (++malformedPackets > MoonshineProtocolStateMachine.MaxHandshakeMalformedPackets)
                {
                    _protocolStateMachine.Fault("SessionSetup exceeded maximum malformed packet ceiling.");
                    throw new InvalidOperationException("SessionSetup exceeded maximum malformed packet ceiling.");
                }
                continue;
            }

            if (setupRespHeader.MessageType == MoonshineMessageType.SessionSetupResponse)
            {
                if (result.ReceivedBytes < MoonshineProtocolConstants.HeaderSize + 32)
                {
                    _protocolStateMachine.Fault("Truncated SessionSetupResponse received from host.");
                    throw new InvalidOperationException("Invalid SessionSetupResponse received from host.");
                }

                err = MoonshineProtocolCodec.TryReadSessionSetupResponse(respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 32), out var setupResp);
                if (err != MoonshineErrorCode.Success || setupResp.StatusCode != MoonshineErrorCode.Success)
                {
                    _protocolStateMachine.Fault($"Host rejected session setup with code {setupResp.StatusCode}.");
                    throw new InvalidOperationException($"Host rejected session setup with code {setupResp.StatusCode}.");
                }

                _protocolStateMachine.RecordSessionSetupResponseReceived(
                    setupResp.VideoStreamId != 0 ? setupResp.VideoStreamId : 1,
                    setupResp.AudioStreamId != 0 ? setupResp.AudioStreamId : 2,
                    setupResp.FeedbackStreamId != 0 ? setupResp.FeedbackStreamId : 3,
                    setupResp.NegotiatedMtu != 0 ? setupResp.NegotiatedMtu : (uint)_config.MtuPayloadSize);

                // Configure host destinations from response if specified
                if (setupResp.HostUdpVideoPort > 0)
                {
                    _hostVideoEndpoint = new IPEndPoint(_config.HostAddress, setupResp.HostUdpVideoPort);
                }
                if (setupResp.HostUdpAudioPort > 0)
                {
                    _hostAudioEndpoint = new IPEndPoint(_config.HostAddress, setupResp.HostUdpAudioPort);
                }

                receivedSetupResp = true;
                break;
            }
            else if (setupRespHeader.MessageType is MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck)
            {
                continue;
            }
            else
            {
                _protocolStateMachine.Fault($"Expected SessionSetupResponse but received {setupRespHeader.MessageType}.");
                throw new InvalidOperationException($"Expected SessionSetupResponse but received {setupRespHeader.MessageType}.");
            }
        }

        if (!receivedSetupResp)
        {
            _protocolStateMachine.Fault("Timed out awaiting SessionSetupResponse from host.");
            throw new TimeoutException("Timed out awaiting SessionSetupResponse from host.");
        }

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
                Interlocked.Increment(ref _windowPacketsReceived);
                Interlocked.Exchange(ref _lastHostActivityTimestampQpc, Stopwatch.GetTimestamp());

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

                    // Evaluate Degraded state transitions periodically
                    EvaluateNetworkHealth();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Handle transient UDP socket errors (like WSAECONNRESET on Windows) without crashing loop.
            catch (SocketException)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Evaluates sliding-window loss rate and jitter to drive Streaming &lt;-&gt; Degraded state transitions with hysteresis dampening.
    /// </summary>
    public void EvaluateNetworkHealth()
    {
        long nowQpc = Stopwatch.GetTimestamp();

        // 1. Connection Timeout Check
        if (_config.ConnectionTimeoutSeconds > 0)
        {
            double secSinceHostActivity = (nowQpc - Interlocked.Read(ref _lastHostActivityTimestampQpc)) / (double)Stopwatch.Frequency;
            if (secSinceHostActivity > _config.ConnectionTimeoutSeconds && IsStreaming)
            {
                SetFaultedState("Host connection timed out (no packets or heartbeats received).");
                return;
            }
        }

        // 2. Network Quality Evaluation
        double elapsedSec = (nowQpc - _lastWindowTimestampQpc) / (double)Stopwatch.Frequency;
        if (elapsedSec < 0.5) return;

        _lastWindowTimestampQpc = nowQpc;
        long rx = Interlocked.Exchange(ref _windowPacketsReceived, 0);

        var pipelineMetrics = _reassemblyPipeline?.Metrics ?? default;
        long lost = (long)pipelineMetrics.PacketsLost;
        double lossRate = (rx + lost) > 0 ? (double)lost / (rx + lost) : 0.0;
        double jitterUs = pipelineMetrics.AverageJitterMicroseconds;
        uint rttUs = _feedbackReporter?.RoundTripTimeUs ?? 0;

        lock (_stateLock)
        {
            if (_state == ClientSessionState.Streaming)
            {
                if (lossRate > 0.05 || jitterUs > 15000 || rttUs > 100000)
                {
                    _degradedConsecutiveCleanWindows = 0;
                    TransitionStateLocked(ClientSessionState.Degraded);
                }
            }
            else if (_state == ClientSessionState.Degraded)
            {
                if (lossRate < 0.01 && jitterUs < 5000 && rttUs < 50000)
                {
                    _degradedConsecutiveCleanWindows++;
                    if (_degradedConsecutiveCleanWindows >= 4) // 4 * 0.5s = 2.0s clean hysteresis
                    {
                        _degradedConsecutiveCleanWindows = 0;
                        TransitionStateLocked(ClientSessionState.Streaming);
                    }
                }
                else
                {
                    _degradedConsecutiveCleanWindows = 0;
                }
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
                Interlocked.Exchange(ref _lastHostActivityTimestampQpc, Stopwatch.GetTimestamp());

                _audioPipeline?.IngestMoonshinePacket(datagram);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Handle transient UDP socket errors (like WSAECONNRESET on Windows) without crashing loop.
            catch (SocketException)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
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

    private async Task ControlReceiveLoopAsync()
    {
        byte[] buffer = new byte[2048];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket = _controlSocket;
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
                MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(datagram, out var header);
                if (err != MoonshineErrorCode.Success || header.Magic != MoonshineProtocolConstants.Magic) continue;

                // Update activity timestamp on any valid packet from host
                Interlocked.Exchange(ref _lastHostActivityTimestampQpc, Stopwatch.GetTimestamp());

                _controlClient?.ProcessIncomingControlMessage(datagram);

                switch (header.MessageType)
                {
                    case MoonshineMessageType.KeepAliveAck:
                        // Calculate roundtrip latency if timestamp is present
                        if (header.TimestampUs > 0)
                        {
                            ulong nowUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
                            if (nowUs >= header.TimestampUs)
                            {
                                uint rtt = (uint)(nowUs - header.TimestampUs);
                                _feedbackReporter?.UpdateRtt(rtt);
                            }
                        }
                        break;

                    case MoonshineMessageType.Teardown:
                        // Host initiated teardown
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await StopAsync().ConfigureAwait(false);
                            }
                            // ALLOWED_EXCEPTION: Fire-and-forget teardown on Teardown control message; failure logged via session state.
                            catch { }
                        });
                        break;

                    case MoonshineMessageType.IdrRequest:
                        // Host IDR acknowledgement / request
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Handle transient UDP socket errors (like WSAECONNRESET on Windows) without crashing loop.
            catch (SocketException)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Catch unexpected exceptions in receive loop to transition to Faulted safely.
            catch (Exception ex)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetFaultedState($"Control receive error: {ex.Message}");
                break;
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
        if (_disposed || !IsStreaming || _controlSocket == null) return false;

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
        if (_disposed || !IsStreaming || _controlSocket == null) return false;

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
        if (_disposed || !IsStreaming || _controlSocket == null) return false;

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

    /// <summary>
    /// Encodes and transmits a recorded client microphone Float32 PCM frame to the host backchannel uplink.
    /// </summary>
    public bool TrySendMicrophoneFrame(ReadOnlySpan<float> pcmSamples)
    {
        if (_disposed || !IsStreaming || _micCapturePipeline == null || _micSocket == null || _hostMicEndpoint == null)
        {
            return false;
        }

        Span<byte> outDatagram = stackalloc byte[2048];
        if (!_micCapturePipeline.TryProcessRecordedFrame(pcmSamples, outDatagram, out int written, preferMoonshineFraming: true))
        {
            return false;
        }

        if (written <= 0)
        {
            return false;
        }

        try
        {
            int sent = _micSocket.SendTo(outDatagram[..written], SocketFlags.None, _hostMicEndpoint);
            return sent == written;
        }
        // ALLOWED_EXCEPTION: Transient UDP send failure on network drop.
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sets client microphone input gain multiplier clamped between 0.0 and 10.0.
    /// </summary>
    public void SetMicrophoneGain(float gain) => _micCapturePipeline?.SetGain(gain);

    /// <summary>
    /// Sets client microphone mute state.
    /// </summary>
    public void SetMicrophoneMute(bool isMuted) => _micCapturePipeline?.SetMute(isMuted);

    private void TransitionStateLocked(ClientSessionState newState, string? error = null)
    {
        if (_state == newState) return;
        _state = newState;
        if (error != null) _lastError = error;

        switch (newState)
        {
            case ClientSessionState.Streaming:
                if (_protocolStateMachine.State == MoonshineProtocolState.StreamingDegraded)
                {
                    _protocolStateMachine.SetDegraded(false);
                }
                break;
            case ClientSessionState.Degraded:
                _protocolStateMachine.SetDegraded(true);
                break;
            case ClientSessionState.Draining:
                _protocolStateMachine.RecordTeardown();
                break;
            case ClientSessionState.Closed:
                _protocolStateMachine.Close();
                break;
            case ClientSessionState.Faulted:
                _protocolStateMachine.Fault(error ?? "Client session faulted.");
                break;
        }

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
        if (_controlReceiveTask != null) tasks.Add(_controlReceiveTask);

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

        _controlClient?.Dispose();
        _controlClient = null;

        _videoSocket?.Dispose();
        _videoSocket = null;

        _audioSocket?.Dispose();
        _audioSocket = null;

        _controlSocket?.Dispose();
        _controlSocket = null;

        if (_ownsMicCapture)
        {
            _micCapturePipeline?.Dispose();
            _micCapturePipeline = null;
        }

        _micSocket?.Dispose();
        _micSocket = null;

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
