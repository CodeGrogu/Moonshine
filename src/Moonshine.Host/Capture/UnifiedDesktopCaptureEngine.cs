using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public enum CaptureBackend
{
    Automatic = 0,
    DxgiDesktopDuplication = 1,
    WindowsGraphicsCapture = 2
}

/// <summary>
/// Unified Desktop Capture Engine coordinating DirectX and WinRT capture pipelines.
/// </summary>
public sealed class UnifiedDesktopCaptureEngine : IDesktopCapturePipeline
{
    private IDesktopCapturePipeline _activePipeline;
    private CaptureBackend _backendType;
    private readonly uint _targetFps;
    private uint _adapterIndex;
    private uint _outputIndex;
    private IntPtr _hmonitor;
    private CaptureSourceDescriptor? _source;
    private readonly Lock _lock = new();

    public CaptureBackend ActiveBackend => _backendType;
    public uint Width => _activePipeline.Width;
    public uint Height => _activePipeline.Height;
    public uint Format => _activePipeline.Format;
    public bool IsHdr => _activePipeline.IsHdr;
    public uint AdapterIndex => _activePipeline.AdapterIndex;
    public uint OutputIndex => _activePipeline.OutputIndex;
    public CaptureSourceDescriptor? Source => _activePipeline.Source ?? _source;
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
        _targetFps = targetFps > 0 ? targetFps : 60;
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

    public UnifiedDesktopCaptureEngine(
        CaptureSourceDescriptor source,
        CaptureBackend preferredBackend = CaptureBackend.Automatic,
        uint targetFps = 60
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _targetFps = targetFps > 0 ? targetFps : (uint)Math.Max(1, (int)Math.Round(source.RefreshRateHz));
        _adapterIndex = source.AdapterIndex;
        _outputIndex = source.OutputIndex;
        _hmonitor = source.MonitorHandle;

        if (preferredBackend == CaptureBackend.WindowsGraphicsCapture)
        {
            _activePipeline = new WgcDesktopCapturePipeline(source, _targetFps);
            _backendType = CaptureBackend.WindowsGraphicsCapture;
        }
        else if (preferredBackend == CaptureBackend.DxgiDesktopDuplication)
        {
            _activePipeline = new DxgiDesktopCapturePipeline(source);
            _backendType = CaptureBackend.DxgiDesktopDuplication;
        }
        else // Automatic
        {
            var dxgi = new DxgiDesktopCapturePipeline(source);
            if (dxgi.IsAvailable)
            {
                _activePipeline = dxgi;
                _backendType = CaptureBackend.DxgiDesktopDuplication;
            }
            else
            {
                dxgi.Dispose();
                _activePipeline = new WgcDesktopCapturePipeline(source, _targetFps);
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
        lock (_lock)
        {
            if (_activePipeline.TryRecover())
            {
                return true;
            }

            // Automatic failover between backends if recovery on current pipeline fails
            if (_backendType == CaptureBackend.DxgiDesktopDuplication)
            {
                _activePipeline.Dispose();
                _activePipeline = _source != null
                    ? new WgcDesktopCapturePipeline(_source, _targetFps)
                    : new WgcDesktopCapturePipeline(_hmonitor, _targetFps);
                _backendType = CaptureBackend.WindowsGraphicsCapture;
                return _activePipeline.IsAvailable;
            }
            else
            {
                _activePipeline.Dispose();
                _activePipeline = _source != null
                    ? new DxgiDesktopCapturePipeline(_source)
                    : new DxgiDesktopCapturePipeline(_adapterIndex, _outputIndex);
                _backendType = CaptureBackend.DxgiDesktopDuplication;
                return _activePipeline.IsAvailable;
            }
        }
    }

    /// <summary>
    /// Dynamically reconfigures the capture engine to stream from a new capture source.
    /// </summary>
    public bool TryReconfigureSource(CaptureSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_lock)
        {
            _source = source;
            _adapterIndex = source.AdapterIndex;
            _outputIndex = source.OutputIndex;
            _hmonitor = source.MonitorHandle;

            if (_activePipeline.TryReconfigureSource(source))
            {
                return true;
            }

            // Failover to alternate backend if the active pipeline fails to reconfigure
            if (_backendType == CaptureBackend.DxgiDesktopDuplication)
            {
                _activePipeline.Dispose();
                _activePipeline = new WgcDesktopCapturePipeline(source, _targetFps);
                _backendType = CaptureBackend.WindowsGraphicsCapture;
                return _activePipeline.IsAvailable;
            }
            else
            {
                _activePipeline.Dispose();
                _activePipeline = new DxgiDesktopCapturePipeline(source);
                _backendType = CaptureBackend.DxgiDesktopDuplication;
                return _activePipeline.IsAvailable;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _activePipeline.Dispose();
        }
    }
}
