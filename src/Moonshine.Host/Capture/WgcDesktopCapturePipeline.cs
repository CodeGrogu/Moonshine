using System.Diagnostics;
using Moonshine.Interop;

namespace Moonshine.Host.Capture;

/// <summary>
/// Modern Windows.Graphics.Capture & Direct3D 12 Low-Latency Desktop Ingestion Pipeline.
/// Provides high-precision frame pacing and hybrid GPU multi-adapter compatibility.
/// </summary>
public sealed class WgcDesktopCapturePipeline : IDesktopCapturePipeline
{
    private readonly IntPtr _hmonitor;
    private readonly uint _targetFps;
    private IntPtr _handle;
    private uint _width;
    private uint _height;
    private bool _disposed;
    private readonly Lock _lock = new();

    private ulong _framesCaptured;
    private ulong _timeoutsCount;
    private ulong _captureErrorsCount;
    private ulong _lastFrameTimestampQpc;

    public uint Width => Volatile.Read(ref _width);
    public uint Height => Volatile.Read(ref _height);
    public uint TargetFps => _targetFps;
    public bool IsAvailable => _handle != IntPtr.Zero;

    public CaptureMetrics Metrics => new(
        Volatile.Read(ref _framesCaptured),
        Volatile.Read(ref _timeoutsCount),
        Volatile.Read(ref _captureErrorsCount),
        Volatile.Read(ref _lastFrameTimestampQpc),
        Volatile.Read(ref _width),
        Volatile.Read(ref _height)
    );

    public WgcDesktopCapturePipeline(IntPtr hmonitor = 0, uint targetFps = 60)
    {
        _hmonitor = hmonitor;
        _targetFps = targetFps > 0 ? targetFps : 60;
        Initialize();
    }

    private void Initialize()
    {
        lock (_lock)
        {
            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.CaptureDestroy(_handle);
                _handle = IntPtr.Zero;
            }

            _handle = MoonshineNativeMethods.CaptureCreateWgc(_hmonitor, _targetFps, out _width, out _height);
        }
    }

    public bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                frame = default;
                return false;
            }

            int result = MoonshineNativeMethods.CaptureAcquireFrame(_handle, timeoutMs, out frame);
            if (result > 0)
            {
                Interlocked.Increment(ref _framesCaptured);
                Volatile.Write(ref _lastFrameTimestampQpc, frame.TimestampQpc);
                return true;
            }

            if (result == 0)
            {
                Interlocked.Increment(ref _timeoutsCount);
                return false;
            }

            Interlocked.Increment(ref _captureErrorsCount);
            return false;
        }
    }

    public void ReleaseFrame()
    {
        lock (_lock)
        {
            if (!_disposed && _handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.CaptureReleaseFrame(_handle);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.CaptureDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
