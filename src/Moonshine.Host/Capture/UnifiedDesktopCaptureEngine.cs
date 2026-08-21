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
    private IDesktopCapturePipeline _activePipeline;
    private CaptureBackend _backendType;
    private readonly uint _targetFps;
    private readonly uint _adapterIndex;
    private readonly uint _outputIndex;
    private readonly IntPtr _hmonitor;

    public CaptureBackend ActiveBackend => _backendType;
    public uint Width => _activePipeline.Width;
    public uint Height => _activePipeline.Height;
    public uint Format => _activePipeline.Format;
    public bool IsHdr => _activePipeline.IsHdr;
    public uint AdapterIndex => _activePipeline.AdapterIndex;
    public uint OutputIndex => _activePipeline.OutputIndex;
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
        _targetFps = targetFps;
        _adapterIndex = adapterIndex;
        _outputIndex = outputIndex;
        _hmonitor = hmonitor;

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

    public bool TryRecover()
    {
        if (_activePipeline.TryRecover())
        {
            return true;
        }

        // Automatic failover between backends if recovery on current pipeline fails
        if (_backendType == CaptureBackend.DxgiDesktopDuplication)
        {
            _activePipeline.Dispose();
            _activePipeline = new WgcDesktopCapturePipeline(_hmonitor, _targetFps);
            _backendType = CaptureBackend.WindowsGraphicsCapture;
            return _activePipeline.IsAvailable;
        }
        else
        {
            _activePipeline.Dispose();
            _activePipeline = new DxgiDesktopCapturePipeline(_adapterIndex, _outputIndex);
            _backendType = CaptureBackend.DxgiDesktopDuplication;
            return _activePipeline.IsAvailable;
        }
    }

    public void Dispose()
    {
        _activePipeline.Dispose();
    }
}
