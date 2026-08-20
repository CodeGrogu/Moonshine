using FluentAssertions;
using Moonshine.Core.Video;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Core.Tests;

public class VideoPipelineTests
{
    [Fact]
    public void MoonshineVideoPipeline_QueryCaps_ReturnsValidHardwareCaps()
    {
        var caps = MoonshineVideoPipeline.QueryCaps();

        caps.MaxWidth.Should().BeGreaterThanOrEqualTo(1920);
        caps.MaxHeight.Should().BeGreaterThanOrEqualTo(1080);
        caps.SupportsHevc.Should().Be(1);
    }

    [Fact]
    public unsafe void MoonshineVideoPipeline_D3D11_InitializesAndSubmitsFrame()
    {
        using var pipeline = new MoonshineVideoPipeline(IntPtr.Zero, 1920, 1080, 1, HardwareDecoderApi.Direct3D11);

        byte[] frameBytes = new byte[2048];
        fixed (byte* ptr = frameBytes)
        {
            var frame = new MoonshineFrameDesc
            {
                FrameIndex = 1,
                TotalBytes = (uint)frameBytes.Length,
                PacketCount = 2,
                IsKeyframe = 1,
                FrameBuffer = ptr
            };

            bool success = pipeline.SubmitFrame(in frame);
            success.Should().BeTrue();
        }

        pipeline.Metrics.FramesSubmitted.Should().Be(1);
        pipeline.Metrics.DecodeErrors.Should().Be(0);
    }

    [Fact]
    public unsafe void MoonshineVideoPipeline_D3D12_InitializesAndSubmitsFrame()
    {
        using var pipeline = new MoonshineVideoPipeline(IntPtr.Zero, 3840, 2160, 2, HardwareDecoderApi.Direct3D12);

        byte[] frameBytes = new byte[4096];
        fixed (byte* ptr = frameBytes)
        {
            var frame = new MoonshineFrameDesc
            {
                FrameIndex = 10,
                TotalBytes = (uint)frameBytes.Length,
                PacketCount = 4,
                IsKeyframe = 0,
                FrameBuffer = ptr
            };

            bool success = pipeline.SubmitFrame(in frame);
            success.Should().BeTrue();
        }

        pipeline.Metrics.FramesSubmitted.Should().Be(1);
        pipeline.Metrics.DecodeErrors.Should().Be(0);
    }

    [Fact]
    public void MoonshineVideoPipeline_SubmitFrame_InvalidBuffer_FailsAndIncrementsErrorCount()
    {
        using var pipeline = new MoonshineVideoPipeline(IntPtr.Zero, 1920, 1080, 0);

        var invalidFrame = default(MoonshineFrameDesc);
        bool success = pipeline.SubmitFrame(in invalidFrame);

        success.Should().BeFalse();
        pipeline.Metrics.DecodeErrors.Should().Be(1);
    }

    [Fact]
    public void MoonshineVideoPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new MoonshineVideoPipeline(IntPtr.Zero, 1280, 720, 0);
        pipeline.Dispose();
        pipeline.Dispose();
    }
}
