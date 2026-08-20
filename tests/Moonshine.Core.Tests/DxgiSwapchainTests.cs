using FluentAssertions;
using Moonshine.Core.Video;
using Xunit;

namespace Moonshine.Core.Tests;

public class DxgiSwapchainTests
{
    [Fact]
    public void DxgiSwapchainPipeline_CreateAndPresent_IncrementsPresentedFrames()
    {
        using var pipeline = new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 1920, 1080, 2, false);

        pipeline.Width.Should().Be(1920);
        pipeline.Height.Should().Be(1080);
        pipeline.BufferCount.Should().Be(2);
        pipeline.IsHdr10.Should().BeFalse();

        bool presented = pipeline.Present();
        presented.Should().BeTrue();

        pipeline.Metrics.FramesPresented.Should().Be(1);
        pipeline.Metrics.PresentationErrors.Should().Be(0);
    }

    [Fact]
    public void DxgiSwapchainPipeline_ResizeAndToggleHdr_UpdatesState()
    {
        using var pipeline = new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 1920, 1080, 3, false);

        bool resized = pipeline.Resize(3840, 2160);
        resized.Should().BeTrue();
        pipeline.Width.Should().Be(3840);
        pipeline.Height.Should().Be(2160);

        bool hdrSet = pipeline.SetHdr(true);
        hdrSet.Should().BeTrue();
        pipeline.IsHdr10.Should().BeTrue();
    }

    [Fact]
    public void DxgiSwapchainPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 1280, 720, 2, false);
        pipeline.Dispose();
        pipeline.Dispose();
    }
}
