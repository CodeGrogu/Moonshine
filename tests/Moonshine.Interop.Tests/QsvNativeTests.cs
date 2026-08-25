using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class QsvNativeTests
{
    [Theory]
    [InlineData(0u)] // H.264
    [InlineData(1u)] // HEVC
    [InlineData(2u)] // HEVC Main10 (10-bit HDR)
    [InlineData(3u)] // AV1
    public void QsvQueryCodecSupport_AllCodecs_ReturnsSupported(uint codec)
    {
        int res = MoonshineNativeMethods.QsvQueryCodecSupport(codec, out uint supported);
        res.Should().Be(1);
        supported.Should().Be(1);
    }

    [Fact]
    public void QsvSetTuningAndIntraRefresh_ValidHandle_ReturnsSuccess()
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

        IntPtr handle = MoonshineNativeMethods.EncoderCreate(3, IntPtr.Zero, in config); // Vendor 3 = QSV
        handle.Should().NotBe(IntPtr.Zero);

        int tuningRes = MoonshineNativeMethods.QsvSetTuning(handle, 1, 1); // BestSpeed, LowPower
        tuningRes.Should().Be(1);

        int intraRes = MoonshineNativeMethods.QsvSetIntraRefresh(handle, 1, 30, -2);
        intraRes.Should().Be(1);

        MoonshineNativeMethods.EncoderDestroy(handle);
    }

    [Fact]
    public void QsvSetTuningAndIntraRefresh_NullHandle_FailClosed()
    {
        int tuningRes = MoonshineNativeMethods.QsvSetTuning(IntPtr.Zero, 1, 1);
        tuningRes.Should().Be(0, "Null handle must fail closed");

        int intraRes = MoonshineNativeMethods.QsvSetIntraRefresh(IntPtr.Zero, 1, 30, -2);
        intraRes.Should().Be(0, "Null handle must fail closed");
    }

    [Fact]
    public void QsvEncoder_HighBitrateReconfigure_HandlesMultiplierSuccessfully()
    {
        var config = new MoonshineEncoderConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            PeakBitrateKbps = 30000,
            Codec = 1, // HEVC
            RcMode = 0,
            GopLength = 0,
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        IntPtr handle = MoonshineNativeMethods.EncoderCreate(3, IntPtr.Zero, in config);
        handle.Should().NotBe(IntPtr.Zero);

        // Reconfigure to 80 Mbps (which requires BRCParamMultiplier = 2)
        var highBitrateConfig = config;
        highBitrateConfig.BitrateKbps = 80000;
        highBitrateConfig.PeakBitrateKbps = 100000;

        int reconfRes = MoonshineNativeMethods.EncoderReconfigure(handle, in highBitrateConfig);
        reconfRes.Should().Be(1, "High bitrate reconfiguration with multiplier should succeed");

        MoonshineNativeMethods.EncoderDestroy(handle);
    }
}
