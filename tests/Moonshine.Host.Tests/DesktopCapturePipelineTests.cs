using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class DesktopCapturePipelineTests
{
    [Fact]
    public void DxgiDesktopCapturePipeline_InitializeAndDispose_ExecutesCleanly()
    {
        using var pipeline = new DxgiDesktopCapturePipeline(0, 0);

        if (pipeline.IsAvailable)
        {
            pipeline.Width.Should().BeGreaterThan(0);
            pipeline.Height.Should().BeGreaterThan(0);

            pipeline.TryAcquireNextFrame(50, out var frame);
            pipeline.ReleaseFrame();

            var metrics = pipeline.Metrics;
            metrics.Width.Should().Be(pipeline.Width);
            metrics.Height.Should().Be(pipeline.Height);
        }
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new DxgiDesktopCapturePipeline(0, 0);
        pipeline.Dispose();
        pipeline.Dispose();

        pipeline.TryAcquireNextFrame(10, out _).Should().BeFalse();
    }
}
