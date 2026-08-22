using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Core.Video;

public sealed record SwapchainMetrics(
    ulong FramesPresented,
    ulong PresentationErrors,
    ulong DroppedFrames
);

/// <summary>
/// Low-latency DXGI Flip Model swapchain presentation pipeline.
/// Supports tearing for Variable Refresh Rate (VRR / G-Sync / FreeSync), 1-frame latency waitable object,
/// and 10-bit HDR10 color spaces with direct zero-copy GPU texture presentation.
/// </summary>
public sealed class DxgiSwapchainPipeline : IDisposable
{
    private IntPtr _swapchainHandle;
    private readonly Lock _lock = new();
    private bool _disposed;
    private ulong _framesPresented;
    private ulong _presentationErrors;

    public IntPtr Handle => _swapchainHandle;
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint BufferCount { get; }
    public bool IsHdr10 { get; private set; }
    public bool IsTearingSupported { get; }
    public IntPtr WaitableObject { get; }

    public SwapchainMetrics Metrics
    {
        get
        {
            lock (_lock)
            {
                if (!_disposed && _swapchainHandle != IntPtr.Zero &&
                    MoonshineNativeMethods.SwapchainGetMetrics(_swapchainHandle, out var nativeMetrics) == 0)
                {
                    return new(nativeMetrics.FramesPresented, nativeMetrics.PresentationErrors, nativeMetrics.DroppedFrames);
                }

                return new(
                    Volatile.Read(ref _framesPresented),
                    Volatile.Read(ref _presentationErrors),
                    0
                );
            }
        }
    }

    public const uint DxgipresentAllowTearing = 0x00000200;

    public DxgiSwapchainPipeline(
        IntPtr hwnd,
        IntPtr d3d11Device,
        uint width,
        uint height,
        uint bufferCount = 2,
        bool isHdr10 = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        BufferCount = bufferCount < 2 ? 2 : (bufferCount > 4 ? 4 : bufferCount);
        IsHdr10 = isHdr10;

        _swapchainHandle = MoonshineNativeMethods.SwapchainCreate(
            hwnd,
            d3d11Device,
            width,
            height,
            BufferCount,
            (byte)(isHdr10 ? 1 : 0)
        );

        if (_swapchainHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize low-latency DXGI swapchain.");
        }

        IsTearingSupported = MoonshineNativeMethods.SwapchainIsTearingSupported(_swapchainHandle) != 0;
        WaitableObject = MoonshineNativeMethods.SwapchainGetWaitableObject(_swapchainHandle);
    }

    /// <summary>
    /// Presents the current swapchain backbuffer to the display.
    /// </summary>
    public bool Present(uint syncInterval = 0, uint flags = DxgipresentAllowTearing)
    {
        lock (_lock)
        {
            if (_disposed || _swapchainHandle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.SwapchainPresent(_swapchainHandle, syncInterval, flags);
            if (res != 0)
            {
                Interlocked.Increment(ref _presentationErrors);
                return false;
            }

            Interlocked.Increment(ref _framesPresented);
            return true;
        }
    }

    /// <summary>
    /// Performs zero-copy GPU texture presentation: blits/copies the decoded surface directly
    /// into the swapchain backbuffer and presents to the display with minimal latency.
    /// </summary>
    public bool PresentTexture(IntPtr textureHandle, uint syncInterval = 0, uint flags = DxgipresentAllowTearing)
    {
        lock (_lock)
        {
            if (_disposed || _swapchainHandle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.SwapchainPresentTexture(_swapchainHandle, textureHandle, syncInterval, flags);
            if (res != 0)
            {
                Interlocked.Increment(ref _presentationErrors);
                return false;
            }

            Interlocked.Increment(ref _framesPresented);
            return true;
        }
    }

    /// <summary>
    /// Resizes the swapchain buffer dimensions on window resize.
    /// </summary>
    public bool Resize(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (_lock)
        {
            if (_disposed || _swapchainHandle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.SwapchainResize(_swapchainHandle, width, height);
            if (res == 0)
            {
                Width = width;
                Height = height;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Dynamically toggles HDR10 (ST 2084 / Rec.2020) color space.
    /// </summary>
    public bool SetHdr(bool isHdr10)
    {
        lock (_lock)
        {
            if (_disposed || _swapchainHandle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.SwapchainSetHdr(_swapchainHandle, (byte)(isHdr10 ? 1 : 0));
            if (res == 0)
            {
                IsHdr10 = isHdr10;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Configures SMPTE ST 2086 HDR10 mastering metadata on the swapchain.
    /// </summary>
    public bool SetHdrMetadata(in MoonshineHdr10Metadata metadata)
    {
        lock (_lock)
        {
            if (_disposed || _swapchainHandle == IntPtr.Zero) return false;
            return MoonshineNativeMethods.SwapchainSetHdrMetadata(_swapchainHandle, in metadata) == 0;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_swapchainHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.SwapchainDestroy(_swapchainHandle);
                _swapchainHandle = IntPtr.Zero;
            }
        }
    }
}
