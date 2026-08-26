using System.Diagnostics;
using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public sealed record CaptureMetrics(
    ulong FramesCaptured,
    ulong TimeoutsCount,
    ulong CaptureErrorsCount,
    ulong LastFrameTimestampQpc,
    uint Width,
    uint Height,
    uint Format,
    bool IsHdr,
    double AverageAcquisitionTimeMicroseconds
);

/// <summary>
/// Direct3D 11/12 DXGI Desktop Duplication Capture Pipeline.
/// Provides high-throughput, zero-copy VRAM surface acquisition for video encoders.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: unmanaged handle lifetime is managed via SafeHandleStore and deterministic Dispose.")]
public sealed class DxgiDesktopCapturePipeline : IDesktopCapturePipeline
{
    private uint _adapterIndex;
    private uint _outputIndex;
    private CaptureSourceDescriptor? _source;
    private IntPtr _handle;
    private uint _width;
    private uint _height;
    private uint _format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
    private bool _isHdr;
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
    public bool IsHdr => Volatile.Read(ref _isHdr);
    public uint AdapterIndex => _adapterIndex;
    public uint OutputIndex => _outputIndex;
    public CaptureSourceDescriptor? Source => _source;
    public bool IsAvailable => _handle != IntPtr.Zero;
    public IntPtr DeviceHandle => _handle != IntPtr.Zero ? MoonshineNativeMethods.CaptureGetDevice(_handle) : IntPtr.Zero;

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
                Volatile.Read(ref _isHdr),
                avgUs
            );
        }
    }

    public DxgiDesktopCapturePipeline(uint adapterIndex = 0, uint outputIndex = 0)
    {
        _adapterIndex = adapterIndex;
        _outputIndex = outputIndex;
        Initialize();
    }

    public DxgiDesktopCapturePipeline(CaptureSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _adapterIndex = source.AdapterIndex;
        _outputIndex = source.OutputIndex;
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
            if (_handle != IntPtr.Zero)
            {
                _format = MoonshineNativeMethods.CaptureGetFormat(_handle);
                _isHdr = MoonshineNativeMethods.CaptureIsHdr(_handle) != 0;
            }
        }
    }

    /// <summary>
    /// Acquires the next available desktop frame texture. Zero GC allocations on the hot path.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a new presented frame.</param>
    /// <param name="frame">Descriptor populated with shared texture handle and metadata.</param>
    /// <returns>True if a new frame was acquired; false on timeout or error.</returns>
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
                // Timeout (no new frame rendered by desktop compositor)
                Interlocked.Increment(ref _timeoutsCount);
                return false;
            }

            // Error occurred (e.g. display mode change, UAC secure desktop switch, or device lost)
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

    /// <summary>
    /// Recovers the capture session after display mode change, resolution switch, or device lost event.
    /// </summary>
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

    /// <summary>
    /// Dynamically reconfigures the capture source to a new display output.
    /// </summary>
    public bool TryReconfigureSource(CaptureSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_lock)
        {
            if (_disposed) return false;
            _source = source;
            _adapterIndex = source.AdapterIndex;
            _outputIndex = source.OutputIndex;
            Initialize();
            return _handle != IntPtr.Zero;
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
        GC.SuppressFinalize(this);
    }
}
