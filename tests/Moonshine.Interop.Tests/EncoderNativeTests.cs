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

    [Theory]
    [InlineData(0u)] // H.264
    [InlineData(1u)] // HEVC
    [InlineData(2u)] // HEVC Main10 (10-bit HDR)
    [InlineData(3u)] // AV1
    public void QsvQueryCodecSupport_ValidCodec_ExecutesDefensively(uint codec)
    {
        int res = MoonshineNativeMethods.QsvQueryCodecSupport(codec, out uint supported);
        res.Should().Be(1);
        (supported == 0 || supported == 1).Should().BeTrue();
    }

    [Fact]
    public void QsvQueryCodecSupport_InvalidCodec_ReturnsUnsupported()
    {
        int res = MoonshineNativeMethods.QsvQueryCodecSupport(999u, out uint supported);
        res.Should().Be(1);
        supported.Should().Be(0);
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
    public void D3D11TextureHelpers_NullDevice_FailClosedSafely()
    {
        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(IntPtr.Zero, 1920, 1080, 0);
        tex.Should().Be(IntPtr.Zero, "Null D3D11 device must fail closed");

        // Destroying null texture must be safe and idempotent
        MoonshineNativeMethods.D3D11DestroyTexture(IntPtr.Zero);
    }

    [Fact]
    public void D3D11SharedTexture_NullDevice_FailClosedSafely()
    {
        IntPtr tex = MoonshineNativeMethods.D3D11CreateSharedTexture(IntPtr.Zero, 1920, 1080, 0, 0, out IntPtr sharedHandle);
        tex.Should().Be(IntPtr.Zero, "Null D3D11 device must fail closed");
        sharedHandle.Should().Be(IntPtr.Zero);

        IntPtr opened = MoonshineNativeMethods.D3D11OpenSharedTexture(IntPtr.Zero, IntPtr.Zero, 1);
        opened.Should().Be(IntPtr.Zero, "Null shared handle must fail closed");
    }

    [Fact]
    public unsafe void D3D11CrossAdapterCopy_ValidSurfaces_CopiesAndRendersCorrectly()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        if (dev == IntPtr.Zero) return;

        try
        {
            IntPtr srcTex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1280, 720, 1, 0); // Teal pattern
            IntPtr dstTex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1280, 720, 0);

            srcTex.Should().NotBe(IntPtr.Zero);
            dstTex.Should().NotBe(IntPtr.Zero);

            try
            {
                int copyRes = MoonshineNativeMethods.D3D11CrossAdapterCopy(dev, srcTex, dev, dstTex, 1280, 720);
                copyRes.Should().Be(0, "Cross-adapter surface copy must succeed");

                byte[] outPixels = new byte[1280 * 720 * 4];
                fixed (byte* pOut = outPixels)
                {
                    int readRes = MoonshineNativeMethods.D3D11ReadbackPixels(dev, dstTex, pOut, (uint)outPixels.Length, out uint readBytes);
                    readRes.Should().Be(0);
                    readBytes.Should().Be((uint)outPixels.Length);

                    // Verify Teal pattern: Green > 100, Blue > 100, Red < 80 (BGRA layout: B=p[0], G=p[1], R=p[2])
                    pOut[0].Should().BeGreaterThan(100); // Blue
                    pOut[1].Should().BeGreaterThan(100); // Green
                    pOut[2].Should().BeLessThan(80);     // Red
                }
            }
            finally
            {
                MoonshineNativeMethods.D3D11DestroyTexture(srcTex);
                MoonshineNativeMethods.D3D11DestroyTexture(dstTex);
            }
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void EncoderDrainAndFlush_NullHandle_FailClosedSafely()
    {
        int drainRes = MoonshineNativeMethods.EncoderDrain(IntPtr.Zero);
        drainRes.Should().Be(0, "Null encoder handle must fail closed on drain");

        int flushRes = MoonshineNativeMethods.EncoderFlush(IntPtr.Zero);
        flushRes.Should().Be(0, "Null encoder handle must fail closed on flush");
    }
}
