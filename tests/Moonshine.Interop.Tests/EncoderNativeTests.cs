using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class EncoderNativeTests
{
    [Fact]
    public void MoonshineEncoderStructs_HaveExactBlittableSizes()
    {
        Marshal.SizeOf<MoonshineEncoderCaps>().Should().Be(32);
        Marshal.SizeOf<MoonshineEncoderConfig>().Should().Be(32);
        Marshal.SizeOf<MoonshineEncodedPacketDesc>().Should().Be(24);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    public void EncoderQueryCaps_AllUnimplementedVendors_ReturnsUnsupported(uint vendor) => MoonshineNativeMethods.EncoderQueryCaps(vendor, IntPtr.Zero, out _).Should().Be(0);

    [Fact]
    public void EncoderCreate_ReturnsNullWithoutARealHardwareBackend()
    {
        var config = new MoonshineEncoderConfig { Width = 1920, Height = 1080, Fps = 60, BitrateKbps = 20000 };
        MoonshineNativeMethods.EncoderCreate(0, IntPtr.Zero, in config).Should().Be(IntPtr.Zero);
    }
}
