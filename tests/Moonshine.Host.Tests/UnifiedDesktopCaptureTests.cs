using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class UnifiedDesktopCaptureTests
{
    [Fact]
    public void UnifiedDesktopCaptureEngine_Automatic_SelectsBackendCleanly()
    {
        using var engine = new UnifiedDesktopCaptureEngine(CaptureBackend.Automatic, targetFps: 60);

        if (engine.IsAvailable)
        {
            engine.Width.Should().BeGreaterThan(0);
            engine.Height.Should().BeGreaterThan(0);

            engine.TryAcquireNextFrame(50, out _);
            engine.ReleaseFrame();

            engine.Metrics.Width.Should().Be(engine.Width);
        }
    }

    [Fact]
    public void UnifiedDesktopCaptureEngine_WgcExplicit_InitializesWgc()
    {
        using var engine = new UnifiedDesktopCaptureEngine(CaptureBackend.WindowsGraphicsCapture, targetFps: 144);
        engine.ActiveBackend.Should().Be(CaptureBackend.WindowsGraphicsCapture);
    }

    [Fact]
    public void UnifiedDesktopCaptureEngine_DxgiExplicit_InitializesDxgi()
    {
        using var engine = new UnifiedDesktopCaptureEngine(CaptureBackend.DxgiDesktopDuplication, targetFps: 60);
        engine.ActiveBackend.Should().Be(CaptureBackend.DxgiDesktopDuplication);
    }
}
