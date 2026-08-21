using Moonshine.Interop;

namespace Moonshine.Host.Capture;

/// <summary>
/// Universal interface for high-performance desktop capture pipelines.
/// </summary>
public interface IDesktopCapturePipeline : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    uint Format { get; }
    bool IsHdr { get; }
    uint AdapterIndex { get; }
    uint OutputIndex { get; }
    bool IsAvailable { get; }
    CaptureMetrics Metrics { get; }

    bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame);
    void ReleaseFrame();
    bool TryRecover();
}
