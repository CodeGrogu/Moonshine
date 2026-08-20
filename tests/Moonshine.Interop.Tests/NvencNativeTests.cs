using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class NvencNativeTests
{
    [Theory]
    [InlineData(0u)] // H.264
    [InlineData(1u)] // HEVC
    [InlineData(2u)] // HEVC Main10 (10-bit HDR)
    [InlineData(3u)] // AV1
    public void NvencQueryCodecSupport_AllCodecs_ReturnsSupported(uint codec)
    {
        int res = MoonshineNativeMethods.NvencQueryCodecSupport(codec, out uint supported);
        res.Should().Be(1);
        supported.Should().Be(1);
    }

    [Fact]
    public void NvencSetTuningAndIntraRefresh_ValidHandle_ReturnsSuccess()
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

        IntPtr handle = MoonshineNativeMethods.EncoderCreate(1, IntPtr.Zero, in config); // Vendor 1 = NVENC
        handle.Should().NotBe(IntPtr.Zero);

        int tuningRes = MoonshineNativeMethods.NvencSetTuning(handle, 1, 2); // P1, UltraLowLatency
        tuningRes.Should().Be(1);

        int intraRes = MoonshineNativeMethods.NvencSetIntraRefresh(handle, 1, 60, 4);
        intraRes.Should().Be(1);

        MoonshineNativeMethods.EncoderDestroy(handle);
    }
}
