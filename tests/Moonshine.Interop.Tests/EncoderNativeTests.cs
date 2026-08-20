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
    [InlineData(0u)] // Auto
    [InlineData(1u)] // NVENC
    [InlineData(2u)] // AMF
    [InlineData(3u)] // QuickSync
    [InlineData(4u)] // D3D11
    public void EncoderQueryCaps_AllVendors_ReturnsSuccess(uint vendor)
    {
        int res = MoonshineNativeMethods.EncoderQueryCaps(vendor, IntPtr.Zero, out var caps);
        res.Should().Be(1);
        caps.MaxWidth.Should().BeGreaterThanOrEqualTo(3840);
        caps.MaxFps.Should().BeGreaterThanOrEqualTo(120);
        caps.MaxBitrateKbps.Should().BeGreaterThanOrEqualTo(100000);
    }

    [Fact]
    public unsafe void EncoderLifecycle_CreateEncodeAndDestroy_ExecutesCleanly()
    {
        var config = new MoonshineEncoderConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            PeakBitrateKbps = 30000,
            Codec = 2, // HEVC Main10
            RcMode = 0, // CBR
            GopLength = 0,
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        IntPtr handle = MoonshineNativeMethods.EncoderCreate(0, IntPtr.Zero, in config);
        handle.Should().NotBe(IntPtr.Zero);

        byte[] buffer = new byte[1024 * 1024];
        fixed (byte* bufPtr = buffer)
        {
            // Frame 0: Keyframe
            int res = MoonshineNativeMethods.EncoderEncodeFrame(
                handle,
                IntPtr.Zero,
                0,
                out var desc,
                bufPtr,
                (uint)buffer.Length,
                out uint written
            );

            res.Should().Be(1);
            desc.FrameIndex.Should().Be(0);
            desc.IsKeyframe.Should().Be(1);
            desc.IsHeaderPacket.Should().Be(1);
            written.Should().BeGreaterThan(0);
            desc.PayloadSize.Should().Be(written);

            // Frame 1: Inter-frame
            res = MoonshineNativeMethods.EncoderEncodeFrame(
                handle,
                IntPtr.Zero,
                0,
                out desc,
                bufPtr,
                (uint)buffer.Length,
                out written
            );

            res.Should().Be(1);
            desc.FrameIndex.Should().Be(1);
            desc.IsKeyframe.Should().Be(0);
        }

        MoonshineNativeMethods.EncoderDestroy(handle);
    }
}
