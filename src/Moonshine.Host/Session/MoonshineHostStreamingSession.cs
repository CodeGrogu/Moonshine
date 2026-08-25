using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Congestion;
using Moonshine.Core.Media;
using Moonshine.Core.Security;
using Moonshine.Core.Transport;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Control;
using Moonshine.Protocol.Feedback;
using MoonshineErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;

namespace Moonshine.Host.Session;

/// <summary>
/// Production Moonshine host streaming session orchestrator.
/// Connects and coordinates capture, hardware encoder, audio pipeline, packetisers,
/// input injector, congestion controller, and network transports into a deterministic session.
/// Enforces fail-closed backend invariants and zero GC allocations on steady-state streaming paths.
/// </summary>
public sealed class MoonshineHostStreamingSession : IAsyncDisposable, IDisposable
{
    private readonly HostSessionConfig _config;
    private readonly Lock _stateLock = new();
    private readonly CancellationTokenSource _cts = new();

    private IDesktopCapturePipeline? _capturePipeline;
    private UnifiedHardwareEncoderEngine? _encoderEngine;
    private MoonshineHostAudioPipeline? _audioPipeline;
    private MoonshineHostInputPipeline? _inputPipeline;
    private IDisplayTopologyWatcher? _topologyWatcher;
    private MoonshineVideoPacketiser? _videoPacketiser;
    private CongestionController? _congestionController;
    private readonly HostConfigurationService _configurationService;
    private readonly MoonshineSessionAuthenticator? _authenticator;
    private uint _currentFps;

    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private Socket? _controlSocket;
    private Socket? _micSocket;

    private IPEndPoint _clientVideoEndpoint;
    private IPEndPoint _clientAudioEndpoint;
    private IPEndPoint _clientControlEndpoint;

    private Task? _videoLoopTask;
    private Task? _controlFeedbackLoopTask;
    private Task? _micUplinkLoopTask;
    private HostMicrophoneUplinkService? _micUplinkService;

    private HostSessionState _state = HostSessionState.Created;
    private string? _lastError;
    private bool _disposed;
    private bool _forceNextIdr;
    private Task? _stopTask;

    // Metrics counters
    private ulong _totalFramesCaptured;
    private ulong _totalFramesEncoded;
    private ulong _totalPacketsSent;
    private ulong _totalBytesSent;
    private ulong _totalAudioFramesCaptured;
    private ulong _totalAudioPacketsSent;
    private ulong _totalInputPacketsProcessed;
    private ulong _keyframesRequested;
    private double _averageCaptureToNetworkLatencyUs;
    private uint _pacingAdjustmentUs;

    private readonly bool _ownsCapture;
    private readonly bool _ownsEncoder;
    private readonly bool _ownsAudio;
    private readonly bool _ownsInput;
    private readonly bool _ownsMicUplink;
    private readonly MoonshineProtocolStateMachine _protocolStateMachine;

    public HostSessionConfig Config => _config;
    public MoonshineProtocolStateMachine ProtocolStateMachine => _protocolStateMachine;
    public HostSessionState State
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

    public bool IsStreaming => State == HostSessionState.Streaming;

    public HostConfigurationService ConfigurationService => _configurationService;

    private ulong _currentTopologyGeneration;
    public ulong CurrentTopologyGeneration => Volatile.Read(ref _currentTopologyGeneration);

    public int BoundLocalVideoPort => (_videoSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalAudioPort => (_audioSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalControlPort => (_controlSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public int BoundLocalMicPort => (_micSocket?.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    public void SetClientEndpoints(IPEndPoint videoEp, IPEndPoint audioEp, IPEndPoint controlEp)
    {
        _clientVideoEndpoint = videoEp;
        _clientAudioEndpoint = audioEp;
        _clientControlEndpoint = controlEp;
    }

    public HostSessionMetrics Metrics => new(
        Interlocked.Read(ref _totalFramesCaptured),
        Interlocked.Read(ref _totalFramesEncoded),
        Interlocked.Read(ref _totalPacketsSent),
        Interlocked.Read(ref _totalBytesSent),
        Interlocked.Read(ref _totalAudioFramesCaptured),
        Interlocked.Read(ref _totalAudioPacketsSent),
        Interlocked.Read(ref _totalInputPacketsProcessed),
        Volatile.Read(ref _averageCaptureToNetworkLatencyUs),
        _congestionController?.CurrentBitrateKbps ?? _config.BitrateKbps,
        Interlocked.Read(ref _keyframesRequested),
        State,
        LastError,
        _micUplinkService?.GetMetrics());

    /// <summary>
    /// Evaluates real-time live backend readiness across all host streaming subsystems.
    /// ComponentReadiness.Operational is reported only when the backend is active and performing live streaming.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Native virtual audio driver query may fail if PortCls or C-ABI runtime is not present.")]
    public HostBackendReadiness GetLiveBackendReadiness()
    {
        bool anyEncoderSupported = (_encoderEngine != null && _encoderEngine.IsActive) ||
                                  HostCapabilityProbeEngine.IsAnyHardwareEncoderSupported();

        ComponentReadiness videoEncoder;
        if (State == HostSessionState.Faulted || (_encoderEngine?.IsActive == false && IsStreaming && State != HostSessionState.InitializingBackends))
        {
            videoEncoder = ComponentReadiness.Faulted;
        }
        else if (_encoderEngine != null && _encoderEngine.IsActive && IsStreaming)
        {
            // Operational is reported ONLY when the backend is a real hardware-accelerated encoder that has produced valid output.
            // Synthetic test or placeholder encoders report Available, NEVER Operational.
            // The AND-gate (ImplementationKind == HardwareAccelerated && HasProducedValidOutput) is strictly load-bearing for operational safety across all vendors and must never be bypassed.
            if (_encoderEngine.ImplementationKind == EncoderImplementationKind.HardwareAccelerated && _encoderEngine.HasProducedValidOutput)
            {
                videoEncoder = ComponentReadiness.Operational;
            }
            else
            {
                videoEncoder = ComponentReadiness.Available;
            }
        }
        else
        {
            videoEncoder = anyEncoderSupported ? ComponentReadiness.Available : ComponentReadiness.Unsupported;
        }

        DisplayTopology topology = _topologyWatcher?.CurrentTopology ?? DisplayManager.GetDisplayTopology();
        var adapters = topology.Adapters.Count > 0 ? topology.Adapters : DisplayManager.GetPhysicalAdapters();

        uint attachedDisplayCount = 0;
        for (int i = 0; i < topology.Displays.Count; i++)
        {
            if (topology.Displays[i].IsAttachedToDesktop)
            {
                attachedDisplayCount++;
            }
        }

        bool isHeadless = topology.IsHeadless || attachedDisplayCount == 0;

        ComponentReadiness desktopCapture;
        if (_capturePipeline?.IsAvailable == true && IsStreaming)
        {
            desktopCapture = ComponentReadiness.Operational;
        }
        else if (isHeadless && attachedDisplayCount == 0)
        {
            desktopCapture = ComponentReadiness.Unsupported;
        }
        else if (_capturePipeline == null && isHeadless)
        {
            desktopCapture = ComponentReadiness.Unsupported;
        }
        else if (!IsStreaming)
        {
            desktopCapture = (!isHeadless && attachedDisplayCount > 0)
                ? ComponentReadiness.Available
                : ComponentReadiness.Unsupported;
        }
        else
        {
            desktopCapture = ComponentReadiness.Faulted;
        }

        ComponentReadiness audioLoopback;
        if (_config.AudioTopology == AudioChannelTopology.None)
        {
            audioLoopback = ComponentReadiness.Unsupported;
        }
        else if (_audioPipeline?.IsStreaming == true && IsStreaming)
        {
            audioLoopback = ComponentReadiness.Operational;
        }
        else if (!IsStreaming)
        {
            audioLoopback = HostCapabilityProbeEngine.HasActiveRenderEndpoint()
                ? ComponentReadiness.Available
                : ComponentReadiness.Unsupported;
        }
        else
        {
            audioLoopback = ComponentReadiness.Faulted;
        }

        DriverInstallationState audioDriverState;
        try
        {
            using var driverService = new VirtualAudioDriverService();
            audioDriverState = driverService.GetInstallationState();
        }
        // ALLOWED_EXCEPTION: Native virtual audio driver query may fail if PortCls or C-ABI runtime is not present.
        catch (Exception)
        {
            audioDriverState = DriverInstallationState.Error;
        }

        ComponentReadiness virtualAudioDriver;
        if (_audioPipeline?.IpcBridge?.IsConnected == true && IsStreaming)
        {
            virtualAudioDriver = ComponentReadiness.Operational;
        }
        else
        {
            virtualAudioDriver = audioDriverState switch
            {
                DriverInstallationState.EndpointsActive => ComponentReadiness.Available,
                DriverInstallationState.Error => ComponentReadiness.Faulted,
                _ => ComponentReadiness.Unsupported
            };
        }

        ComponentReadiness microphoneBackchannel;
        if (!_config.EnableMicrophoneBackchannel)
        {
            microphoneBackchannel = ComponentReadiness.Unsupported;
        }
        else if (_micUplinkService != null && _micUplinkService.IsRunning && _micUplinkService.IsInitialized && IsStreaming)
        {
            microphoneBackchannel = ComponentReadiness.Operational;
        }
        else if (!IsStreaming)
        {
            microphoneBackchannel = audioDriverState switch
            {
                DriverInstallationState.EndpointsActive => ComponentReadiness.Available,
                DriverInstallationState.Error => ComponentReadiness.Faulted,
                _ => ComponentReadiness.Unsupported
            };
        }
        else
        {
            microphoneBackchannel = ComponentReadiness.Faulted;
        }

        DisplayAdapterInfo? primaryGpu = null;
        if (topology.PrimaryDisplay != null)
        {
            primaryGpu = HostCapabilityProbeEngine.FindAdapter(adapters, topology.PrimaryDisplay.AdapterIndex);
        }
        primaryGpu ??= HostCapabilityProbeEngine.FindPreferredAdapter(adapters);
        string primaryGpuName = primaryGpu?.Description ?? string.Empty;

        return new HostBackendReadiness(
            VideoEncoder: videoEncoder,
            DesktopCapture: desktopCapture,
            AudioLoopback: audioLoopback,
            VirtualAudioDriver: virtualAudioDriver,
            MicrophoneBackchannel: microphoneBackchannel,
            PrimaryGpuName: primaryGpuName,
            AttachedDisplayCount: attachedDisplayCount,
            IsHeadless: isHeadless
        );
    }

    public MoonshineHostStreamingSession(
        HostSessionConfig? config = null,
        IDesktopCapturePipeline? capturePipeline = null,
        UnifiedHardwareEncoderEngine? encoderEngine = null,
        MoonshineHostAudioPipeline? audioPipeline = null,
        MoonshineHostInputPipeline? inputPipeline = null,
        IDisplayTopologyWatcher? topologyWatcher = null,
        HostMicrophoneUplinkService? micUplinkService = null,
        HostConfigurationService? configurationService = null,
        MoonshineSessionAuthenticator? authenticator = null)
    {
        _config = config ?? HostSessionConfig.Default;
        _currentFps = _config.Fps > 0 ? _config.Fps : 60;
        _authenticator = authenticator;
        _clientVideoEndpoint = new IPEndPoint(_config.ClientAddress, _config.ClientVideoPort);
        _clientAudioEndpoint = new IPEndPoint(_config.ClientAddress, _config.ClientAudioPort);
        _clientControlEndpoint = new IPEndPoint(_config.ClientAddress, _config.ClientControlFeedbackPort);

        _capturePipeline = capturePipeline;
        _ownsCapture = capturePipeline == null;

        _encoderEngine = encoderEngine;
        _ownsEncoder = encoderEngine == null;

        _audioPipeline = audioPipeline;
        _ownsAudio = audioPipeline == null;

        _inputPipeline = inputPipeline;
        _ownsInput = inputPipeline == null;

        _topologyWatcher = topologyWatcher;

        _micUplinkService = micUplinkService;
        _ownsMicUplink = micUplinkService == null;

        if (configurationService != null)
        {
            _configurationService = configurationService;
        }
        else
        {
            var capabilities = new MoonshineHostCapabilitiesResponsePayload
            {
                SupportedVideoCodecs = (uint)(MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264),
                SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
                MaxEncodeWidth = 3840,
                MaxEncodeHeight = 2160,
                MaxEncodeFps = 240,
                SupportsHdr10 = (byte)(_config.EnableHdr10 ? 1 : 1),
                SupportsVirtualAudio = 1,
                SupportsMicBackchannel = (byte)(_config.EnableMicrophoneBackchannel ? 1 : 0),
                Reserved = 0,
                MaxBitrateKbps = Math.Max(150000, _config.BitrateKbps * 2),
                Reserved2 = 0
            };

            var initialConfig = new MoonshineHostConfigurationPayload
            {
                ConfigVersion = 1,
                DisplayWidth = _config.Width,
                DisplayHeight = _config.Height,
                RefreshRateHz = _config.Fps,
                TargetBitrateKbps = _config.BitrateKbps,
                MaxBitrateKbps = Math.Max(_config.BitrateKbps * 2, 50000),
                PreferredCodec = _config.Codec switch
                {
                    VideoCodec.Av1 => MoonshineVideoCodec.Av1,
                    VideoCodec.HevcMain10 or VideoCodec.Hevc => MoonshineVideoCodec.Hevc,
                    VideoCodec.H264 => MoonshineVideoCodec.H264,
                    _ => MoonshineVideoCodec.Hevc
                },
                Hdr10Enabled = (byte)(_config.EnableHdr10 ? 1 : 0),
                AudioChannels = (byte)(_config.AudioTopology switch
                {
                    AudioChannelTopology.Surround51 => 6,
                    AudioChannelTopology.Surround71 => 8,
                    _ => 2
                }),
                AudioQualityMode = 0,
                AudioBitrateKbps = _config.AudioBitrate > 0 ? _config.AudioBitrate / 1000 : 128,
                InputPollingRateHz = 1000,
                MicPassthroughEnabled = (byte)(_config.EnableMicrophoneBackchannel ? 1 : 0),
                VirtualAudioDriverEnabled = 1,
                Reserved1 = 0,
                Reserved2 = 0,
                Reserved3 = 0
            };

            _configurationService = new HostConfigurationService(capabilities, initialConfig);
        }

        _protocolStateMachine = new MoonshineProtocolStateMachine(_config.SessionId, (uint)_config.MtuPayloadSize);
    }

    /// <summary>
    /// Starts the streaming session, strictly validating backend invariants before entering Streaming state.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is HostSessionState.Streaming or HostSessionState.InitializingBackends)
            {
                return;
            }

            _state = HostSessionState.InitializingBackends;
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Initialise sockets
            InitSockets();

            // 2. Initialise Video Packetiser
            int fecData = _config.EnableFec ? _config.FecDataShards : 0;
            int fecParity = _config.EnableFec ? _config.FecParityShards : 0;
            _videoPacketiser = new MoonshineVideoPacketiser(
                _config.StreamId,
                _config.SessionId,
                _config.MtuPayloadSize,
                fecData,
                fecParity);

            // 3. Initialise Congestion Controller
            _congestionController = new CongestionController(
                _config.BitrateKbps,
                minBitrateKbps: Math.Max(2000, _config.BitrateKbps / 10),
                maxBitrateKbps: (uint)(_config.BitrateKbps * 2.5),
                onBitrateChanged: newBitrate => _encoderEngine?.ReconfigureBitrate(newBitrate),
                onPacingChanged: pacing => Volatile.Write(ref _pacingAdjustmentUs, pacing),
                onIdrRequested: RequestKeyframe);

            // 4. Initialise Desktop Capture Pipeline
            if (_capturePipeline == null)
            {
                _capturePipeline = new UnifiedDesktopCaptureEngine(
                    CaptureBackend.Automatic,
                    _config.Fps);
            }

            if (!_capturePipeline.IsAvailable)
            {
                throw new InvalidOperationException("Desktop capture pipeline is unavailable (no supported display adapter or capture interface).");
            }

            // 5. Initialise Hardware Video Encoder Engine
            if (_encoderEngine == null)
            {
                _encoderEngine = new UnifiedHardwareEncoderEngine(
                    _config.Width,
                    _config.Height,
                    _config.Fps,
                    _config.BitrateKbps,
                    _config.Codec,
                    _config.RateControl);
            }

            if (!_encoderEngine.IsActive)
            {
                throw new InvalidOperationException("Hardware video encoder is unavailable or failed to initialise.");
            }

            // 6. Initialise Host Audio Pipeline
            if (_audioPipeline == null)
            {
                _audioPipeline = new MoonshineHostAudioPipeline(
                    sampleRate: 48000,
                    topology: _config.AudioTopology,
                    bitrate: _config.AudioBitrate,
                    frameDurationMs: 5);
            }

            // 7. Initialise Host Input Pipeline
            if (_inputPipeline == null)
            {
                _inputPipeline = new MoonshineHostInputPipeline(
                    config: new HostInputConfig
                    {
                        ScreenWidth = (int)_config.Width,
                        ScreenHeight = (int)_config.Height,
                        ExpectedSessionId = _config.SessionId
                    });
            }

            // 8. Start Audio Streaming worker
            _audioPipeline.Start(OnAudioPacketEncoded);

            // 9. Initialise Microphone Uplink if enabled
            if (_config.EnableMicrophoneBackchannel)
            {
                if (_micUplinkService == null)
                {
                    _micUplinkService = new HostMicrophoneUplinkService(
                        sampleRate: 48000,
                        channels: 1,
                        frameDurationMs: 10,
                        ipcBridge: _audioPipeline.IpcBridge,
                        autoStartWorker: true);
                }

                _micUplinkLoopTask = Task.Run(MicrophoneUplinkLoopAsync, CancellationToken.None);
            }

            // 10. Start Video loop and Control Feedback loop
            _videoLoopTask = Task.Run(VideoFrameLoopAsync, CancellationToken.None);
            _controlFeedbackLoopTask = Task.Run(ControlFeedbackLoopAsync, CancellationToken.None);

            // 11. Hook Display Topology Watcher if provided
            if (_topologyWatcher != null)
            {
                Volatile.Write(ref _currentTopologyGeneration, _topologyWatcher.CurrentTopology.Generation);
                _topologyWatcher.TopologyChanged += OnDisplayTopologyChanged;
            }

            // 12. Transition to Streaming state only after all backends are verified
            lock (_stateLock)
            {
                _state = HostSessionState.Streaming;
                _protocolStateMachine.RecordHelloResponseReceived(_config.SessionId);
                _protocolStateMachine.RecordSessionSetupSent();
                _protocolStateMachine.RecordSessionSetupResponseReceived(1, 2, 3, (uint)_config.MtuPayloadSize);
            }
        }
        // ALLOWED_EXCEPTION: Fails closed on any backend initialization fault and cleans up all acquired resources.
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _state = HostSessionState.Faulted;
                _lastError = ex.Message;
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
            SendBufferSize = 1024 * 1024,
            ReceiveBufferSize = 256 * 1024,
            ExclusiveAddressUse = false
        };
        _videoSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _videoSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalVideoPort));

        _audioSocket?.Dispose();
        _audioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 256 * 1024,
            ReceiveBufferSize = 256 * 1024,
            ExclusiveAddressUse = false
        };
        _audioSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _audioSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalAudioPort));

        _controlSocket?.Dispose();
        _controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 128 * 1024,
            ReceiveBufferSize = 128 * 1024,
            ExclusiveAddressUse = false
        };
        _controlSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _controlSocket.Bind(new IPEndPoint(IPAddress.Any, _config.LocalControlFeedbackPort));

        if (_config.EnableMicrophoneBackchannel)
        {
            _micSocket?.Dispose();
            _micSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                SendBufferSize = 128 * 1024,
                ReceiveBufferSize = 256 * 1024,
                ExclusiveAddressUse = false
            };
            _micSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            int micPort = _config.LocalMicPort != 0 ? _config.LocalMicPort : _config.MicUdpPort;
            _micSocket.Bind(new IPEndPoint(IPAddress.Any, micPort));
        }
    }

    /// <summary>
    /// Requests an immediate IDR keyframe from the hardware video encoder on the next captured frame.
    /// </summary>
    public void RequestKeyframe()
    {
        Interlocked.Increment(ref _keyframesRequested);
        Volatile.Write(ref _forceNextIdr, true);
    }

    private void OnDisplayTopologyChanged(object? sender, DisplayTopologyChangedEventArgs e)
    {
        HandleDisplayTopologyChanged(e);
    }

    /// <summary>
    /// Handles asynchronous display topology changes and coordinates capture pipeline reconfiguration.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Rule 5 - Protects display event handler from unhandled capture reconfiguration exceptions.")]
    public void HandleDisplayTopologyChanged(DisplayTopologyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        lock (_stateLock)
        {
            if (_disposed || _state != HostSessionState.Streaming) return;
        }

        Volatile.Write(ref _currentTopologyGeneration, e.NewTopology.Generation);

        if (e.NewTopology.IsHeadless)
        {
            // Headless transition: all displays detached.
            return;
        }

        if (_capturePipeline != null)
        {
            // Only request an IDR keyframe if a capture transition or recovery succeeded.
            // When capture is unrecoverable or broken, spurious keyframes are avoided.
            bool captureTransitionRequiresKeyframe = false;

            try
            {
                var selectResult = CaptureSourceSelector.SelectSource(e.NewTopology, new CaptureSourceSelectionCriteria(
                    Policy: CaptureSelectionPolicy.MatchResolution,
                    TargetWidth: _config.Width,
                    TargetHeight: _config.Height,
                    TargetFps: _config.Fps,
                    RequireHdr: _config.EnableHdr10,
                    FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary
                ));

                if (selectResult.IsSuccess && selectResult.Source != null)
                {
                    if (_capturePipeline.TryReconfigureSource(selectResult.Source))
                    {
                        captureTransitionRequiresKeyframe = true;
                    }
                    else if (_capturePipeline.TryRecover())
                    {
                        captureTransitionRequiresKeyframe = true;
                    }
                }
                else
                {
                    if (_capturePipeline.TryRecover())
                    {
                        captureTransitionRequiresKeyframe = true;
                    }
                }
            }
            // ALLOWED_EXCEPTION: Rule 5 - Protects display event handler from unhandled capture reconfiguration exceptions.
            catch (Exception)
            {
                try
                {
                    if (_capturePipeline.TryRecover())
                    {
                        captureTransitionRequiresKeyframe = true;
                    }
                }
                // ALLOWED_EXCEPTION: Rule 5 - Protects display event handler if secondary recovery attempt throws.
                catch (Exception)
                {
                }
            }
            finally
            {
                if (captureTransitionRequiresKeyframe)
                {
                    RequestKeyframe();
                }
            }
        }
    }

    /// <summary>
    /// Processes an incoming remote client input datagram.
    /// </summary>
    public bool ProcessInputDatagram(ReadOnlySpan<byte> datagram)
    {
        if (_disposed || State != HostSessionState.Streaming) return false;
        bool result = _inputPipeline?.ProcessInputPacket(datagram) ?? false;
        if (result)
        {
            Interlocked.Increment(ref _totalInputPacketsProcessed);
        }
        return result;
    }

    public void SendAudioPacket(ReadOnlySpan<byte> audioDatagram) => OnAudioPacketEncoded(audioDatagram);

    private void OnAudioPacketEncoded(ReadOnlySpan<byte> audioDatagram)
    {
        if (_disposed || State != HostSessionState.Streaming || _audioSocket == null) return;

        Interlocked.Increment(ref _totalAudioFramesCaptured);

        try
        {
            int sent = _audioSocket.SendTo(audioDatagram, SocketFlags.None, _clientAudioEndpoint);
            if (sent > 0)
            {
                Interlocked.Increment(ref _totalAudioPacketsSent);
                Interlocked.Add(ref _totalBytesSent, (ulong)sent);
            }
        }
        // ALLOWED_EXCEPTION: Transient UDP send failure on network drop.
        catch (SocketException)
        {
        }
    }

    private async Task VideoFrameLoopAsync()
    {
        long nextFrameTimestamp = Stopwatch.GetTimestamp();
        byte[] bitstreamBuffer = new byte[2 * 1024 * 1024]; // 2 MB max frame buffer
        ulong frameIndex = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                uint targetFps = Volatile.Read(ref _currentFps);
                long targetFrameIntervalTicks = (long)(Stopwatch.Frequency / (double)(targetFps > 0 ? targetFps : 60));
                long startTimestamp = Stopwatch.GetTimestamp();
                if (_capturePipeline == null || _encoderEngine == null || _videoPacketiser == null)
                {
                    break;
                }

                // 1. Acquire Desktop Frame
                bool frameAcquired = _capturePipeline.TryAcquireNextFrame(timeoutMs: 16, out MoonshineCaptureFrameDesc frameDesc);
                if (!frameAcquired)
                {
                    bool isHeadless = _topologyWatcher?.CurrentTopology.IsHeadless ?? false;
                    if (!isHeadless && !_capturePipeline.IsAvailable)
                    {
                        if (!_capturePipeline.TryRecover())
                        {
                            SetFaultedState("Desktop capture pipeline unrecoverable device loss.");
                            break;
                        }
                    }

                    // Yield and align with next cadence
                    await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    Interlocked.Increment(ref _totalFramesCaptured);
                    bool forceIdr = Volatile.Read(ref _forceNextIdr);
                    if (forceIdr)
                    {
                        Volatile.Write(ref _forceNextIdr, false);
                    }

                    // 2. Encode Frame Surface
                    IntPtr texturePtr;
                    unsafe
                    {
                        texturePtr = (IntPtr)frameDesc.TextureHandle;
                    }

                    bool encoded = _encoderEngine.TryEncodeFrame(
                        texturePtr,
                        forceIdr: forceIdr || frameIndex == 0,
                        out MoonshineEncodedPacketDesc encodedDesc,
                        bitstreamBuffer,
                        out int encodedBytes);

                    if (encoded && encodedBytes > 0)
                    {
                        Interlocked.Increment(ref _totalFramesEncoded);
                        ulong timestampUs = (ulong)(frameDesc.TimestampQpc > 0 ? frameDesc.TimestampQpc * 1_000_000.0 / Stopwatch.Frequency : (startTimestamp * 1_000_000.0 / Stopwatch.Frequency));
                        bool isKeyframe = encodedDesc.IsKeyframe != 0;

                        // 3. Packetise and Transmit Slices
                        int packetsSent = _videoPacketiser.PacketiseFrame(
                            bitstreamBuffer.AsSpan(0, encodedBytes),
                            frameIndex: frameIndex++,
                            timestampUs: timestampUs,
                            isKeyframe: isKeyframe,
                            isHdr10: _config.EnableHdr10,
                            sink: SendVideoPacket);

                        Interlocked.Add(ref _totalPacketsSent, (ulong)packetsSent);

                        // 4. Measure End-to-End Latency
                        long dispatchTimestamp = Stopwatch.GetTimestamp();
                        double latencyUs = (dispatchTimestamp - startTimestamp) * 1_000_000.0 / Stopwatch.Frequency;
                        UpdateLatencyMeasurement(latencyUs);
                    }
                }
                finally
                {
                    _capturePipeline.ReleaseFrame();
                }

                // 5. Pace frame loop to target FPS cadence with adaptive network pacing
                uint pacingUs = Volatile.Read(ref _pacingAdjustmentUs);
                long effectiveIntervalTicks = targetFrameIntervalTicks + (long)(pacingUs * (Stopwatch.Frequency / 1_000_000.0));
                nextFrameTimestamp += effectiveIntervalTicks;
                long now = Stopwatch.GetTimestamp();
                long remainingTicks = nextFrameTimestamp - now;
                if (remainingTicks > 0)
                {
                    int delayMs = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, _cts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Frame took longer than interval, catch up
                    nextFrameTimestamp = now;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Propagates unhandled background streaming exceptions into session fault state.
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException or TimeoutException or System.Runtime.InteropServices.ExternalException)
            {
                SetFaultedState($"Video streaming loop faulted: {ex.Message}");
                break;
            }
        }
    }

    private void SendVideoPacket(ReadOnlySpan<byte> datagram)
    {
        if (_disposed || _videoSocket == null) return;

        try
        {
            int bytesSent = _videoSocket.SendTo(datagram, SocketFlags.None, _clientVideoEndpoint);
            if (bytesSent > 0)
            {
                Interlocked.Add(ref _totalBytesSent, (ulong)bytesSent);
            }
        }
        // ALLOWED_EXCEPTION: Transient UDP send failure on network drop.
        catch (SocketException)
        {
        }
    }

    private async Task ControlFeedbackLoopAsync()
    {
        byte[] buffer = new byte[2048];
        byte[] ackBuffer = new byte[MoonshineProtocolConstants.HeaderSize];
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

                ReadOnlySpan<byte> datagram = buffer.AsSpan(0, result.ReceivedBytes);

                // 1. Try parse Moonshine-native feedback messages
                if (datagram.Length >= MoonshineProtocolConstants.HeaderSize)
                {
                    MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(datagram, out var packetHeader);
                    if (err == MoonshineErrorCode.Success && packetHeader.Magic == MoonshineProtocolConstants.Magic)
                    {
                        ulong currentTimestampUs = (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency);
                        MoonshineErrorCode stateErr = _protocolStateMachine.IngestPacketHeader(in packetHeader, currentTimestampUs);
                        if (stateErr != MoonshineErrorCode.Success)
                        {
                            continue;
                        }

                        if (packetHeader.MessageType == MoonshineMessageType.Hello)
                        {
                            if (MoonshineProtocolCodec.TryReadHello(datagram[MoonshineProtocolConstants.HeaderSize..], out var hello))
                            {
                                byte[] respBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 48];
                                var respHeader = new MoonshinePacketHeader(
                                    Magic: MoonshineProtocolConstants.Magic,
                                    Version: MoonshineProtocolConstants.Version10,
                                    MessageType: MoonshineMessageType.HelloResponse,
                                    PayloadSize: 48,
                                    SequenceNumber: packetHeader.SequenceNumber,
                                    SessionId: _config.SessionId,
                                    TimestampUs: currentTimestampUs);

                                var respPayload = new MoonshineHelloResponsePayload
                                {
                                    ServerVersionMajor = 1,
                                    ServerVersionMinor = 0,
                                    NegotiatedCapabilities = MoonshineCapabilities.Hevc | MoonshineCapabilities.ReedSolomonFec | MoonshineCapabilities.HighPollRateInput,
                                    AssignedSessionId = _config.SessionId,
                                    ServerNonce = (ulong)Stopwatch.GetTimestamp(),
                                    ChallengeSalt = new MoonshineUuid128(Guid.NewGuid()),
                                    SessionLeaseSeconds = 3600,
                                    Reserved = 0
                                };

                                MoonshineProtocolCodec.TryWriteHeader(in respHeader, respBuffer);
                                MoonshineProtocolCodec.TryWriteHelloResponse(in respPayload, respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

                                try
                                {
                                    _controlSocket?.SendTo(respBuffer, result.RemoteEndPoint);
                                    _protocolStateMachine.RecordHelloResponseReceived(_config.SessionId);
                                }
                                // ALLOWED_EXCEPTION: Transient socket error on hello response send.
                                catch (SocketException) { }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.SessionSetup)
                        {
                            if (MoonshineProtocolCodec.TryReadSessionSetup(datagram[MoonshineProtocolConstants.HeaderSize..], out var setup) == MoonshineErrorCode.Success)
                            {
                                if (result.RemoteEndPoint is IPEndPoint clientEp)
                                {
                                    SetClientEndpoints(
                                        new IPEndPoint(clientEp.Address, setup.ClientUdpVideoPort > 0 ? setup.ClientUdpVideoPort : _config.ClientVideoPort),
                                        new IPEndPoint(clientEp.Address, setup.ClientUdpAudioPort > 0 ? setup.ClientUdpAudioPort : _config.ClientAudioPort),
                                        new IPEndPoint(clientEp.Address, setup.ClientUdpFeedbackPort > 0 ? setup.ClientUdpFeedbackPort : clientEp.Port));
                                }

                                if (setup.VideoBitrateKbps > 0)
                                {
                                    _encoderEngine?.ReconfigureBitrate(setup.VideoBitrateKbps);
                                }

                                uint negotiatedMtu = setup.MtuPayloadSize > 0 ? setup.MtuPayloadSize : (uint)_config.MtuPayloadSize;
                                byte[] respBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 32];
                                var respHeader = new MoonshinePacketHeader(
                                    Magic: MoonshineProtocolConstants.Magic,
                                    Version: MoonshineProtocolConstants.Version10,
                                    MessageType: MoonshineMessageType.SessionSetupResponse,
                                    PayloadSize: 32,
                                    SequenceNumber: packetHeader.SequenceNumber,
                                    SessionId: _config.SessionId,
                                    TimestampUs: currentTimestampUs);

                                var respPayload = new MoonshineSessionSetupResponsePayload
                                {
                                    StatusCode = MoonshineErrorCode.Success,
                                    VideoStreamId = 1,
                                    AudioStreamId = 2,
                                    FeedbackStreamId = 3,
                                    HostUdpVideoPort = (ushort)BoundLocalVideoPort,
                                    HostUdpAudioPort = (ushort)BoundLocalAudioPort,
                                    HostUdpFeedbackPort = (ushort)BoundLocalControlPort,
                                    HostUdpInputPort = (ushort)BoundLocalControlPort,
                                    NegotiatedMtu = negotiatedMtu,
                                    Reserved = 0
                                };

                                MoonshineProtocolCodec.TryWriteHeader(in respHeader, respBuffer);
                                MoonshineProtocolCodec.TryWriteSessionSetupResponse(in respPayload, respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

                                try
                                {
                                    _controlSocket?.SendTo(respBuffer, result.RemoteEndPoint);
                                    _protocolStateMachine.RecordSessionSetupResponseReceived(1, 2, 3, negotiatedMtu);
                                }
                                // ALLOWED_EXCEPTION: Transient socket error on session setup response send.
                                catch (SocketException) { }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.FeedbackLossStats)
                        {
                            if (MoonshineFeedbackCodec.TryReadLossStats(datagram, out _, out var lossStats) == MoonshineErrorCode.Success)
                            {
                                _protocolStateMachine.IngestFeedbackLossStats(in lossStats, currentTimestampUs);
                                _congestionController?.ProcessFeedback(in lossStats);
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.IdrRequest)
                        {
                            if (MoonshineFeedbackCodec.TryReadIdrRequest(datagram, out _, out var idrRequest) == MoonshineErrorCode.Success)
                            {
                                _congestionController?.ProcessIdrRequest(in idrRequest);
                                RequestKeyframe();
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType is MoonshineMessageType.InputKeyboard
                                                           or MoonshineMessageType.InputMouse
                                                           or MoonshineMessageType.InputGamepad)
                        {
                            if (_inputPipeline?.ProcessInputPacket(datagram) == true)
                            {
                                Interlocked.Increment(ref _totalInputPacketsProcessed);
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.KeepAlive)
                        {
                            var ackHeader = new MoonshinePacketHeader(
                                Magic: MoonshineProtocolConstants.Magic,
                                Version: MoonshineProtocolConstants.Version10,
                                MessageType: MoonshineMessageType.KeepAliveAck,
                                PayloadSize: 0,
                                SequenceNumber: packetHeader.SequenceNumber,
                                SessionId: _config.SessionId,
                                TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

                            if (MoonshineProtocolCodec.TryWriteHeader(in ackHeader, ackBuffer))
                            {
                                try
                                {
                                    _controlSocket?.SendTo(ackBuffer, result.RemoteEndPoint);
                                }
                                // ALLOWED_EXCEPTION: Transient socket exception on keep-alive ACK send.
                                catch (SocketException)
                                {
                                }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.GetHostCapabilities)
                        {
                            if (MoonshineProtocolCodec.TryReadGetHostCapabilities(datagram[MoonshineProtocolConstants.HeaderSize..], out uint queryMask) == MoonshineErrorCode.Success)
                            {
                                if (_authenticator != null)
                                {
                                    if (packetHeader.PayloadSize != 36 || datagram.Length < MoonshineProtocolConstants.HeaderSize + 36)
                                    {
                                        continue;
                                    }

                                    if (!_authenticator.ValidateIncomingSequence(packetHeader.SequenceNumber, packetHeader.TimestampUs, out _))
                                    {
                                        continue;
                                    }

                                    ReadOnlySpan<byte> signedContent = datagram[..(MoonshineProtocolConstants.HeaderSize + 4)];
                                    ReadOnlySpan<byte> tag = datagram.Slice(MoonshineProtocolConstants.HeaderSize + 4, 32);

                                    if (!_authenticator.VerifyMessageAuthTag(signedContent, tag))
                                    {
                                        continue;
                                    }
                                }

                                var caps = _configurationService.Capabilities;
                                byte[] respBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 32];
                                var respHeader = new MoonshinePacketHeader(
                                    Magic: MoonshineProtocolConstants.Magic,
                                    Version: MoonshineProtocolConstants.Version10,
                                    MessageType: MoonshineMessageType.HostCapabilitiesResponse,
                                    PayloadSize: 32,
                                    SequenceNumber: packetHeader.SequenceNumber,
                                    SessionId: _config.SessionId,
                                    TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

                                MoonshineProtocolCodec.TryWriteHeader(in respHeader, respBuffer);
                                MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(in caps, respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

                                try
                                {
                                    _controlSocket?.SendTo(respBuffer, result.RemoteEndPoint);
                                }
                                // ALLOWED_EXCEPTION: Transient socket error on host capabilities response send.
                                catch (SocketException) { }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.GetHostConfiguration)
                        {
                            if (MoonshineProtocolCodec.TryReadGetHostConfiguration(datagram[MoonshineProtocolConstants.HeaderSize..], out uint queryScope) == MoonshineErrorCode.Success)
                            {
                                if (_authenticator != null)
                                {
                                    if (packetHeader.PayloadSize != 36 || datagram.Length < MoonshineProtocolConstants.HeaderSize + 36)
                                    {
                                        continue;
                                    }

                                    if (!_authenticator.ValidateIncomingSequence(packetHeader.SequenceNumber, packetHeader.TimestampUs, out _))
                                    {
                                        continue;
                                    }

                                    ReadOnlySpan<byte> signedContent = datagram[..(MoonshineProtocolConstants.HeaderSize + 4)];
                                    ReadOnlySpan<byte> tag = datagram.Slice(MoonshineProtocolConstants.HeaderSize + 4, 32);

                                    if (!_authenticator.VerifyMessageAuthTag(signedContent, tag))
                                    {
                                        continue;
                                    }
                                }

                                var currentConfig = _configurationService.CurrentConfiguration;
                                byte[] respBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 48];
                                var respHeader = new MoonshinePacketHeader(
                                    Magic: MoonshineProtocolConstants.Magic,
                                    Version: MoonshineProtocolConstants.Version10,
                                    MessageType: MoonshineMessageType.HostConfigurationResponse,
                                    PayloadSize: 48,
                                    SequenceNumber: packetHeader.SequenceNumber,
                                    SessionId: _config.SessionId,
                                    TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

                                MoonshineProtocolCodec.TryWriteHeader(in respHeader, respBuffer);
                                MoonshineProtocolCodec.TryWriteHostConfiguration(in currentConfig, respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

                                try
                                {
                                    _controlSocket?.SendTo(respBuffer, result.RemoteEndPoint);
                                }
                                // ALLOWED_EXCEPTION: Transient socket error on host configuration response send.
                                catch (SocketException) { }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.SetHostConfiguration)
                        {
                            if (_authenticator != null)
                            {
                                if (packetHeader.PayloadSize != 80 || datagram.Length < MoonshineProtocolConstants.HeaderSize + 80)
                                {
                                    SendSetConfigurationResponse(result.RemoteEndPoint, packetHeader.SequenceNumber, MoonshineErrorCode.AuthenticationFailed, _configurationService.ConfigVersion);
                                    continue;
                                }

                                if (!_authenticator.ValidateIncomingSequence(packetHeader.SequenceNumber, packetHeader.TimestampUs, out var status))
                                {
                                    MoonshineErrorCode seqErr = status switch
                                    {
                                        SessionValidationStatus.DuplicateSequence => MoonshineErrorCode.DuplicateSequence,
                                        SessionValidationStatus.StaleTimestamp => MoonshineErrorCode.StaleTimestamp,
                                        _ => MoonshineErrorCode.AuthenticationFailed
                                    };

                                    SendSetConfigurationResponse(result.RemoteEndPoint, packetHeader.SequenceNumber, seqErr, _configurationService.ConfigVersion);
                                    continue;
                                }

                                ReadOnlySpan<byte> signedContent = datagram[..(MoonshineProtocolConstants.HeaderSize + 48)];
                                ReadOnlySpan<byte> tag = datagram.Slice(MoonshineProtocolConstants.HeaderSize + 48, 32);

                                if (!_authenticator.VerifyMessageAuthTag(signedContent, tag))
                                {
                                    SendSetConfigurationResponse(result.RemoteEndPoint, packetHeader.SequenceNumber, MoonshineErrorCode.AuthenticationFailed, _configurationService.ConfigVersion);
                                    continue;
                                }
                            }

                            if (MoonshineProtocolCodec.TryReadHostConfiguration(datagram[MoonshineProtocolConstants.HeaderSize..], out var proposed) == MoonshineErrorCode.Success)
                            {
                                bool success = _configurationService.TryApplyConfiguration(
                                    proposed,
                                    _config.AuthorisationLevel,
                                    out var effective,
                                    out var errorCode,
                                    out _);

                                if (success)
                                {
                                    if (effective.TargetBitrateKbps > 0)
                                    {
                                        _encoderEngine?.ReconfigureBitrate(effective.TargetBitrateKbps, effective.RefreshRateHz);
                                        _congestionController?.ReconfigureBitrate(effective.TargetBitrateKbps, effective.MaxBitrateKbps);
                                    }
                                    if (effective.AudioBitrateKbps > 0)
                                    {
                                        _audioPipeline?.ReconfigureBitrate(effective.AudioBitrateKbps * 1000);
                                    }

                                    if (effective.DisplayWidth != _config.Width ||
                                        effective.DisplayHeight != _config.Height ||
                                        effective.RefreshRateHz != _config.Fps)
                                    {
                                        RequestKeyframe();
                                        if (effective.RefreshRateHz > 0)
                                        {
                                            Volatile.Write(ref _currentFps, effective.RefreshRateHz);
                                            _encoderEngine?.ReconfigureBitrate(effective.TargetBitrateKbps, effective.RefreshRateHz);
                                        }
                                    }
                                }

                                SendSetConfigurationResponse(result.RemoteEndPoint, packetHeader.SequenceNumber, errorCode, effective.ConfigVersion);

                                if (success)
                                {
                                    var changedPayload = new MoonshineConfigurationChangedPayload
                                    {
                                        NewConfigVersion = effective.ConfigVersion,
                                        ChangeReasonFlags = 0
                                    };

                                    byte[] changedBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 8];
                                    var changedHeader = new MoonshinePacketHeader(
                                        Magic: MoonshineProtocolConstants.Magic,
                                        Version: MoonshineProtocolConstants.Version10,
                                        MessageType: MoonshineMessageType.ConfigurationChanged,
                                        PayloadSize: 8,
                                        SequenceNumber: packetHeader.SequenceNumber,
                                        SessionId: _config.SessionId,
                                        TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

                                    MoonshineProtocolCodec.TryWriteHeader(in changedHeader, changedBuffer);
                                    MoonshineProtocolCodec.TryWriteConfigurationChanged(in changedPayload, changedBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

                                    try
                                    {
                                        _controlSocket?.SendTo(changedBuffer, result.RemoteEndPoint);
                                    }
                                    // ALLOWED_EXCEPTION: Transient socket error on configuration changed notification send.
                                    catch (SocketException) { }
                                }
                            }
                            continue;
                        }
                        else if (packetHeader.MessageType == MoonshineMessageType.Teardown)
                        {
                            _ = Task.Run(() => StopAsync(CancellationToken.None));
                            break;
                        }
                    }
                }

                // 2. Fall back to legacy RTCP / GameStream control header
                if (datagram.Length >= ControlHeader.Size)
                {
                    if (ControlHeader.TryParse(datagram, out ControlHeader header, out ReadOnlySpan<byte> payload))
                    {
                        if (header.PacketType == ControlPacketType.IdrRequest)
                        {
                            RequestKeyframe();
                        }
                        else if (header.PacketType == ControlPacketType.LossStats && payload.Length >= 16)
                        {
                            uint lastGood = BinaryPrimitives.ReadUInt32BigEndian(payload[0..4]);
                            uint lostPackets = BinaryPrimitives.ReadUInt32BigEndian(payload[4..8]);
                            uint recoveredFec = BinaryPrimitives.ReadUInt32BigEndian(payload[8..12]);
                            uint rttUs = BinaryPrimitives.ReadUInt32BigEndian(payload[12..16]);

                            var nativeFeedback = new MoonshineFeedbackLossStatsPayload
                            {
                                StreamId = _config.StreamId,
                                LastReceivedFrameIndex = lastGood,
                                PacketsLost = lostPackets,
                                PacketsRecoveredFec = recoveredFec,
                                RoundTripTimeUs = rttUs,
                                PacketsReceived = lastGood,
                                JitterUs = 0,
                                EstimatedBandwidthKbps = 0,
                                ReceiveQueueDepth = 0
                            };
                            _congestionController?.ProcessFeedback(in nativeFeedback);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Continue receiving control feedback despite transient errors.
            catch (SocketException)
            {
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private void SendSetConfigurationResponse(
        EndPoint? remoteEp,
        uint sequenceNumber,
        MoonshineErrorCode statusCode,
        uint appliedConfigVersion)
    {
        if (remoteEp == null || _controlSocket == null) return;

        var respPayload = new MoonshineSetHostConfigurationResponsePayload
        {
            StatusCode = statusCode,
            AppliedConfigVersion = appliedConfigVersion
        };

        byte[] respBuffer = new byte[MoonshineProtocolConstants.HeaderSize + 8];
        var respHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfigurationResponse,
            PayloadSize: 8,
            SequenceNumber: sequenceNumber,
            SessionId: _config.SessionId,
            TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

        MoonshineProtocolCodec.TryWriteHeader(in respHeader, respBuffer);
        MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(in respPayload, respBuffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

        try
        {
            _controlSocket.SendTo(respBuffer, remoteEp);
        }
        // ALLOWED_EXCEPTION: Transient socket error on set host configuration response send.
        catch (SocketException) { }
    }

    private async Task MicrophoneUplinkLoopAsync()
    {
        byte[] buffer = new byte[2048];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket = _micSocket;
            if (socket == null) break;

            try
            {
                SocketReceiveFromResult received = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    remoteEp,
                    _cts.Token).ConfigureAwait(false);

                if (received.ReceivedBytes > 0)
                {
                    _micUplinkService?.IngestDatagram(buffer.AsSpan(0, received.ReceivedBytes));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Continue receiving microphone uplink packets despite transient socket errors.
            catch (SocketException)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await Task.Delay(10, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adjusts host microphone backchannel input gain multiplier.
    /// </summary>
    public void SetMicrophoneGain(float gain) => _micUplinkService?.SetGain(gain);

    /// <summary>
    /// Toggles host microphone backchannel mute state.
    /// </summary>
    public void SetMicrophoneMute(bool isMuted) => _micUplinkService?.SetMute(isMuted);

    /// <summary>
    /// Retrieves active host microphone sink telemetry metrics.
    /// </summary>
    public HostMicSinkMetrics? GetMicrophoneMetrics() => _micUplinkService?.GetMetrics();

    private void UpdateLatencyMeasurement(double latencyUs)
    {
        double current = Volatile.Read(ref _averageCaptureToNetworkLatencyUs);
        double updated = current <= 0.0 ? latencyUs : (current * 0.95) + (latencyUs * 0.05);
        Volatile.Write(ref _averageCaptureToNetworkLatencyUs, updated);
    }

    private void SetFaultedState(string reason)
    {
        lock (_stateLock)
        {
            if (_state is HostSessionState.Terminated or HostSessionState.Draining) return;
            _state = HostSessionState.Faulted;
            _lastError = reason;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopTask;
        lock (_stateLock)
        {
            if (_state == HostSessionState.Terminated) return;
            if (_stopTask != null)
            {
                stopTask = _stopTask;
            }
            else
            {
                _state = HostSessionState.Draining;
                _stopTask = Task.Run(async () =>
                {
                    await CleanupResourcesAsync().ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        _state = HostSessionState.Terminated;
                    }
                }, CancellationToken.None);
                stopTask = _stopTask;
            }
        }

        await stopTask.ConfigureAwait(false);
    }

    private async ValueTask CleanupResourcesAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_videoLoopTask != null)
        {
            try
            {
                await _videoLoopTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Ignore task cancellation during cleanup.
            catch (OperationCanceledException)
            {
            }
            _videoLoopTask = null;
        }

        if (_controlFeedbackLoopTask != null)
        {
            try
            {
                await _controlFeedbackLoopTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Ignore task cancellation during cleanup.
            catch (OperationCanceledException)
            {
            }
            _controlFeedbackLoopTask = null;
        }

        if (_micUplinkLoopTask != null)
        {
            try
            {
                await _micUplinkLoopTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Ignore task cancellation during cleanup.
            catch (OperationCanceledException)
            {
            }
            _micUplinkLoopTask = null;
        }

        if (_topologyWatcher != null)
        {
            _topologyWatcher.TopologyChanged -= OnDisplayTopologyChanged;
        }

        if (_micUplinkService != null)
        {
            if (_ownsMicUplink)
            {
                _micUplinkService.Dispose();
            }
            _micUplinkService = null;
        }

        if (_audioPipeline != null)
        {
            _audioPipeline.Stop();
            if (_ownsAudio)
            {
                _audioPipeline.Dispose();
                _audioPipeline = null;
            }
        }

        if (_ownsCapture && _capturePipeline != null)
        {
            _capturePipeline.Dispose();
            _capturePipeline = null;
        }

        if (_ownsEncoder && _encoderEngine != null)
        {
            _encoderEngine.Dispose();
            _encoderEngine = null;
        }

        if (_ownsInput && _inputPipeline != null)
        {
            _inputPipeline.Dispose();
            _inputPipeline = null;
        }

        _videoSocket?.Dispose();
        _videoSocket = null;

        _audioSocket?.Dispose();
        _audioSocket = null;

        _controlSocket?.Dispose();
        _controlSocket = null;

        _micSocket?.Dispose();
        _micSocket = null;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        await CleanupResourcesAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
