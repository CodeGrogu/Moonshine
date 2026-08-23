using System.Buffers.Binary;
using System.Diagnostics;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Audio;

/// <summary>
/// High-performance managed uplink coordinator for client microphone audio backchannel stream ingestion.
/// Ingests RFC 3550 RTP or Moonshine Native Binary Protocol (MNBP) datagrams from connected clients,
/// feeds the low-latency native jitter buffer and Opus decoder sink, and pumps decoded Float32 PCM samples
/// directly into the Windows virtual audio driver shared-memory IPC bridge.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged microphone sink resources.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:DisposableFieldsShouldBeDisposed", Justification = "Synchronisation events remain alive to prevent ObjectDisposedException during concurrent worker loop unwinding.")]
public sealed class HostMicrophoneUplinkService : IDisposable
{
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly uint _frameDurationMs;
    private readonly uint _samplesPerFrame;

    private readonly HostVirtualMicSinkPipeline _sink;
    private readonly VirtualAudioIpcBridgePipeline? _ipcBridge;
    private readonly float[] _stagingPcmBuffer;

    private Thread? _pumpThread;
    private CancellationTokenSource? _workerCts;
    private readonly ManualResetEventSlim _workerExitedEvent = new(initialState: true);

    private readonly object _stateLock = new();
    private readonly object _pcmProcessingLock = new();

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Gets the negotiated sample rate in Hz.
    /// </summary>
    public uint SampleRate => _sampleRate;

    /// <summary>
    /// Gets the audio channel count.
    /// </summary>
    public uint Channels => _channels;

    /// <summary>
    /// Gets the audio frame duration in milliseconds.
    /// </summary>
    public uint FrameDurationMs => _frameDurationMs;

    /// <summary>
    /// Gets the number of audio samples per frame per channel.
    /// </summary>
    public uint SamplesPerFrame => _samplesPerFrame;

    /// <summary>
    /// Gets whether the background PCM pumping worker thread is actively executing.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _isRunning);

    /// <summary>
    /// Gets whether the underlying microphone sink pipeline is initialised and operational.
    /// </summary>
    public bool IsInitialized => !_disposed && _sink.IsInitialized;

    /// <summary>
    /// Gets the internal native virtual microphone sink pipeline.
    /// </summary>
    public HostVirtualMicSinkPipeline Sink => _sink;

    /// <summary>
    /// Gets the optional virtual audio IPC bridge pipeline.
    /// </summary>
    public VirtualAudioIpcBridgePipeline? IpcBridge => _ipcBridge;

    /// <summary>
    /// Initialises a new instance of the <see cref="HostMicrophoneUplinkService"/> class.
    /// </summary>
    /// <param name="sampleRate">Sampling frequency in Hz (default: 48000).</param>
    /// <param name="channels">Number of audio channels (default: 1 for mono voice capture).</param>
    /// <param name="frameDurationMs">Duration of each audio frame in milliseconds (default: 10ms).</param>
    /// <param name="ipcBridge">Optional virtual audio driver shared memory IPC bridge instance.</param>
    /// <param name="autoStartWorker">True to immediately start the background PCM pumping worker thread.</param>
    public HostMicrophoneUplinkService(
        uint sampleRate = 48000,
        uint channels = 1,
        uint frameDurationMs = 10,
        VirtualAudioIpcBridgePipeline? ipcBridge = null,
        bool autoStartWorker = true
    )
    {
        if (sampleRate == 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be greater than zero.");
        if (channels == 0) throw new ArgumentOutOfRangeException(nameof(channels), "Channel count must be greater than zero.");
        if (frameDurationMs == 0) throw new ArgumentOutOfRangeException(nameof(frameDurationMs), "Frame duration must be greater than zero.");

        _sampleRate = sampleRate;
        _channels = channels;
        _frameDurationMs = frameDurationMs;
        _samplesPerFrame = (sampleRate * frameDurationMs) / 1000;
        if (_samplesPerFrame == 0) _samplesPerFrame = 480;

        _stagingPcmBuffer = new float[_samplesPerFrame * _channels];
        _sink = new HostVirtualMicSinkPipeline(sampleRate, channels, targetLatencyMs: frameDurationMs);
        _ipcBridge = ipcBridge;

        if (autoStartWorker)
        {
            Start();
        }
    }

    /// <summary>
    /// Starts the background high-priority worker thread to pull decoded PCM and push to IPC bridge.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (_isRunning) return true;

            _workerCts = new CancellationTokenSource();
            _workerExitedEvent.Reset();
            _isRunning = true;

            _pumpThread = new Thread(PumpLoop)
            {
                Name = "Moonshine-HostMicrophoneUplinkThread",
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };

            _pumpThread.Start();
            return true;
        }
    }

    /// <summary>
    /// Stops the background worker thread and waits for execution to unwind cleanly.
    /// </summary>
    public void Stop()
    {
        Thread? worker;
        lock (_stateLock)
        {
            if (!_isRunning && _pumpThread == null) return;
            _isRunning = false;
            try
            {
                _workerCts?.Cancel();
            }
            // ALLOWED_EXCEPTION: Ignore disposed cancellation token during shutdown race.
            catch (ObjectDisposedException) { }
            worker = _pumpThread;
        }

        if (Thread.CurrentThread != worker && worker is not null && worker.IsAlive)
        {
            try
            {
                _workerExitedEvent.Wait(2000);
            }
            // ALLOWED_EXCEPTION: Ignore disposed synchronisation primitives during shutdown.
            catch (ObjectDisposedException) { }

            worker.Join();
        }

        lock (_stateLock)
        {
            try
            {
                _workerCts?.Dispose();
            }
            // ALLOWED_EXCEPTION: Ignore disposed cancellation token during cleanup.
            catch (ObjectDisposedException) { }
            _workerCts = null;
            _pumpThread = null;
        }
    }

    /// <summary>
    /// Ingests an incoming client microphone network datagram with zero heap allocations.
    /// Detects Moonshine Native Binary Protocol (MNBP) envelopes or RFC 3550 RTP framing,
    /// extracts the Opus payload, timestamp, and sequence number, and pushes into the jitter sink.
    /// </summary>
    public bool IngestDatagram(ReadOnlySpan<byte> datagram)
    {
        ThrowIfDisposed();
        if (datagram.IsEmpty)
        {
            return false;
        }

        // 1. Check for Moonshine Native Binary Protocol (MNBP) packet framing
        if (datagram.Length >= MoonshineProtocolConstants.HeaderSize)
        {
            uint magicBe = BinaryPrimitives.ReadUInt32BigEndian(datagram[..4]);
            uint magicLe = BinaryPrimitives.ReadUInt32LittleEndian(datagram[..4]);

            const uint MnbpMagic1 = 0x314D5348U; // '1MSH'
            const uint MnbpMagic1Le = 0x48534D31U; // 'H' 'S' 'M' '1'

            if (magicBe == MoonshineProtocolConstants.Magic ||
                magicBe == MnbpMagic1 ||
                magicLe == MnbpMagic1 ||
                magicBe == MnbpMagic1Le)
            {
                uint payloadSize = BinaryPrimitives.ReadUInt32BigEndian(datagram[8..12]);
                uint seqNum = BinaryPrimitives.ReadUInt32BigEndian(datagram[12..16]);
                ulong timestampUs = BinaryPrimitives.ReadUInt64BigEndian(datagram[24..32]);

                ReadOnlySpan<byte> body = datagram[MoonshineProtocolConstants.HeaderSize..];

                // Check for nested MoonshineMicPacketHeader within the payload
                if (body.Length >= MoonshineMicPacketCodec.HeaderSize &&
                    MoonshineMicPacketCodec.TryReadHeader(body, out MoonshineMicPacketHeader micHeader))
                {
                    int opusOffset = MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize;
                    int opusLength = micHeader.PayloadSize;

                    if (datagram.Length >= opusOffset + opusLength && opusLength > 0)
                    {
                        ReadOnlySpan<byte> opusPayload = datagram.Slice(opusOffset, opusLength);
                        uint ts = (uint)micHeader.SampleIndex;
                        if (ts == 0)
                        {
                            ts = (uint)timestampUs;
                        }
                        return _sink.TryPushOpusPacket(opusPayload, ts, (ushort)seqNum);
                    }
                }

                // Direct Opus payload following outer packet header
                if (payloadSize > 0 && datagram.Length >= MoonshineProtocolConstants.HeaderSize + (int)payloadSize)
                {
                    ReadOnlySpan<byte> directPayload = datagram.Slice(MoonshineProtocolConstants.HeaderSize, (int)payloadSize);
                    return _sink.TryPushOpusPacket(directPayload, (uint)timestampUs, (ushort)seqNum);
                }
            }
        }

        // 2. Check for RFC 3550 RTP microphone packet framing
        if (MicAudioPacket.TryParse(datagram, out MicAudioPacket rtpPacket))
        {
            if (!rtpPacket.Payload.IsEmpty)
            {
                return _sink.TryPushOpusPacket(rtpPacket.Payload, rtpPacket.Timestamp, rtpPacket.SequenceNumber);
            }
        }

        return false;
    }

    /// <summary>
    /// Pulls processed Float32 PCM samples from the sink and pushes to the virtual audio IPC bridge if connected.
    /// </summary>
    public bool PumpFrame() => PumpFrame(out _);

    /// <summary>
    /// Pulls processed Float32 PCM samples from the sink, outputs the number of processed samples,
    /// and pushes to the virtual audio IPC bridge if connected.
    /// </summary>
    public bool PumpFrame(out int samplesProcessed)
    {
        samplesProcessed = 0;
        ThrowIfDisposed();

        lock (_pcmProcessingLock)
        {
            if (_disposed)
            {
                return false;
            }

            Span<float> staging = _stagingPcmBuffer.AsSpan(0, (int)(_samplesPerFrame * _channels));
            if (_sink.TryPullPcm(staging, out int read) && read > 0)
            {
                samplesProcessed = read;
                if (_ipcBridge is not null && _ipcBridge.IsConnected)
                {
                    _ipcBridge.WriteCapturePcm(staging[..read]);
                }
                return true;
            }

            // Write zeroed silence buffer during starvation/underrun so virtual audio driver ring buffer continuously receives clean silence
            if (_ipcBridge is not null && _ipcBridge.IsConnected)
            {
                Span<float> silence = _stagingPcmBuffer.AsSpan(0, (int)(_samplesPerFrame * _channels));
                silence.Clear();
                _ipcBridge.WriteCapturePcm(silence);
            }

            return false;
        }
    }

    /// <summary>
    /// Dynamically adjusts microphone input gain multiplier in a thread-safe manner.
    /// </summary>
    public void SetGain(float gain)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            _sink.SetGain(gain);
        }
    }

    /// <summary>
    /// Toggles microphone mute state in a thread-safe manner.
    /// </summary>
    public void SetMute(bool isMuted)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            _sink.SetMute(isMuted);
        }
    }

    /// <summary>
    /// Retrieves active jitter buffer and rendering telemetry metrics in a thread-safe manner.
    /// </summary>
    public HostMicSinkMetrics GetMetrics()
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            return _sink.GetMetrics();
        }
    }

    private void PumpLoop()
    {
        try
        {
            var ct = _workerCts?.Token ?? CancellationToken.None;
            var framePeriod = TimeSpan.FromMilliseconds(_frameDurationMs);
            var nextTick = Stopwatch.GetTimestamp();
            long ticksPerFrame = (long)(framePeriod.TotalSeconds * Stopwatch.Frequency);

            while (!ct.IsCancellationRequested && Volatile.Read(ref _isRunning))
            {
                if (!_disposed)
                {
                    PumpFrame();
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
                    while (Stopwatch.GetTimestamp() < nextTick && !ct.IsCancellationRequested && Volatile.Read(ref _isRunning))
                    {
                        Thread.SpinWait(10);
                    }
                }
                else
                {
                    nextTick = now;
                }
            }
        }
        // ALLOWED_EXCEPTION: Ignore task cancellation or disposed synchronisation primitives during worker loop teardown.
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            try
            {
                _workerExitedEvent.Set();
            }
            // ALLOWED_EXCEPTION: Ignore disposed synchronisation primitives during teardown.
            catch (ObjectDisposedException) { }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes the uplink service, halts worker threads, and releases unmanaged microphone sink resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
                lock (_pcmProcessingLock)
                {
                    _sink.Dispose();
                    _disposed = true;
                }
            }
            else
            {
                _disposed = true;
            }
        }
    }
}
