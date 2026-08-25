using FluentAssertions;
using Moonshine.Core.Video;
using Moonshine.Interop;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Moonshine.Core.Tests;

public class HardwareVideoDecoderPipelineTests
{
    [Fact]
    public void HardwareVideoDecoderPipeline_QueryCapabilities_ReturnsValidCaps()
    {
        var caps = HardwareVideoDecoderPipeline.QueryCapabilities();
        (caps.SupportsH264 == 0 || caps.SupportsH264 == 1).Should().BeTrue();
        (caps.SupportsHevc == 0 || caps.SupportsHevc == 1).Should().BeTrue();
        (caps.SupportsAv1 == 0 || caps.SupportsAv1 == 1).Should().BeTrue();
    }

    [Theory]
    [InlineData(0u, 1080u)]
    [InlineData(1920u, 0u)]
    public void MoonshineVideoPipeline_InvalidDimensions_ThrowsArgumentException(uint width, uint height)
    {
        var act = () => new MoonshineVideoPipeline(IntPtr.Zero, width, height);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HardwareVideoDecoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new HardwareVideoDecoderPipeline(IntPtr.Zero, 1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
    }

    [Fact]
    public void HardwareVideoDecoderPipeline_InactiveOperations_ReturnFailureGracefully()
    {
        using var pipeline = new HardwareVideoDecoderPipeline(IntPtr.Zero, 1920, 1080);
        if (!pipeline.IsActive)
        {
            MoonshineFrameDesc frame = default;
            pipeline.TrySubmitFrame(in frame).Should().BeFalse();
            pipeline.GetDecodedSurface().Should().Be(IntPtr.Zero);
            pipeline.Reconfigure(1280, 720).Should().BeFalse();
        }
    }

    [Fact]
    public unsafe void MoonshineVideoPipeline_SubmitFrame_ZeroAllocationsHotPath()
    {
        var caps = MoonshineVideoPipeline.QueryCaps();
        if (caps.SupportsHevc == 0 && caps.SupportsH264 == 0)
        {
            // Headless environment without hardware GPU
            return;
        }

        try
        {
            uint codec = caps.SupportsHevc != 0 ? 1u : 0u;
            using var pipeline = new MoonshineVideoPipeline(IntPtr.Zero, 1920, 1080, codec);

            byte[] mockData = [0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01];
            fixed (byte* ptr = mockData)
            {
                MoonshineFrameDesc frame = new()
                {
                    FrameIndex = 1,
                    TotalBytes = (uint)mockData.Length,
                    PacketCount = 1,
                    IsKeyframe = 1,
                    FrameBuffer = ptr
                };

                // Warm up
                pipeline.SubmitFrame(in frame);

                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 50; i++)
                {
                    pipeline.SubmitFrame(in frame);
                }
                long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

                (afterAlloc - beforeAlloc).Should().Be(0, "Hardware video decoder hot path must have zero GC allocations");
            }
        }
        catch (InvalidOperationException)
        {
            // Hardware device not available on this specific test runner
        }
    }
}
