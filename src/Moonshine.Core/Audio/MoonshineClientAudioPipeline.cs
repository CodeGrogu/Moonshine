using System.Runtime.InteropServices;
using Moonshine.Core.Media;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
#if MOONSHINE_LEGACY_INTEROP
using Moonshine.Protocol.RTP;
#endif

namespace Moonshine.Core.Audio;

public sealed record ClientAudioPipelineMetrics(
    ulong PacketsReceived,
    ulong FramesDecoded,
    ulong FramesRendered,
    uint BufferUnderruns,
    uint DecodeErrors,
    uint ConcealmentFrames,
    double AvgDecodeLatencyUs
);

/// <summary>
/// Production client-side audio pipeline coordinating low-latency packet ingestion,
/// jitter buffering, Opus decoding, and WASAPI rendering with zero GC allocations.
/// </summary>
public sealed class MoonshineClientAudioPipeline : IDisposable
{
    private readonly Lock _stateLock = new();
    private OpusAudioDecoderPipeline _decoder;
    private MoonshineAudioPipeline _renderer;
    private readonly AudioJitterBuffer _jitterBuffer;

    private readonly Thread? _playbackThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _runBackgroundWorker;
    private bool _disposed;
    private bool _running;

    private uint _sampleRate;
    private AudioChannelConfiguration _channels;
    private readonly bool _isExclusive;

    private ulong _packetsReceived;
    private ulong _framesDecoded;
    private ulong _framesRendered;
    private uint _underruns;

    private readonly byte[] _popBuffer = new byte[2048];
    private readonly float[] _decodePcmBuffer;

    public uint SampleRate => _sampleRate;
    public AudioChannelConfiguration Channels => _channels;
    public bool IsExclusive => _isExclusive;
    public bool IsRunning => _running;

    public ClientAudioPipelineMetrics Metrics
    {
        get
        {
            lock (_stateLock)
            {
                var decMetrics = _decoder.Metrics;
                var rendMetrics = _renderer.Metrics;
                var jitMetrics = _jitterBuffer.Metrics;

                return new ClientAudioPipelineMetrics(
                    _packetsReceived,
                    _framesDecoded,
                    _framesRendered,
                    _underruns + jitMetrics.BufferUnderruns + rendMetrics.BufferUnderruns,
                    decMetrics.DecodeErrors,
                    decMetrics.ConcealmentFrames,
                    decMetrics.AvgDecodeTimeUs
                );
            }
        }
    }

    public MoonshineClientAudioPipeline(
        uint sampleRate = 48000,
        AudioChannelConfiguration channels = AudioChannelConfiguration.Stereo,
        bool isExclusive = false,
        bool startBackgroundWorker = true)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _isExclusive = isExclusive;
        _runBackgroundWorker = startBackgroundWorker;

        _decoder = new OpusAudioDecoderPipeline(sampleRate, channels);
        _renderer = new MoonshineAudioPipeline(sampleRate, channels, isExclusive);
        _jitterBuffer = new AudioJitterBuffer(capacity: 64, maxPacketSize: 2048);

        // Preallocate decode float buffer for 20ms frame worst-case
        int maxSamples = (int)((sampleRate * 20) / 1000) * (int)channels;
        _decodePcmBuffer = new float[maxSamples];

        if (_runBackgroundWorker)
        {
            _running = true;
            _playbackThread = new Thread(PlaybackWorkerLoop)
            {
                Name = "MoonshineClientAudioPlaybackWorker",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _playbackThread.Start();
        }
    }

    /// <summary>
    /// Ingests a raw compressed Opus frame with sequence number and QPC timestamp.
    /// </summary>
    public bool IngestRawOpusFrame(uint sequence, ulong timestampQpc, ReadOnlySpan<byte> opusPayload)
    {
        if (opusPayload.IsEmpty) return false;

        Interlocked.Increment(ref _packetsReceived);
        return _jitterBuffer.Push(sequence, timestampQpc, opusPayload);
    }

    /// <summary>
    /// Ingests a native Moonshine media datagram packet.
    /// </summary>
    public bool IngestMoonshinePacket(ReadOnlySpan<byte> packetData)
    {
        if (packetData.Length < MoonshineProtocolConstants.HeaderSize + MoonshineAudioPacketCodec.HeaderSize)
        {
            return false;
        }

        if (MoonshineProtocolCodec.TryReadHeader(packetData, out var mshnHdr) != MoonshineErrorCode.Success)
        {
            return false;
        }

        if (!MoonshineAudioPacketCodec.TryReadHeader(packetData[MoonshineProtocolConstants.HeaderSize..], out var audioHdr))
        {
            return false;
        }

        int payloadOffset = MoonshineProtocolConstants.HeaderSize + MoonshineAudioPacketCodec.HeaderSize;
        if (packetData.Length < payloadOffset + audioHdr.PayloadSize)
        {
            return false;
        }

        var incomingChannels = (AudioChannelConfiguration)audioHdr.Channels;
        if (incomingChannels != _channels && (incomingChannels == AudioChannelConfiguration.Stereo ||
            incomingChannels == AudioChannelConfiguration.Surround51 ||
            incomingChannels == AudioChannelConfiguration.Surround71))
        {
            ReconfigureFormat(audioHdr.SampleRate > 0 ? audioHdr.SampleRate : _sampleRate, incomingChannels);
        }

        ReadOnlySpan<byte> payload = packetData.Slice(payloadOffset, audioHdr.PayloadSize);
        return IngestRawOpusFrame(mshnHdr.SequenceNumber, audioHdr.SampleIndex, payload);
    }

#if MOONSHINE_LEGACY_INTEROP
    /// <summary>
    /// Ingests a standard RFC 3550 RTP audio packet.
    /// </summary>
    public bool IngestRtpPacket(ReadOnlySpan<byte> packetData)
    {
        if (!RtpAudioHeader.TryParse(packetData, out var rtpHdr, out var payload))
        {
            return false;
        }

        return IngestRawOpusFrame(rtpHdr.BaseHeader.SequenceNumber, rtpHdr.BaseHeader.Timestamp, payload);
    }
#endif

    /// <summary>
    /// Processes a single frame immediately without background thread jitter delay.
    /// Useful for ultra-low latency direct dispatch or deterministic benchmarking.
    /// </summary>
    public bool ProcessDirectFrame(ReadOnlySpan<byte> opusPayload, bool isConcealment = false)
    {
        lock (_stateLock)
        {
            if (_disposed) return false;

            Span<float> pcmSpan = _decodePcmBuffer.AsSpan();
            bool decOk = _decoder.DecodeFloat(opusPayload, pcmSpan, out uint samplesDecoded, isConcealment);
            if (!decOk || samplesDecoded == 0)
            {
                return false;
            }

            _framesDecoded++;
            bool renderOk = _renderer.SubmitPcm(pcmSpan[..(int)samplesDecoded]);
            if (renderOk)
            {
                _framesRendered++;
            }
            return renderOk;
        }
    }

    /// <summary>
    /// Dynamically reconfigures audio format (e.g. Stereo to 5.1/7.1 Surround).
    /// </summary>
    public void ReconfigureFormat(uint sampleRate, AudioChannelConfiguration channels)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            if (_sampleRate == sampleRate && _channels == channels) return;

            _sampleRate = sampleRate;
            _channels = channels;

            _decoder.Dispose();
            _renderer.Dispose();

            _decoder = new OpusAudioDecoderPipeline(sampleRate, channels);
            _renderer = new MoonshineAudioPipeline(sampleRate, channels, _isExclusive);
            _jitterBuffer.Reset();
        }
    }

    private void PlaybackWorkerLoop()
    {
        const int frameIntervalMs = 5;

        while (!_cts.Token.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            lock (_stateLock)
            {
                if (_disposed) break;

                Span<byte> popSpan = _popBuffer.AsSpan();
                bool popped = _jitterBuffer.Pop(popSpan, out int bytesPopped, out _, out _);

                Span<float> pcmSpan = _decodePcmBuffer.AsSpan();
                if (popped && bytesPopped > 0)
                {
                    bool decOk = _decoder.DecodeFloat(popSpan[..bytesPopped], pcmSpan, out uint samplesDecoded, false);
                    if (decOk && samplesDecoded > 0)
                    {
                        _framesDecoded++;
                        if (_renderer.SubmitPcm(pcmSpan[..(int)samplesDecoded]))
                        {
                            _framesRendered++;
                        }
                    }
                }
                else
                {
                    // Underrun PLC concealment frame
                    _underruns++;
                    if (_decoder.DecodeFloat(ReadOnlySpan<byte>.Empty, pcmSpan, out uint samplesDecoded, true))
                    {
                        _renderer.SubmitPcm(pcmSpan[..(int)samplesDecoded]);
                    }
                }
            }

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            int sleepTime = Math.Max(1, (int)(frameIntervalMs - elapsed));
            Thread.Sleep(sleepTime);
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _cts.Cancel();
        }

        _playbackThread?.Join(500);

        lock (_stateLock)
        {
            _decoder.Dispose();
            _renderer.Dispose();
            _cts.Dispose();
        }
    }
}
