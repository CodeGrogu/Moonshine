using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class DesktopCapturePipelineTests
{
    [Fact]
    public void DisplayManager_GetPhysicalAdapters_DiscoversAdapters()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeEmpty();

        foreach (var adapter in adapters)
        {
            adapter.Description.Should().NotBeNullOrWhiteSpace();
            adapter.DedicatedVideoMemoryBytes.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void DisplayManager_GetDisplays_DiscoversDisplays()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeEmpty();

        var primaryAdapter = adapters[0];
        var displays = DisplayManager.GetDisplays(primaryAdapter.AdapterIndex);

        if (displays.Count > 0)
        {
            var primary = DisplayManager.GetPrimaryDisplay(primaryAdapter.AdapterIndex);
            primary.Should().NotBeNull();
            primary!.Width.Should().BeGreaterThan(0);
            primary.Height.Should().BeGreaterThan(0);
            primary.RefreshRateHz.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void DisplayManager_GetPreferredHardwareAdapter_ReturnsValidAdapter()
    {
        var preferred = DisplayManager.GetPreferredHardwareAdapter();
        preferred.Should().NotBeNull();
        preferred!.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_InitializeAndDispose_ExecutesCleanly()
    {
        using var pipeline = new DxgiDesktopCapturePipeline(0, 0);

        if (pipeline.IsAvailable)
        {
            pipeline.Width.Should().BeGreaterThan(0);
            pipeline.Height.Should().BeGreaterThan(0);
            pipeline.Format.Should().BeGreaterThan(0);

            pipeline.TryAcquireNextFrame(50, out var frame);
            pipeline.ReleaseFrame();

            var metrics = pipeline.Metrics;
            metrics.Width.Should().Be(pipeline.Width);
            metrics.Height.Should().Be(pipeline.Height);
            metrics.Format.Should().Be(pipeline.Format);
        }
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_TryRecover_ExecutesCleanly()
    {
        using var pipeline = new DxgiDesktopCapturePipeline(0, 0);

        if (pipeline.IsAvailable)
        {
            bool recovered = pipeline.TryRecover();
            recovered.Should().BeTrue();
            pipeline.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_AcquireFrameHotPath_ZeroAllocations()
    {
        using var pipeline = new DxgiDesktopCapturePipeline(0, 0);

        if (pipeline.IsAvailable)
        {
            // Warmup
            pipeline.TryAcquireNextFrame(10, out _);
            pipeline.ReleaseFrame();

            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 50; i++)
            {
                pipeline.TryAcquireNextFrame(5, out _);
                pipeline.ReleaseFrame();
            }

            long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
            long totalAllocated = bytesAfter - bytesBefore;

            // Strict zero managed allocations discipline on the frame acquisition hot path
            totalAllocated.Should().Be(0);
        }
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new DxgiDesktopCapturePipeline(0, 0);
        pipeline.Dispose();
        pipeline.Dispose();

        pipeline.TryAcquireNextFrame(10, out _).Should().BeFalse();
        pipeline.TryRecover().Should().BeFalse();
    }
}
