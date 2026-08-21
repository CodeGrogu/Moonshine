using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class AmfNativeTests
{
    [Theory]
    [InlineData(0u)] // H.264
    [InlineData(1u)] // HEVC
    [InlineData(2u)] // HEVC Main10 (10-bit HDR)
    [InlineData(3u)] // AV1
    public void AmfQueryCodecSupport_AllCodecs_ReturnsSupported(uint codec)
    {
        int res = MoonshineNativeMethods.AmfQueryCodecSupport(codec, out uint supported);
        res.Should().Be(1);
        supported.Should().Be(1);
    }

    [Fact]
    public void AmfSetTuningAndIntraRefresh_ValidHandle_ReturnsSuccess()
    {
        var config = new MoonshineEncoderConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            PeakBitrateKbps = 30000,
            Codec = 2, // HEVC Main10
            RcMode = 0,
            GopLength = 0,
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        IntPtr handle = MoonshineNativeMethods.EncoderCreate(2, IntPtr.Zero, in config); // Vendor 2 = AMF
        handle.Should().NotBe(IntPtr.Zero);

        int tuningRes = MoonshineNativeMethods.AmfSetTuning(handle, 1, 1); // Speed, UltraLowLatency
        tuningRes.Should().Be(1);

        int intraRes = MoonshineNativeMethods.AmfSetIntraRefresh(handle, 1, 16);
        intraRes.Should().Be(1);

        MoonshineNativeMethods.EncoderDestroy(handle);
    }
}
