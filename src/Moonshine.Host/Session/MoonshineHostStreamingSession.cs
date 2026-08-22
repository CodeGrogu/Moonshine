using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Congestion;
using Moonshine.Core.Media;
using Moonshine.Core.Transport;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
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
    private MoonshineVideoPacketiser? _videoPacketiser;
    private CongestionController? _congestionController;

    private Socket? _videoSocket;
    private Socket? _audioSocket;
    private Socket? _controlSocket;

    private IPEndPoint _clientVideoEndpoint;
    private IPEndPoint _clientAudioEndpoint;
    private IPEndPoint _clientControlEndpoint;

    private Task? _videoLoopTask;
    private Task? _controlFeedbackLoopTask;

    private HostSessionState _state = HostSessionState.Created;
    private string? _lastError;
    private bool _disposed;
    private bool _forceNextIdr;

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

    public HostSessionConfig Config => _config;
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
        LastError);

    public MoonshineHostStreamingSession(
        HostSessionConfig? config = null,
        IDesktopCapturePipeline? capturePipeline = null,
        UnifiedHardwareEncoderEngine? encoderEngine = null,
        MoonshineHostAudioPipeline? audioPipeline = null,
        MoonshineHostInputPipeline? inputPipeline = null)
    {
        _config = config ?? HostSessionConfig.Default;
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

            // 9. Start Video loop and Control Feedback loop
            _videoLoopTask = Task.Run(VideoFrameLoopAsync, CancellationToken.None);
            _controlFeedbackLoopTask = Task.Run(ControlFeedbackLoopAsync, CancellationToken.None);

            // 10. Transition to Streaming state only after all backends are verified
            lock (_stateLock)
            {
                _state = HostSessionState.Streaming;
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
    }

    /// <summary>
    /// Requests an immediate IDR keyframe from the hardware video encoder on the next captured frame.
    /// </summary>
    public void RequestKeyframe()
    {
        Interlocked.Increment(ref _keyframesRequested);
        Volatile.Write(ref _forceNextIdr, true);
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
        long targetFrameIntervalTicks = (long)(Stopwatch.Frequency / (double)_config.Fps);
        long nextFrameTimestamp = Stopwatch.GetTimestamp();
        byte[] bitstreamBuffer = new byte[2 * 1024 * 1024]; // 2 MB max frame buffer
        ulong frameIndex = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                if (_capturePipeline == null || _encoderEngine == null || _videoPacketiser == null)
                {
                    break;
                }

                // 1. Acquire Desktop Frame
                bool frameAcquired = _capturePipeline.TryAcquireNextFrame(timeoutMs: 16, out MoonshineCaptureFrameDesc frameDesc);
                if (!frameAcquired)
                {
                    // Check if capture pipeline is still healthy or needs recovery
                    if (!_capturePipeline.IsAvailable)
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
                        if (packetHeader.MessageType == MoonshineMessageType.FeedbackLossStats)
                        {
                            if (MoonshineFeedbackCodec.TryReadLossStats(datagram, out _, out var lossStats) == MoonshineErrorCode.Success)
                            {
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

                            var rtcpLoss = new Moonshine.Protocol.RTP.RtcpLossStatsPacket(
                                0, 0, (ushort)lostPackets, (ushort)recoveredFec, 0, 0);
                            _congestionController?.ProcessFeedback(in rtcpLoss, rttUs / 1000.0);
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
        lock (_stateLock)
        {
            if (_state is HostSessionState.Terminated or HostSessionState.Draining) return;
            _state = HostSessionState.Draining;
        }

        await CleanupResourcesAsync().ConfigureAwait(false);

        lock (_stateLock)
        {
            _state = HostSessionState.Terminated;
        }
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
