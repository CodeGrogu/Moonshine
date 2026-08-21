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
    private uint _format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
    private bool _disposed;
    private readonly Lock _lock = new();

    private ulong _framesCaptured;
    private ulong _timeoutsCount;
    private ulong _captureErrorsCount;
    private ulong _lastFrameTimestampQpc;
    private ulong _totalAcquisitionTimeQpc;

    public uint Width => Volatile.Read(ref _width);
    public uint Height => Volatile.Read(ref _height);
    public uint Format => Volatile.Read(ref _format);
    public bool IsHdr => false;
    public uint AdapterIndex => 0;
    public uint OutputIndex => 0;
    public uint TargetFps => _targetFps;
    public bool IsAvailable => _handle != IntPtr.Zero;

    public CaptureMetrics Metrics
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesCaptured);
            ulong totalQpc = Volatile.Read(ref _totalAcquisitionTimeQpc);
            double avgUs = frames > 0 ? (double)totalQpc / frames * (1_000_000.0 / Stopwatch.Frequency) : 0.0;

            return new CaptureMetrics(
                frames,
                Volatile.Read(ref _timeoutsCount),
                Volatile.Read(ref _captureErrorsCount),
                Volatile.Read(ref _lastFrameTimestampQpc),
                Volatile.Read(ref _width),
                Volatile.Read(ref _height),
                Volatile.Read(ref _format),
                false,
                avgUs
            );
        }
    }

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

    /// <summary>
    /// Acquires the next available desktop frame texture. Zero GC allocations on the hot path.
    /// </summary>
    public bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
    {
        long startQpc = Stopwatch.GetTimestamp();

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
                long elapsed = Stopwatch.GetTimestamp() - startQpc;
                Interlocked.Increment(ref _framesCaptured);
                Interlocked.Add(ref _totalAcquisitionTimeQpc, (ulong)elapsed);
                Volatile.Write(ref _lastFrameTimestampQpc, frame.TimestampQpc);
                Volatile.Write(ref _format, frame.Format);
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

    public bool TryRecover()
    {
        lock (_lock)
        {
            if (_disposed) return false;

            if (_handle != IntPtr.Zero)
            {
                if (MoonshineNativeMethods.CaptureRecover(_handle) > 0)
                {
                    return true;
                }
            }

            Initialize();
            return _handle != IntPtr.Zero;
        }
    }

    ~WgcDesktopCapturePipeline()
    {
        Dispose(false);
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
                MoonshineNativeMethods.CaptureDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
