using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Core.Audio;

public sealed record OpusDecoderPipelineMetrics(
    ulong TotalFramesDecoded,
    ulong TotalSamplesDecoded,
    uint DecodeErrors,
    uint ConcealmentFrames,
    double AvgDecodeTimeUs,
    uint StreamsCount
);

/// <summary>
/// Low-latency multi-channel Opus audio decoding pipeline wrapper with zero GC allocations.
/// Supports 48kHz Stereo, Surround 5.1, and Surround 7.1 with Vorbis stream mapping.
/// </summary>
public sealed class OpusAudioDecoderPipeline : IDisposable
{
    private IntPtr _decoderHandle;
    private readonly Lock _lock = new();
    private bool _disposed;

    public uint SampleRate { get; }
    public AudioChannelConfiguration Channels { get; }

    public OpusDecoderPipelineMetrics Metrics
    {
        get
        {
            lock (_lock)
            {
                if (_disposed || _decoderHandle == IntPtr.Zero)
                {
                    return new OpusDecoderPipelineMetrics(0, 0, 0, 0, 0, 1);
                }

                MoonshineNativeMethods.OpusDecoderGetMetrics(
                    _decoderHandle,
                    out ulong frames,
                    out ulong samples,
                    out uint errors,
                    out uint concealment,
                    out double avgTimeUs,
                    out uint streams
                );

                return new OpusDecoderPipelineMetrics(frames, samples, errors, concealment, avgTimeUs, streams);
            }
        }
    }

    public OpusAudioDecoderPipeline(
        uint sampleRate = 48000,
        AudioChannelConfiguration channels = AudioChannelConfiguration.Stereo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        SampleRate = sampleRate;
        Channels = channels;

        _decoderHandle = MoonshineNativeMethods.OpusDecoderCreate(sampleRate, (uint)channels);
        if (_decoderHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to initialize Opus audio decoder for {sampleRate}Hz, {channels} channels.");
        }
    }

    /// <summary>
    /// Decodes an Opus compressed packet into Float32 PCM [-1.0f, 1.0f] samples with zero allocations.
    /// </summary>
    public unsafe bool DecodeFloat(
        ReadOnlySpan<byte> opusPayload,
        Span<float> outPcmSamples,
        out uint samplesDecoded,
        bool decodeFec = false)
    {
        samplesDecoded = 0;
        if (outPcmSamples.IsEmpty) return false;

        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return false;

            fixed (byte* payloadPtr = opusPayload)
            fixed (float* pcmPtr = outPcmSamples)
            {
                int res = MoonshineNativeMethods.OpusDecoderDecodeFloat(
                    _decoderHandle,
                    payloadPtr,
                    (uint)opusPayload.Length,
                    pcmPtr,
                    (uint)outPcmSamples.Length,
                    out samplesDecoded,
                    decodeFec ? 1 : 0
                );

                return res != 0;
            }
        }
    }

    /// <summary>
    /// Decodes an Opus compressed packet into Int16 PCM [-32768, 32767] samples with zero allocations.
    /// </summary>
    public unsafe bool DecodePcm16(
        ReadOnlySpan<byte> opusPayload,
        Span<short> outPcmSamples,
        out uint samplesDecoded,
        bool decodeFec = false)
    {
        samplesDecoded = 0;
        if (outPcmSamples.IsEmpty) return false;

        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return false;

            fixed (byte* payloadPtr = opusPayload)
            fixed (short* pcmPtr = outPcmSamples)
            {
                int res = MoonshineNativeMethods.OpusDecoderDecodePcm16(
                    _decoderHandle,
                    payloadPtr,
                    (uint)opusPayload.Length,
                    pcmPtr,
                    (uint)outPcmSamples.Length,
                    out samplesDecoded,
                    decodeFec ? 1 : 0
                );

                return res != 0;
            }
        }
    }

    /// <summary>
    /// Resets internal decoder state and history filters.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            if (!_disposed && _decoderHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusDecoderReset(_decoderHandle);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_decoderHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusDecoderDestroy(_decoderHandle);
                _decoderHandle = IntPtr.Zero;
            }
        }
    }
}
