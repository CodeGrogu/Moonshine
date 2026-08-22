using FluentAssertions;
using Moonshine.Core.Video;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Core.Tests;

public class GpuPresentationPipelineTests
{
    [Fact]
    public void DxgiSwapchainPipeline_InvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        var act1 = () => new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 0, 1080);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 1920, 0);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DxgiSwapchainPipeline_NullHwnd_ThrowsInvalidOperationException()
    {
        var act = () => new DxgiSwapchainPipeline(IntPtr.Zero, IntPtr.Zero, 1920, 1080);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MoonshineClientGpuPresenter_InvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        var act1 = () => new MoonshineClientGpuPresenter(IntPtr.Zero, IntPtr.Zero, 0, 1080);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new MoonshineClientGpuPresenter(IntPtr.Zero, IntPtr.Zero, 1920, 0);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MoonshineClientGpuPresenter_EnqueueAndProcessFrames_UpdatesMetricsAccurately()
    {
        using var presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080,
            targetRefreshRate: 120,
            isHdr10: false,
            queueCapacity: 16
        );

        presenter.Width.Should().Be(1920);
        presenter.Height.Should().Be(1080);
        presenter.IsHdr10.Should().BeFalse();

        // Enqueue 10 mock GPU decoded frames
        for (ulong i = 1; i <= 10; i++)
        {
            bool enqueued = presenter.EnqueueFrame(
                textureHandle: (IntPtr)0x1234,
                frameIndex: i,
                captureTimestampQpc: 1000000 + (long)i * 1000,
                isKeyframe: i == 1
            );
            enqueued.Should().BeTrue();
        }

        // Allow presentation thread loop to process frames with bounded wait
        for (int retry = 0; retry < 50; retry++)
        {
            var m = presenter.Metrics;
            if (m.FramesPresented + m.FramesDropped >= 10) break;
            Thread.Sleep(20);
        }

        var metrics = presenter.Metrics;
        metrics.FramesEnqueued.Should().Be(10);
        (metrics.FramesPresented + metrics.FramesDropped).Should().Be(10);
    }

    [Fact]
    public void MoonshineClientGpuPresenter_Resize_UpdatesDimensions()
    {
        using var presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080
        );

        bool resized = presenter.Resize(2560, 1440);
        resized.Should().BeTrue();
        presenter.Width.Should().Be(2560);
        presenter.Height.Should().Be(1440);
    }

    [Fact]
    public void MoonshineClientGpuPresenter_SetHdr_TogglesHdrState()
    {
        using var presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080,
            isHdr10: false
        );

        presenter.IsHdr10.Should().BeFalse();
        presenter.SetHdr(true).Should().BeTrue();
        presenter.IsHdr10.Should().BeTrue();
    }

    [Fact]
    public void MoonshineClientGpuPresenter_SetHdrMetadata_ConfiguresMetadata()
    {
        using var presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080,
            isHdr10: true
        );

        var meta = new MoonshineHdr10Metadata
        {
            HdrEnabled = 1,
            ColorSpace = 1,
            MaxMasteringLuminance = 10000000,
            MinMasteringLuminance = 1,
            MaxContentLightLevel = 1000,
            MaxFrameAverageLightLevel = 400
        };

        presenter.SetHdrMetadata(in meta).Should().BeTrue();
    }

    [Fact]
    public void MoonshineClientGpuPresenter_Disposal_ShutsDownCleanly()
    {
        var presenter = new MoonshineClientGpuPresenter(
            hwnd: IntPtr.Zero,
            d3d11Device: IntPtr.Zero,
            width: 1920,
            height: 1080
        );

        presenter.Dispose();

        // Enqueue after dispose must fail closed
        presenter.EnqueueFrame(IntPtr.Zero, 1, 0, false).Should().BeFalse();
    }
}
