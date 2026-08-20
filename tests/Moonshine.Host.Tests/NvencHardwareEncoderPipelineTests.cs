using FluentAssertions;
using Moonshine.Host.Encoding;
using Xunit;

namespace Moonshine.Host.Tests;

public class NvencHardwareEncoderPipelineTests
{
    [Fact]
    public void NvencHardwareEncoderPipeline_Initialize_PresetPropertiesMatch()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(
            width: 3840,
            height: 2160,
            fps: 120,
            bitrateKbps: 50000,
            codec: VideoCodec.HevcMain10,
            preset: NvencPreset.P1_UltraFast,
            tuning: NvencTuning.UltraLowLatency
        );

        pipeline.Width.Should().Be(3840);
        pipeline.Height.Should().Be(2160);
        pipeline.Fps.Should().Be(120);
        pipeline.BitrateKbps.Should().Be(50000);
        pipeline.Codec.Should().Be(VideoCodec.HevcMain10);
        pipeline.Vendor.Should().Be(EncoderVendor.NvidiaNvenc);
        pipeline.Preset.Should().Be(NvencPreset.P1_UltraFast);
        pipeline.Tuning.Should().Be(NvencTuning.UltraLowLatency);
        pipeline.IsActive.Should().BeTrue();
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_ConfigureTuningAndIntraRefresh_UpdatesState()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);

        bool tuningOk = pipeline.ConfigureTuning(NvencPreset.P2_Fast, NvencTuning.LowLatency);
        tuningOk.Should().BeTrue();
        pipeline.Preset.Should().Be(NvencPreset.P2_Fast);
        pipeline.Tuning.Should().Be(NvencTuning.LowLatency);

        bool intraOk = pipeline.ConfigureIntraRefresh(true, 60, 4);
        intraOk.Should().BeTrue();
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_TryEncodeFrame_GeneratesKeyframeAndInterframes()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(
            width: 2560,
            height: 1440,
            fps: 240,
            bitrateKbps: 40000,
            codec: VideoCodec.Av1
        );

        Span<byte> buffer = stackalloc byte[1024 * 512];

        // Frame 0: Keyframe
        bool ok1 = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc1, buffer, out int written1);
        ok1.Should().BeTrue();
        desc1.FrameIndex.Should().Be(0);
        desc1.IsKeyframe.Should().Be(1);
        desc1.IsHeaderPacket.Should().Be(1);
        written1.Should().BeGreaterThan(0);

        // Frame 1: Inter-frame
        bool ok2 = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc2, buffer, out int written2);
        ok2.Should().BeTrue();
        desc2.FrameIndex.Should().Be(1);
        desc2.IsKeyframe.Should().Be(0);
        written2.Should().BeGreaterThan(0);

        // Request Keyframe
        pipeline.RequestKeyframe();
        bool ok3 = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc3, buffer, out int written3);
        ok3.Should().BeTrue();
        desc3.FrameIndex.Should().Be(2);
        desc3.IsKeyframe.Should().Be(1);
        written3.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    [InlineData(VideoCodec.HevcMain10)]
    [InlineData(VideoCodec.Av1)]
    public void NvencHardwareEncoderPipeline_IsCodecSupported_ReturnsTrueForAll(VideoCodec codec)
    {
        NvencHardwareEncoderPipeline.IsCodecSupported(codec).Should().BeTrue();
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();

        Span<byte> buffer = stackalloc byte[128];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }
}
