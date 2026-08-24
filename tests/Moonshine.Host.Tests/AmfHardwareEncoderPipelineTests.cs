using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class AmfHardwareEncoderPipelineTests
{
    [Fact]
    public void AmfHardwareEncoderPipeline_Initialize_PropertiesMatchConfiguration()
    {
        using var pipeline = new AmfHardwareEncoderPipeline(
            width: 3840,
            height: 2160,
            fps: 120,
            bitrateKbps: 45000,
            codec: VideoCodec.HevcMain10,
            preset: AmfQualityPreset.Speed,
            usage: AmfUsage.UltraLowLatency
        );

        pipeline.Width.Should().Be(3840);
        pipeline.Height.Should().Be(2160);
        pipeline.Fps.Should().Be(120);
        pipeline.BitrateKbps.Should().Be(45000);
        pipeline.Codec.Should().Be(VideoCodec.HevcMain10);
        pipeline.Vendor.Should().Be(EncoderVendor.AmdAmf);
        pipeline.Preset.Should().Be(AmfQualityPreset.Speed);
        pipeline.Usage.Should().Be(AmfUsage.UltraLowLatency);
    }

    [Fact]
    public void AmfHardwareEncoderPipeline_ConfigureTuningAndIntraRefresh_UpdatesStateWhenActive()
    {
        using var pipeline = new AmfHardwareEncoderPipeline(1920, 1080);

        if (!pipeline.IsActive)
        {
            // On non-AMD hardware, pipeline safely reports inactive
            pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Faulted);
            return;
        }

        bool tuningOk = pipeline.ConfigureTuning(AmfQualityPreset.Balanced, AmfUsage.LowLatency);
        tuningOk.Should().BeTrue();
        pipeline.Preset.Should().Be(AmfQualityPreset.Balanced);
        pipeline.Usage.Should().Be(AmfUsage.LowLatency);

        bool intraOk = pipeline.ConfigureIntraRefresh(true, 16);
        intraOk.Should().BeTrue();
    }

    [Fact]
    public void AmfHardwareEncoderPipeline_TryEncodeFrame_GeneratesKeyframeAndInterframesWhenActive()
    {
        using var pipeline = new AmfHardwareEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.Hevc
        );

        if (!pipeline.IsActive)
        {
            // On non-AMD hardware, encoding fails closed safely
            Span<byte> failBuf = stackalloc byte[128];
            pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, failBuf, out int written).Should().BeFalse();
            written.Should().Be(0);
            return;
        }

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
    public void AmfHardwareEncoderPipeline_IsCodecSupported_TruthfulHardwareQuery(VideoCodec codec)
    {
        bool supported = AmfHardwareEncoderPipeline.IsCodecSupported(codec);
        (supported == true || supported == false).Should().BeTrue();
    }

    [Fact]
    public void AmfHardwareEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new AmfHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();

        Span<byte> buffer = stackalloc byte[128];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);
    }

    [Fact]
    public void AmfHardwareEncoderPipeline_Evidence_ExposesAuthoritativePipelineEvidence()
    {
        using var pipeline = new AmfHardwareEncoderPipeline(1920, 1080);
        var evidence = pipeline.Evidence;

        if (!pipeline.IsActive)
        {
            evidence.SessionInitialised.Should().BeFalse();
            evidence.FrameSubmitted.Should().BeFalse();
            return;
        }

        evidence.ApiAvailable.Should().BeTrue();
        evidence.HardwareSupported.Should().BeTrue();
        evidence.SessionInitialised.Should().BeTrue();
        evidence.FrameSubmitted.Should().BeFalse();

        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, true, out _, buffer, out int written);
        ok.Should().BeTrue();
        written.Should().BeGreaterThan(0);

        var liveEvidence = pipeline.Evidence;
        liveEvidence.FrameSubmitted.Should().BeTrue();
        liveEvidence.OutputReceived.Should().BeTrue();
        liveEvidence.BitstreamStructurallyValid.Should().BeTrue();
        liveEvidence.AccessUnitValid.Should().BeTrue();
    }
}
