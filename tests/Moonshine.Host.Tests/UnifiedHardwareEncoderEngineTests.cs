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
        engine.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_SubmitFrameAndPoll_WhenInactive_ReturnsExpected()
    {
        using var engine = new UnifiedHardwareEncoderEngine(1920, 1080);
        Span<byte> buffer = stackalloc byte[256];
        var result = engine.SubmitFrame(IntPtr.Zero, false, buffer, out int written);
        result.Submitted.Should().BeFalse();
        written.Should().Be(0);
        result.Result.Should().Be(EncoderResult.NotAvailable);

        bool polled = engine.TryPollPacket(buffer, out _, out _);
        polled.Should().BeFalse();
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

    [Fact]
    public void UnifiedHardwareEncoderEngine_AutoSelection_WithHardwareDevice_SelectsVendorAndEncodesRealFrames()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var engine = new UnifiedHardwareEncoderEngine(
                1920,
                1080,
                fps: 60,
                bitrateKbps: 20000,
                codec: VideoCodec.Hevc,
                preferredVendor: EncoderVendor.Auto,
                d3dDevice: dev
            );

            if (!engine.IsActive) return;

            engine.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
            engine.Vendor.Should().NotBe(EncoderVendor.Auto);

            Span<byte> bitstream = stackalloc byte[1024 * 512];
            var sub1 = engine.SubmitFrame(tex, true, bitstream, out int written1);
            sub1.Submitted.Should().BeTrue();
            sub1.OutputAvailable.Should().BeTrue();
            sub1.BytesWritten.Should().Be(written1);
            written1.Should().BeGreaterThan(0);
            sub1.Result.Should().Be(EncoderResult.Success);

            // Record decoder acceptance for frame 1
            engine.RecordDecoderAcceptance(sub1.PacketDesc.FrameIndex);
            engine.FramesEncoded.Should().Be(1);
            engine.KeyframesEmitted.Should().Be(1);

            // Frame 2 (inter-frame)
            var sub2 = engine.SubmitFrame(tex, false, bitstream, out int written2);
            sub2.Submitted.Should().BeTrue();
            sub2.OutputAvailable.Should().BeTrue();
            written2.Should().BeGreaterThan(0);
            engine.FramesEncoded.Should().Be(2);

            // Dynamic reconfiguration
            bool reconfigured = engine.ReconfigureBitrate(30000, 120);
            reconfigured.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_RapidStartStop_10Cycles_SoakTest()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1280, 720, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[1024 * 256];
            for (int cycle = 0; cycle < 10; cycle++)
            {
                using var engine = new UnifiedHardwareEncoderEngine(
                    1280,
                    720,
                    fps: 60,
                    bitrateKbps: 10000,
                    codec: VideoCodec.H264,
                    preferredVendor: EncoderVendor.Auto,
                    d3dDevice: dev
                );

                if (!engine.IsActive) continue;

                var result = engine.SubmitFrame(tex, true, buffer, out int written);
                result.Submitted.Should().BeTrue();
                written.Should().BeGreaterThan(0);
                engine.RecordDecoderAcceptance(result.PacketDesc.FrameIndex);
            }
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void UnifiedHardwareEncoderEngine_MultiInstance_Concurrency_SoakTest()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        if (dev == IntPtr.Zero) return;

        IntPtr tex1 = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        IntPtr tex2 = MoonshineNativeMethods.D3D11CreateTexture(dev, 1280, 720, 0);
        if (tex1 == IntPtr.Zero || tex2 == IntPtr.Zero)
        {
            if (tex1 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex1);
            if (tex2 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex2);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var engine1 = new UnifiedHardwareEncoderEngine(1920, 1080, 60, 20000, VideoCodec.Hevc, preferredVendor: EncoderVendor.Auto, d3dDevice: dev);
            using var engine2 = new UnifiedHardwareEncoderEngine(1280, 720, 60, 10000, VideoCodec.H264, preferredVendor: EncoderVendor.Auto, d3dDevice: dev);

            if (!engine1.IsActive || !engine2.IsActive) return;

            byte[] buffer1 = new byte[1024 * 1024 * 2];
            byte[] buffer2 = new byte[1024 * 1024 * 2];

            for (int i = 0; i < 5; i++)
            {
                var res1 = engine1.SubmitFrame(tex1, i == 0, buffer1, out int written1);
                res1.Submitted.Should().BeTrue();
                written1.Should().BeGreaterThan(0);
                engine1.RecordDecoderAcceptance(res1.PacketDesc.FrameIndex);

                var res2 = engine2.SubmitFrame(tex2, i == 0, buffer2, out int written2);
                res2.Submitted.Should().BeTrue();
                written2.Should().BeGreaterThan(0);
                engine2.RecordDecoderAcceptance(res2.PacketDesc.FrameIndex);
            }

            engine1.FramesEncoded.Should().Be(5);
            engine2.FramesEncoded.Should().Be(5);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex1);
            MoonshineNativeMethods.D3D11DestroyTexture(tex2);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }
}
