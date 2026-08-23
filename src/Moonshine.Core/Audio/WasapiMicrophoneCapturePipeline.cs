using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Core.Audio;

/// <summary>
/// Low-latency WASAPI Microphone Audio Capture Pipeline.
/// Captures communications and microphone audio directly from the default input device,
/// supporting 48kHz mono and stereo float32 streams with zero GC allocations.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: native SafeHandleStore defers deallocation via shared_ptr, preventing GC-thread use-after-free races during audio teardown.")]
public sealed class WasapiMicrophoneCapturePipeline : IDisposable
{
    private IntPtr _handle;
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly uint _bufferDurationMs;
    private bool _disposed;
    private readonly Lock _lock = new();

    public uint SampleRate => _sampleRate;
    public uint Channels => _channels;
    public uint BufferDurationMs => _bufferDurationMs;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                if (_disposed || _handle == IntPtr.Zero) return false;
                return MoonshineNativeMethods.MicCaptureIsActive(_handle) != 0;
            }
        }
    }

    public WasapiMicrophoneCapturePipeline(
        uint sampleRate = 48000,
        uint channels = 1,
        uint bufferDurationMs = 10
    )
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bufferDurationMs = bufferDurationMs;

        _handle = MoonshineNativeMethods.MicCaptureCreate(sampleRate, channels, bufferDurationMs);
    }

    public unsafe bool TryReadSamples(
        Span<float> outPcm,
        out int samplesRead,
        out ulong timestampQpc
    )
    {
        samplesRead = 0;
        timestampQpc = 0;

        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            fixed (float* bufferPtr = outPcm)
            {
                int res = MoonshineNativeMethods.MicCaptureReadFloat(
                    _handle,
                    bufferPtr,
                    (uint)outPcm.Length,
                    out uint read,
                    out timestampQpc
                );

                if (res > 0)
                {
                    samplesRead = (int)read;
                    return true;
                }

                return false;
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
                MoonshineNativeMethods.MicCaptureDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
