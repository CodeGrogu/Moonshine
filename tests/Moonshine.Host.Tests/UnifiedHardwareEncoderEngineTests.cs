using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
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

    [Theory]
    [InlineData(EncoderVendor.NvidiaNvenc)]
    [InlineData(EncoderVendor.AmdAmf)]
    [InlineData(EncoderVendor.IntelQuickSync)]
    [InlineData(EncoderVendor.Direct3D11Hardware)]
    public void UnifiedHardwareEncoderEngine_VendorQueryWithoutDevice_FailsClosed(EncoderVendor vendor)
    {
        UnifiedHardwareEncoderEngine.TryQueryCapabilities(vendor, out var caps, IntPtr.Zero).Should().BeFalse();
        caps.SupportedCodecsMask.Should().Be(0);
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_ReconfigureInactive_ReturnsFalse()
    {
        using var engine = new UnifiedHardwareEncoderEngine(1920, 1080, 60, 20000);
        engine.ReconfigureBitrate(15000, 120).Should().BeFalse();
        engine.RequestKeyframe();
        engine.EncodingErrors.Should().Be(0);
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_DoubleDispose_IsSafe()
    {
        var engine = new UnifiedHardwareEncoderEngine(1920, 1080);
        engine.Dispose();
        engine.Dispose();
        engine.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_TryEncodeFrame_ZeroAllocationsHotPath()
    {
        using var engine = new UnifiedHardwareEncoderEngine(1920, 1080);
        byte[] buffer = new byte[1024];

        // Warm up
        engine.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            engine.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);
        }
        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

        (afterAlloc - beforeAlloc).Should().Be(0, "Hardware video encoder hot path must have zero GC allocations");
    }
}
