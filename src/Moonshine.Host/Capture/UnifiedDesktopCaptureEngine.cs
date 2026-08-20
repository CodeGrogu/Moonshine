using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public enum CaptureBackend
{
    Automatic,
    DxgiDesktopDuplication,
    WindowsGraphicsCapture
}

/// <summary>
/// Unified Desktop Capture Engine coordinating DirectX and WinRT capture pipelines.
/// </summary>
public sealed class UnifiedDesktopCaptureEngine : IDesktopCapturePipeline
{
    private readonly IDesktopCapturePipeline _activePipeline;
    private readonly CaptureBackend _backendType;

    public CaptureBackend ActiveBackend => _backendType;
    public uint Width => _activePipeline.Width;
    public uint Height => _activePipeline.Height;
    public bool IsAvailable => _activePipeline.IsAvailable;
    public CaptureMetrics Metrics => _activePipeline.Metrics;

    public UnifiedDesktopCaptureEngine(
        CaptureBackend preferredBackend = CaptureBackend.Automatic,
        uint targetFps = 60,
        uint adapterIndex = 0,
        uint outputIndex = 0,
        IntPtr hmonitor = 0
    )
    {
        if (preferredBackend == CaptureBackend.WindowsGraphicsCapture)
        {
            _activePipeline = new WgcDesktopCapturePipeline(hmonitor, targetFps);
            _backendType = CaptureBackend.WindowsGraphicsCapture;
        }
        else if (preferredBackend == CaptureBackend.DxgiDesktopDuplication)
        {
            _activePipeline = new DxgiDesktopCapturePipeline(adapterIndex, outputIndex);
            _backendType = CaptureBackend.DxgiDesktopDuplication;
        }
        else // Automatic
        {
            var dxgi = new DxgiDesktopCapturePipeline(adapterIndex, outputIndex);
            if (dxgi.IsAvailable)
            {
                _activePipeline = dxgi;
                _backendType = CaptureBackend.DxgiDesktopDuplication;
            }
            else
            {
                dxgi.Dispose();
                _activePipeline = new WgcDesktopCapturePipeline(hmonitor, targetFps);
                _backendType = CaptureBackend.WindowsGraphicsCapture;
            }
        }
    }

    public bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
    {
        return _activePipeline.TryAcquireNextFrame(timeoutMs, out frame);
    }

    public void ReleaseFrame()
    {
        _activePipeline.ReleaseFrame();
    }

    public void Dispose()
    {
        _activePipeline.Dispose();
    }
}
