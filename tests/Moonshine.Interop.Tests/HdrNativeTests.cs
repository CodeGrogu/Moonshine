using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class HdrNativeTests
{
    [Fact]
    public void MoonshineHdr10Metadata_HasExactExpectedSize()
    {
        Marshal.SizeOf<MoonshineHdr10Metadata>().Should().Be(32);
    }

    [Fact]
    public void HdrParseCapabilities_DxgiHdr_ReturnsValidMetadata()
    {
        int res = MoonshineNativeMethods.HdrParseCapabilities(12, out var meta);
        res.Should().Be(1);
        meta.HdrEnabled.Should().Be(1);
        meta.ColorSpace.Should().Be(1);
        meta.MaxMasteringLuminance.Should().Be(10000000);
    }

    [Fact]
    public void ColorConverter_CreateAndDestroy_ExecutesCleanly()
    {
        IntPtr handle = MoonshineNativeMethods.ColorConverterCreate(IntPtr.Zero, 1920, 1080, 24, 104);
        if (handle != IntPtr.Zero)
        {
            MoonshineNativeMethods.ColorConverterDestroy(handle);
        }
    }
}
