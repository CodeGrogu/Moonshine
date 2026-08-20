using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class WgcCapturePipelineTests
{
    [Fact]
    public void WgcDesktopCapturePipeline_InitializeAndDispose_ExecutesCleanly()
    {
        using var pipeline = new WgcDesktopCapturePipeline(IntPtr.Zero, targetFps: 120);

        if (pipeline.IsAvailable)
        {
            pipeline.Width.Should().BeGreaterThan(0);
            pipeline.Height.Should().BeGreaterThan(0);
            pipeline.TargetFps.Should().Be(120);

            pipeline.TryAcquireNextFrame(50, out _);
            pipeline.ReleaseFrame();

            var metrics = pipeline.Metrics;
            metrics.Width.Should().Be(pipeline.Width);
            metrics.Height.Should().Be(pipeline.Height);
        }
    }

    [Fact]
    public void WgcDesktopCapturePipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new WgcDesktopCapturePipeline(IntPtr.Zero, targetFps: 60);
        pipeline.Dispose();
        pipeline.Dispose();

        pipeline.TryAcquireNextFrame(10, out _).Should().BeFalse();
    }
}
