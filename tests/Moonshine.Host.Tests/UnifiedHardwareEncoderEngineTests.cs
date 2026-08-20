using FluentAssertions;
using Moonshine.Host.Encoding;
using Xunit;

namespace Moonshine.Host.Tests;

public class UnifiedHardwareEncoderEngineTests
{
    [Fact]
    public void UnifiedHardwareEncoderEngine_TryQueryCapabilities_ReturnsValidCaps()
    {
        bool success = UnifiedHardwareEncoderEngine.TryQueryCapabilities(EncoderVendor.Auto, out var caps);
        success.Should().BeTrue();
        caps.MaxWidth.Should().BeGreaterThanOrEqualTo(3840);
        caps.MaxFps.Should().BeGreaterThanOrEqualTo(120);
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_EncodeSequence_UpdatesTelemetry()
    {
        using var engine = new UnifiedHardwareEncoderEngine(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.HevcMain10
        );

        engine.IsActive.Should().BeTrue();
        engine.FramesEncoded.Should().Be(0);
        engine.KeyframesEmitted.Should().Be(0);

        byte[] buffer = new byte[1024 * 256];

        // Frame 0: Keyframe
        bool res1 = engine.TryEncodeFrame(IntPtr.Zero, false, out var desc1, buffer, out int written1);
        res1.Should().BeTrue();
        desc1.IsKeyframe.Should().Be(1);
        engine.FramesEncoded.Should().Be(1);
        engine.KeyframesEmitted.Should().Be(1);
        engine.BytesEmitted.Should().Be(written1);

        // Frame 1: Inter-frame
        bool res2 = engine.TryEncodeFrame(IntPtr.Zero, false, out var desc2, buffer, out int written2);
        res2.Should().BeTrue();
        desc2.IsKeyframe.Should().Be(0);
        engine.FramesEncoded.Should().Be(2);
        engine.KeyframesEmitted.Should().Be(1);
        engine.BytesEmitted.Should().Be(written1 + written2);

        // Force Keyframe via RequestKeyframe
        engine.RequestKeyframe();
        bool res3 = engine.TryEncodeFrame(IntPtr.Zero, false, out var desc3, buffer, out int written3);
        res3.Should().BeTrue();
        desc3.IsKeyframe.Should().Be(1);
        engine.FramesEncoded.Should().Be(3);
        engine.KeyframesEmitted.Should().Be(2);
        engine.BytesEmitted.Should().Be(written1 + written2 + written3);

        // Dynamic Bitrate Reconfiguration
        bool reconf = engine.ReconfigureBitrate(35000, 120);
        reconf.Should().BeTrue();
        engine.BitrateKbps.Should().Be(35000);
        engine.Fps.Should().Be(120);
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_DoubleDispose_IsSafe()
    {
        var engine = new UnifiedHardwareEncoderEngine(1920, 1080);
        engine.Dispose();
        engine.Dispose();

        byte[] buffer = new byte[256];
        engine.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        engine.IsActive.Should().BeFalse();
    }
}
