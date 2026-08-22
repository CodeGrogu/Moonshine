using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonshine.Core.Media;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Audio;

public enum HostAudioBackend
{
    Disabled = 0,
    VirtualDriverIpc = 1,
    WasapiLoopbackFallback = 2
}

public readonly record struct HostAudioMetrics(
    ulong TotalFramesCaptured,
    ulong TotalFramesEncoded,
    ulong TotalPacketsEmitted,
    uint Underruns,
    uint Overruns,
    double AverageCaptureLatencyUs,
    double AverageEncodeLatencyUs,
    HostAudioBackend ActiveBackend
);

/// <summary>
/// Production host-side audio capture, low-latency Opus compression, and dual packetisation pipeline.
/// Automatically detects and uses the PortCls WaveRT virtual audio driver IPC when available,
/// or seamlessly falls back to real Windows Core Audio WASAPI Loopback capture with zero GC allocations.
/// </summary>
public sealed class MoonshineHostAudioPipeline : IDisposable
{
    private readonly uint _sampleRate;
    private readonly AudioChannelTopology _topology;
    private readonly uint _channels;
    private readonly uint _frameDurationMs;
    private readonly uint _samplesPerFrame; // per channel
    private uint _bitrate;

    private readonly VirtualAudioDriverService? _driverService;
    private VirtualAudioIpcBridgePipeline? _ipcBridge;
    private WasapiLoopbackAudioPipeline? _wasapiLoopback;
    private OpusAudioEncoderPipeline? _encoder;
    private MoonshineAudioPacketiser? _moonshinePacketiser;
    private RtpAudioPacketiser? _rtpPacketiser;

    private HostAudioBackend _activeBackend = HostAudioBackend.Disabled;
    private readonly bool _isDriverInstalled;

    private Thread? _audioWorkerThread;
    private CancellationTokenSource? _workerCts;
    private AudioPacketSink? _activeSink;
    private bool _preferMoonshineFraming = true;
    private bool _isRunning;
    private bool _disposed;
    private int _disposeInitiated;
    private int _inFlightOperations;
    [ThreadStatic] private static int t_inFlightDepth;
    private readonly ManualResetEventSlim _drainCompletedEvent = new(initialState: true);

    private readonly Lock _stateLock = new();

    // Metrics tracking
    private ulong _totalFramesCaptured;
    private ulong _totalFramesEncoded;
    private ulong _totalPacketsEmitted;
    private ulong _sampleIndex;
    private uint _rtpTimestamp;
    private double _totalCaptureLatencyUs;
    private double _totalEncodeLatencyUs;

    // Preallocated buffers for zero-allocation streaming hot path
    private readonly float[] _pcmStagingBuffer;
    private readonly byte[] _encodedPayloadBuffer;
    private readonly byte[] _rtpPacketBuffer;

    public HostAudioBackend ActiveBackend => _activeBackend;
    public bool IsDriverInstalled => _isDriverInstalled;
    public uint SampleRate => _sampleRate;
    public AudioChannelTopology Topology => _topology;
    public uint Channels => _channels;
    public uint Bitrate => _bitrate;
    public uint FrameDurationMs => _frameDurationMs;
    public bool IsRunning => Volatile.Read(ref _isRunning);

    public HostAudioMetrics Metrics
    {
        get
        {
            lock (_stateLock)
            {
                uint underruns = 0;
                uint overruns = 0;

                if (_activeBackend == HostAudioBackend.VirtualDriverIpc && _ipcBridge is not null)
                {
                    if (_ipcBridge.TryGetMetrics(out var ipcMetrics))
                    {
                        underruns = ipcMetrics.RenderUnderruns;
                        overruns = ipcMetrics.RenderOverruns;
                    }
                }
                else if (_activeBackend == HostAudioBackend.WasapiLoopbackFallback && _wasapiLoopback is not null)
                {
                    _wasapiLoopback.GetMetrics(out _, out _, out underruns, out overruns);
                }

                double avgCapture = _totalFramesCaptured > 0 ? _totalCaptureLatencyUs / _totalFramesCaptured : 0.0;
                double avgEncode = _totalFramesEncoded > 0 ? _totalEncodeLatencyUs / _totalFramesEncoded : 0.0;

                return new HostAudioMetrics(
                    TotalFramesCaptured: _totalFramesCaptured,
                    TotalFramesEncoded: _totalFramesEncoded,
                    TotalPacketsEmitted: _totalPacketsEmitted,
                    Underruns: underruns,
                    Overruns: overruns,
                    AverageCaptureLatencyUs: avgCapture,
                    AverageEncodeLatencyUs: avgEncode,
                    ActiveBackend: _activeBackend
                );
            }
        }
    }

    public MoonshineHostAudioPipeline(
        uint sampleRate = 48000,
        AudioChannelTopology topology = AudioChannelTopology.Stereo,
        uint bitrate = 160000,
        uint frameDurationMs = 5,
        uint streamId = 1,
        ulong sessionId = 0x10001000,
        bool forceWasapiFallback = false
    )
    {
        if (sampleRate == 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be greater than zero.");
        if (frameDurationMs == 0) throw new ArgumentOutOfRangeException(nameof(frameDurationMs), "Frame duration must be greater than zero.");

        _sampleRate = sampleRate;
        _topology = topology;
        _channels = (uint)topology;
        _bitrate = bitrate;
        _frameDurationMs = frameDurationMs;
        _samplesPerFrame = (sampleRate * frameDurationMs) / 1000;
        if (_samplesPerFrame == 0) _samplesPerFrame = 240;

        _pcmStagingBuffer = new float[_samplesPerFrame * _channels];
        _encodedPayloadBuffer = new byte[2048];
        _rtpPacketBuffer = new byte[2048];

        // Query driver presence
        bool driverAvailable = false;
        try
        {
            _driverService = new VirtualAudioDriverService();
            driverAvailable = _driverService.IsDriverInstalled();
        }
        // ALLOWED_EXCEPTION: Native driver probing may fail if service or driver is not installed.
        catch (ExternalException)
        {
            _driverService = null;
            driverAvailable = false;
        }
        // ALLOWED_EXCEPTION: Dll/entrypoint lookup failures in testing environments.
        catch (DllNotFoundException)
        {
            _driverService = null;
            driverAvailable = false;
        }

        _isDriverInstalled = driverAvailable;

        InitialiseAudioBackend(forceWasapiFallback);
        InitialiseEncoderAndPacketisers(streamId, sessionId);
    }

    private void InitialiseAudioBackend(bool forceWasapiFallback)
    {
        if (_isDriverInstalled && !forceWasapiFallback)
        {
            try
            {
                _ipcBridge = new VirtualAudioIpcBridgePipeline(isHostServer: true, _sampleRate, _channels);
                if (_ipcBridge.IsConnected)
                {
                    _ipcBridge.TryEnableMmcss();
                    _activeBackend = HostAudioBackend.VirtualDriverIpc;
                    return;
                }
            }
            // ALLOWED_EXCEPTION: Native IPC initialisation fallback to WASAPI.
            catch (InvalidOperationException)
            {
                _ipcBridge?.Dispose();
                _ipcBridge = null;
            }
            // ALLOWED_EXCEPTION: Native IPC external exception fallback to WASAPI.
            catch (ExternalException)
            {
                _ipcBridge?.Dispose();
                _ipcBridge = null;
            }
        }

        // Fallback to real Windows Core Audio WASAPI Loopback Capture
        _wasapiLoopback = new WasapiLoopbackAudioPipeline(_sampleRate, _topology, _frameDurationMs);
        _activeBackend = HostAudioBackend.WasapiLoopbackFallback;
    }

    private void InitialiseEncoderAndPacketisers(uint streamId, ulong sessionId)
    {
        _encoder = new OpusAudioEncoderPipeline(
            sampleRate: _sampleRate,
            topology: _topology,
            bitrate: _bitrate,
            frameDurationMs: _frameDurationMs,
            complexity: 8,
            useVbr: true
        );

        _moonshinePacketiser = new MoonshineAudioPacketiser(
            streamId: streamId,
            sessionId: sessionId,
            sampleRate: _sampleRate,
            channels: (byte)_channels,
            codec: MoonshineAudioCodec.Opus
        );

        _rtpPacketiser = new RtpAudioPacketiser(
            payloadType: 97,
            ssrc: 0x12345678,
            initialSeq: 0
        );
    }

    /// <summary>
    /// Starts the asynchronous high-priority audio capture and transmission worker thread.
    /// </summary>
    /// <param name="packetSink">The audio packet delivery sink. The sink delegate and its captured state must remain valid for the duration of the streaming session or until Stop() / Dispose() completes.</param>
    /// <param name="preferMoonshineFraming">True to packetise in native Moonshine format; false for standard RFC 3550 RTP.</param>
    public bool Start(AudioPacketSink packetSink, bool preferMoonshineFraming = true)
    {
        ArgumentNullException.ThrowIfNull(packetSink);

        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_isRunning) return true;

            _activeSink = packetSink;
            _preferMoonshineFraming = preferMoonshineFraming;
            _workerCts = new CancellationTokenSource();
            _isRunning = true;

            _audioWorkerThread = new Thread(AudioProcessingLoop)
            {
                Name = "Moonshine-HostAudioThread",
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };

            _audioWorkerThread.Start();
            return true;
        }
    }

    /// <summary>
    /// Stops the audio worker thread and halts audio packet transmission.
    /// </summary>
    public void Stop()
    {
        Thread? worker = null;
        lock (_stateLock)
        {
            if (!_isRunning) return;

            _isRunning = false;
            _workerCts?.Cancel();
            worker = _audioWorkerThread;
        }

        if (worker is not null && worker.IsAlive && Thread.CurrentThread != worker)
        {
            worker.Join();
        }

        lock (_stateLock)
        {
            _workerCts?.Dispose();
            _workerCts = null;
            _audioWorkerThread = null;
            _activeSink = null;
        }
    }

    /// <summary>
    /// Dynamically reconfigures the active Opus compression bitrate.
    /// </summary>
    public void ReconfigureBitrate(uint newBitrate)
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (newBitrate == 0) return;
            _bitrate = newBitrate;
            _encoder?.SetBitrate(newBitrate);
        }
    }

    /// <summary>
    /// Processes a single audio frame synchronously with zero GC allocations without holding the state lock across audio execution.
    /// Used for precise stepped streaming iterations and microbenchmarks.
    /// </summary>
    public bool ProcessNextAudioFrame(AudioPacketSink packetSink, bool preferMoonshineFraming = true)
    {
        ArgumentNullException.ThrowIfNull(packetSink);

        if (!TryEnterOperation())
        {
            ThrowIfDisposed();
            return false;
        }

        try
        {
            return ExecuteAudioFrameStep(packetSink, preferMoonshineFraming);
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Synchronously processes a caller-provided PCM buffer through the full encode, packetise, and dispatch pipeline
    /// without holding the internal state lock during audio execution.
    /// Intended for synchronous testing, microbenchmarks, and virtual PCM feed.
    /// </summary>
    public bool ProcessPcmFrame(ReadOnlySpan<float> pcmSamples, AudioPacketSink packetSink, bool preferMoonshineFraming = true)
    {
        ArgumentNullException.ThrowIfNull(packetSink);

        uint requiredSamples = _samplesPerFrame * _channels;
        if ((uint)pcmSamples.Length < requiredSamples)
        {
            return false;
        }

        if (!TryEnterOperation())
        {
            ThrowIfDisposed();
            return false;
        }

        try
        {
            return ExecutePcmFrameStep(pcmSamples[..(int)requiredSamples], packetSink, preferMoonshineFraming);
        }
        finally
        {
            ExitOperation();
        }
    }

    private bool ExecuteAudioFrameStep(AudioPacketSink packetSink, bool preferMoonshineFraming)
    {
        long captureStart = Stopwatch.GetTimestamp();
        Span<float> pcmSpan = _pcmStagingBuffer.AsSpan();
        int samplesRead = 0;

        if (_activeBackend == HostAudioBackend.VirtualDriverIpc && _ipcBridge is not null)
        {
            samplesRead = _ipcBridge.ReadRenderPcm(pcmSpan, waitEvent: false, timeoutMs: 0);
            if (samplesRead < pcmSpan.Length)
            {
                if (samplesRead > 0)
                {
                    pcmSpan[samplesRead..].Clear();
                }
                else
                {
                    pcmSpan.Clear();
                }
                samplesRead = pcmSpan.Length;
            }
        }
        else if (_activeBackend == HostAudioBackend.WasapiLoopbackFallback && _wasapiLoopback is not null)
        {
            bool ok = _wasapiLoopback.TryReadSamples(pcmSpan, out samplesRead, out _);
            if (!ok || samplesRead < pcmSpan.Length)
            {
                if (samplesRead > 0 && samplesRead < pcmSpan.Length)
                {
                    pcmSpan[samplesRead..].Clear();
                }
                else
                {
                    pcmSpan.Clear();
                }
                samplesRead = pcmSpan.Length;
            }
        }
        else
        {
            return false;
        }

        long captureEnd = Stopwatch.GetTimestamp();
        double captureUs = (double)(captureEnd - captureStart) * 1_000_000.0 / Stopwatch.Frequency;
        _totalCaptureLatencyUs += captureUs;
        _totalFramesCaptured++;

        return ExecutePcmFrameStep(pcmSpan[..samplesRead], packetSink, preferMoonshineFraming);
    }

    private bool ExecutePcmFrameStep(ReadOnlySpan<float> pcmSpan, AudioPacketSink packetSink, bool preferMoonshineFraming)
    {
        uint requiredSamples = _samplesPerFrame * _channels;
        if ((uint)pcmSpan.Length < requiredSamples)
        {
            return false;
        }

        ReadOnlySpan<float> validPcm = pcmSpan[..(int)requiredSamples];

        // Encode via low-latency Opus
        long encodeStart = Stopwatch.GetTimestamp();
        Span<byte> encodedPayload = _encodedPayloadBuffer.AsSpan();
        int bytesEncoded = 0;

        if (_encoder is not null)
        {
            _encoder.TryEncode(
                validPcm,
                _samplesPerFrame,
                encodedPayload,
                out bytesEncoded
            );
        }

        long encodeEnd = Stopwatch.GetTimestamp();
        double encodeUs = (double)(encodeEnd - encodeStart) * 1_000_000.0 / Stopwatch.Frequency;
        _totalEncodeLatencyUs += encodeUs;
        _totalFramesEncoded++;

        if (bytesEncoded <= 0) return false;

        // Packetise and emit
        ulong currentSampleIndex = _sampleIndex;
        _sampleIndex += _samplesPerFrame;
        ushort frameDurationUs = (ushort)(_frameDurationMs * 1000);
        ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);

        if (preferMoonshineFraming && _moonshinePacketiser is not null)
        {
            int emitted = _moonshinePacketiser.PacketiseAudioFrame(
                encodedPayload[..bytesEncoded],
                currentSampleIndex,
                frameDurationUs,
                timestampUs,
                packetSink
            );

            if (emitted > 0)
            {
                _totalPacketsEmitted += (ulong)emitted;
                return true;
            }
        }
        else if (_rtpPacketiser is not null)
        {
            Span<byte> rtpOut = _rtpPacketBuffer.AsSpan();
            uint rtpTs = _rtpTimestamp;
            _rtpTimestamp += _samplesPerFrame;

            if (_rtpPacketiser.TryPacketise(encodedPayload[..bytesEncoded], rtpTs, marker: true, rtpOut, out int bytesWritten))
            {
                packetSink(rtpOut[..bytesWritten]);
                _totalPacketsEmitted++;
                return true;
            }
        }

        return false;
    }

    private void AudioProcessingLoop()
    {
        var ct = _workerCts?.Token ?? CancellationToken.None;
        var framePeriod = TimeSpan.FromMilliseconds(_frameDurationMs);
        var nextTick = Stopwatch.GetTimestamp();
        long ticksPerFrame = (long)(framePeriod.TotalSeconds * Stopwatch.Frequency);

        while (!ct.IsCancellationRequested && Volatile.Read(ref _isRunning))
        {
            AudioPacketSink? sink;
            bool preferMoonshine;
            lock (_stateLock)
            {
                if (_disposed || !_isRunning || ct.IsCancellationRequested) break;
                sink = _activeSink;
                preferMoonshine = _preferMoonshineFraming;
            }

            if (sink is not null)
            {
                if (TryEnterOperation())
                {
                    try
                    {
                        ExecuteAudioFrameStep(sink, preferMoonshine);
                    }
                    finally
                    {
                        ExitOperation();
                    }
                }
            }

            nextTick += ticksPerFrame;
            long now = Stopwatch.GetTimestamp();
            long waitTicks = nextTick - now;

            if (waitTicks > 0)
            {
                int waitMs = (int)((waitTicks * 1000) / Stopwatch.Frequency);
                if (waitMs > 1)
                {
                    Thread.Sleep(waitMs - 1);
                }
                while (Stopwatch.GetTimestamp() < nextTick)
                {
                    Thread.SpinWait(10);
                }
            }
            else
            {
                // Align to current time if behind
                nextTick = now;
            }
        }
    }

    private bool TryEnterOperation()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return false;
            }
            if (Interlocked.Increment(ref _inFlightOperations) == 1)
            {
                _drainCompletedEvent.Reset();
            }
            t_inFlightDepth++;
            return true;
        }
    }

    private void ExitOperation()
    {
        t_inFlightDepth--;
        if (Interlocked.Decrement(ref _inFlightOperations) == 0)
        {
            _drainCompletedEvent.Set();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeInitiated, 1) != 0)
        {
            // Disposal already initiated by another thread; wait for state teardown to complete
            lock (_stateLock)
            {
                // Unblocks once the disposing thread finishes state teardown
            }
            return;
        }

        Stop();

        lock (_stateLock)
        {
            _disposed = true;
        }

        int localDepth = t_inFlightDepth;
        if (localDepth > 0)
        {
            // Re-entrant disposal on an active audio processing thread (e.g. from within a packet sink callback):
            // Wait until all OTHER in-flight threads have completely drained to prevent self-deadlock.
            while (Volatile.Read(ref _inFlightOperations) > localDepth)
            {
                Thread.Yield();
            }
        }
        else
        {
            // Unconditionally wait until all in-flight operations have completely exited.
            // Guarantees zero unmanaged resource destruction while an audio frame is being processed.
            _drainCompletedEvent.Wait();
        }

        lock (_stateLock)
        {
            _ipcBridge?.Dispose();
            _ipcBridge = null;

            _wasapiLoopback?.Dispose();
            _wasapiLoopback = null;

            _encoder?.Dispose();
            _encoder = null;

            _driverService?.Dispose();

            _activeBackend = HostAudioBackend.Disabled;
        }

        _drainCompletedEvent.Dispose();
    }
}
