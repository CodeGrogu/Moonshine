using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Audio;

/// <summary>
/// Low-latency multi-channel Opus audio compression pipeline.
/// Compresses 48kHz PCM audio into Opus frames across Stereo, Surround 5.1,
/// and Surround 7.1 with sub-1ms encode latency and zero GC allocations.
/// </summary>
public sealed class OpusAudioEncoderPipeline : IDisposable
{
    private IntPtr _handle;
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private uint _bitrate;
    private readonly uint _frameDurationMs;
    private uint _complexity;
    private readonly bool _useVbr;
    private readonly AudioChannelTopology _topology;
    private uint _streamsCount;
    private bool _disposed;
    private readonly Lock _lock = new();

    public uint SampleRate => _sampleRate;
    public uint Channels => _channels;
    public uint Bitrate => _bitrate;
    public uint FrameDurationMs => _frameDurationMs;
    public uint Complexity => _complexity;
    public bool UseVbr => _useVbr;
    public AudioChannelTopology Topology => _topology;
    public uint StreamsCount => _streamsCount;
    public bool IsActive => _handle != IntPtr.Zero && !_disposed;

    public OpusAudioEncoderPipeline(
        uint sampleRate = 48000,
        AudioChannelTopology topology = AudioChannelTopology.Stereo,
        uint bitrate = 160000,
        uint frameDurationMs = 5,
        uint complexity = 8,
        bool useVbr = true
    )
    {
        _sampleRate = sampleRate;
        _topology = topology;
        _channels = (uint)topology;
        _bitrate = bitrate;
        _frameDurationMs = frameDurationMs;
        _complexity = complexity;
        _useVbr = useVbr;

        _handle = MoonshineNativeMethods.OpusEncoderCreate(
            sampleRate,
            _channels,
            bitrate,
            frameDurationMs,
            complexity,
            useVbr ? 1 : 0
        );

        if (_handle != IntPtr.Zero)
        {
            MoonshineNativeMethods.OpusEncoderGetMetrics(
                _handle,
                out _,
                out _,
                out _,
                out _,
                out _streamsCount
            );
        }
    }

    ~OpusAudioEncoderPipeline()
    {
        Dispose(false);
    }

    public unsafe bool TryEncode(
        ReadOnlySpan<float> pcmSamples,
        uint frameSamples,
        Span<byte> outPayload,
        out int bytesWritten
    )
    {
        bytesWritten = 0;

        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            fixed (float* pcmPtr = pcmSamples)
            fixed (byte* outPtr = outPayload)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(
                    _handle,
                    pcmPtr,
                    frameSamples,
                    outPtr,
                    (uint)outPayload.Length,
                    out uint written
                );

                if (res > 0)
                {
                    bytesWritten = (int)written;
                    return true;
                }

                return false;
            }
        }
    }

    public unsafe bool TryEncodePcm16(
        ReadOnlySpan<short> pcmSamples,
        uint frameSamples,
        Span<byte> outPayload,
        out int bytesWritten
    )
    {
        bytesWritten = 0;

        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            fixed (short* pcmPtr = pcmSamples)
            fixed (byte* outPtr = outPayload)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodePcm16(
                    _handle,
                    pcmPtr,
                    frameSamples,
                    outPtr,
                    (uint)outPayload.Length,
                    out uint written
                );

                if (res > 0)
                {
                    bytesWritten = (int)written;
                    return true;
                }

                return false;
            }
        }
    }

    public bool SetBitrate(uint bitrate)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            int res = MoonshineNativeMethods.OpusEncoderSetBitrate(_handle, bitrate);
            if (res > 0)
            {
                _bitrate = bitrate;
                return true;
            }
            return false;
        }
    }

    public bool SetComplexity(uint complexity)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            int res = MoonshineNativeMethods.OpusEncoderSetComplexity(_handle, complexity);
            if (res > 0)
            {
                _complexity = complexity;
                return true;
            }
            return false;
        }
    }

    public void GetMetrics(
        out ulong framesEncoded,
        out ulong bytesEncoded,
        out double avgEncodeTimeUs,
        out uint currentBitrate,
        out uint streamsCount
    )
    {
        framesEncoded = 0;
        bytesEncoded = 0;
        avgEncodeTimeUs = 0.0;
        currentBitrate = 0;
        streamsCount = _streamsCount;

        lock (_lock)
        {
            if (!_disposed && _handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusEncoderGetMetrics(
                    _handle,
                    out framesEncoded,
                    out bytesEncoded,
                    out avgEncodeTimeUs,
                    out currentBitrate,
                    out streamsCount
                );
                _streamsCount = streamsCount;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusEncoderDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
