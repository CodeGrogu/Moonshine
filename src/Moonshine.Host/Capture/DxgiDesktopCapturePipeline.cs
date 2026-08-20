using System.Diagnostics;
using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public sealed record CaptureMetrics(
    ulong FramesCaptured,
    ulong TimeoutsCount,
    ulong CaptureErrorsCount,
    ulong LastFrameTimestampQpc,
    uint Width,
    uint Height
);

/// <summary>
/// Direct3D 11/12 DXGI Desktop Duplication Capture Pipeline.
/// Provides high-throughput, zero-copy VRAM surface acquisition for video encoders.
/// </summary>
public sealed class DxgiDesktopCapturePipeline : IDisposable
{
    private readonly uint _adapterIndex;
    private readonly uint _outputIndex;
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
    public bool IsAvailable => _handle != IntPtr.Zero;

    public CaptureMetrics Metrics => new(
        Volatile.Read(ref _framesCaptured),
        Volatile.Read(ref _timeoutsCount),
        Volatile.Read(ref _captureErrorsCount),
        Volatile.Read(ref _lastFrameTimestampQpc),
        Volatile.Read(ref _width),
        Volatile.Read(ref _height)
    );

    public DxgiDesktopCapturePipeline(uint adapterIndex = 0, uint outputIndex = 0)
    {
        _adapterIndex = adapterIndex;
        _outputIndex = outputIndex;
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

            _handle = MoonshineNativeMethods.CaptureCreateDxgi(_adapterIndex, _outputIndex, out _width, out _height);
        }
    }

    /// <summary>
    /// Acquires the next available desktop frame texture.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a new presented frame.</param>
    /// <param name="frame">Descriptor populated with shared texture handle and metadata.</param>
    /// <returns>True if a new frame was acquired; false on timeout or error.</returns>
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
                // Timeout (no new frame rendered by desktop)
                Interlocked.Increment(ref _timeoutsCount);
                return false;
            }

            // Error occurred (e.g. display mode change or device lost)
            Interlocked.Increment(ref _captureErrorsCount);
            return false;
        }
    }

    /// <summary>
    /// Releases the currently held desktop duplication frame.
    /// </summary>
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
