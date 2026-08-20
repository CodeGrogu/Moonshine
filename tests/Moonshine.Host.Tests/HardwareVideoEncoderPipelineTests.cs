using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class HardwareVideoEncoderPipelineTests
{
    [Fact]
    public void HardwareVideoEncoderPipeline_InitializeAndProperties_MatchesExpected()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 120,
            bitrateKbps: 30000,
            codec: VideoCodec.Av1,
            vendor: EncoderVendor.Auto
        );

        pipeline.Width.Should().Be(1920);
        pipeline.Height.Should().Be(1080);
        pipeline.Fps.Should().Be(120);
        pipeline.BitrateKbps.Should().Be(30000);
        pipeline.Codec.Should().Be(VideoCodec.Av1);
        pipeline.IsActive.Should().BeTrue();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_EncodeAndReconfigure_OperatesCleanly()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 3840,
            height: 2160,
            fps: 60,
            bitrateKbps: 50000,
            codec: VideoCodec.HevcMain10
        );

        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool success = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc, buffer, out int written);
        success.Should().BeTrue();
        desc.IsKeyframe.Should().Be(1);
        written.Should().BeGreaterThan(0);

        bool reconf = pipeline.Reconfigure(60000, 120);
        reconf.Should().BeTrue();
        pipeline.BitrateKbps.Should().Be(60000);
        pipeline.Fps.Should().Be(120);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new HardwareVideoEncoderPipeline(1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();

        Span<byte> buffer = stackalloc byte[256];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
    }
}
