using Moonshine.Interop;

namespace Moonshine.Core.Audio;

public enum AudioChannelConfiguration : ushort
{
    Stereo = 2,
    Surround51 = 6,
    Surround71 = 8
}

public sealed record AudioPipelineMetrics(
    ulong FramesSubmitted,
    ulong FramesRendered,
    uint BufferUnderruns
);

/// <summary>
/// Sub-5ms low-latency Windows Audio Session API (WASAPI) Exclusive Mode audio rendering pipeline.
/// Supports stereo, 5.1 surround, and 7.1 surround sound audio at 48kHz with 32-bit floating point PCM.
/// </summary>
public sealed class MoonshineAudioPipeline : IDisposable
{
    private IntPtr _audioHandle;
    private readonly Lock _lock = new();
    private bool _disposed;
    private ulong _framesSubmitted;

    public IntPtr Handle => _audioHandle;
    public uint SampleRate { get; }
    public AudioChannelConfiguration Channels { get; }
    public bool IsExclusive { get; }

    public AudioPipelineMetrics Metrics
    {
        get
        {
            lock (_lock)
            {
                if (_disposed || _audioHandle == IntPtr.Zero)
                {
                    return new AudioPipelineMetrics(_framesSubmitted, 0, 0);
                }
                MoonshineNativeMethods.AudioGetMetrics(_audioHandle, out ulong rendered, out uint underruns);
                return new AudioPipelineMetrics(_framesSubmitted, rendered, underruns);
            }
        }
    }

    public MoonshineAudioPipeline(
        uint sampleRate = 48000,
        AudioChannelConfiguration channels = AudioChannelConfiguration.Stereo,
        bool isExclusive = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        SampleRate = sampleRate;
        Channels = channels;
        IsExclusive = isExclusive;

        _audioHandle = MoonshineNativeMethods.AudioCreateWasapi(
            sampleRate,
            (ushort)channels,
            (ushort)(isExclusive ? 1 : 0)
        );

        if (_audioHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize WASAPI low-latency audio renderer.");
        }
    }

    /// <summary>
    /// Submits 32-bit floating point PCM audio samples to the audio engine with zero allocations.
    /// </summary>
    public unsafe bool SubmitPcm(ReadOnlySpan<float> pcmData)
    {
        if (pcmData.IsEmpty) return false;

        lock (_lock)
        {
            if (_disposed || _audioHandle == IntPtr.Zero) return false;

            uint sampleCount = (uint)(pcmData.Length / (int)Channels);
            if (sampleCount == 0) return false;

            fixed (float* pcmPtr = pcmData)
            {
                int res = MoonshineNativeMethods.AudioSubmitPcm(_audioHandle, pcmPtr, sampleCount);
                if (res != 0) return false;

                Interlocked.Add(ref _framesSubmitted, sampleCount);
                return true;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_audioHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.AudioDestroy(_audioHandle);
                _audioHandle = IntPtr.Zero;
            }
        }
    }
}
