using FluentAssertions;
using Moonshine.Host.Encoding;
using Xunit;

namespace Moonshine.Host.Tests;

public class UnifiedHardwareEncoderEngineTests
{
    [Fact]
    public void UnifiedHardwareEncoderEngine_ReportsUnsupportedWithoutHardwareSdk()
    {
        UnifiedHardwareEncoderEngine.TryQueryCapabilities(EncoderVendor.Auto, out _).Should().BeFalse();
        using var engine = new UnifiedHardwareEncoderEngine(1920, 1080);
        engine.IsActive.Should().BeFalse();
        engine.TryEncodeFrame(IntPtr.Zero, false, out _, new byte[256], out int bytesWritten).Should().BeFalse();
        bytesWritten.Should().Be(0);
        engine.FramesEncoded.Should().Be(0);
    }
}
