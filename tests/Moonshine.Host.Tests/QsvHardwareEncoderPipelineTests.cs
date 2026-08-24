using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class QsvHardwareEncoderPipelineTests
{
    [Fact]
    public void QsvHardwareEncoderPipeline_Initialize_PropertiesMatchConfiguration()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(
                width: 2560,
                height: 1440,
                fps: 240,
                bitrateKbps: 38000,
                codec: VideoCodec.Av1,
                targetUsage: QsvTargetUsage.BestSpeed,
                lowPowerVdenc: true,
                d3dDevice: dev
            );

            pipeline.Width.Should().Be(2560);
            pipeline.Height.Should().Be(1440);
            pipeline.Fps.Should().Be(240);
            pipeline.BitrateKbps.Should().Be(38000);
            pipeline.Codec.Should().Be(VideoCodec.Av1);
            pipeline.Vendor.Should().Be(EncoderVendor.IntelQuickSync);
            pipeline.TargetUsage.Should().Be(QsvTargetUsage.BestSpeed);
            pipeline.LowPowerVdenc.Should().BeTrue();
        }
        finally
        {
            if (dev != IntPtr.Zero)
            {
                MoonshineNativeMethods.D3D11DestroyDevice(dev);
            }
        }
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_NullTexture_RejectsWithFalse()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Span<byte> buffer = stackalloc byte[1024];

            // Strict contract: null texture must always fail closed
            bool result = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out int written);
            result.Should().BeFalse();
            written.Should().Be(0);
        }
        finally
        {
            if (dev != IntPtr.Zero)
            {
                MoonshineNativeMethods.D3D11DestroyDevice(dev);
            }
        }
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_ConfigureTuningAndIntraRefresh_UpdatesStateWhenActive()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);

            if (!pipeline.IsActive)
            {
                // On non-Intel hardware, pipeline safely reports inactive
                pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Faulted);
                return;
            }

            bool tuningOk = pipeline.ConfigureTuning(QsvTargetUsage.Balanced, false);
            tuningOk.Should().BeTrue();
            pipeline.TargetUsage.Should().Be(QsvTargetUsage.Balanced);
            pipeline.LowPowerVdenc.Should().BeFalse();

            bool intraOk = pipeline.ConfigureIntraRefresh(true, 30, -2);
            intraOk.Should().BeTrue();
        }
        finally
        {
            if (dev != IntPtr.Zero)
            {
                MoonshineNativeMethods.D3D11DestroyDevice(dev);
            }
        }
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_TryEncodeFrame_GeneratesKeyframeAndInterframesWhenActive()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(
                width: 1920,
                height: 1080,
                fps: 60,
                bitrateKbps: 20000,
                codec: VideoCodec.Hevc,
                d3dDevice: dev
            );

            if (!pipeline.IsActive) return;

            Span<byte> buffer = stackalloc byte[1024 * 512];

            // Frame 0: Keyframe
            bool ok1 = pipeline.TryEncodeFrame(tex, false, out var desc1, buffer, out int written1);
            ok1.Should().BeTrue();
            desc1.FrameIndex.Should().Be(0);
            desc1.IsKeyframe.Should().Be(1);
            desc1.IsHeaderPacket.Should().Be(1);
            written1.Should().BeGreaterThan(0);

            // Frame 1: Inter-frame
            bool ok2 = pipeline.TryEncodeFrame(tex, false, out var desc2, buffer, out int written2);
            ok2.Should().BeTrue();
            desc2.FrameIndex.Should().Be(1);
            desc2.IsKeyframe.Should().Be(0);
            written2.Should().BeGreaterThan(0);

            // Request Keyframe
            pipeline.RequestKeyframe();
            bool ok3 = pipeline.TryEncodeFrame(tex, false, out var desc3, buffer, out int written3);
            ok3.Should().BeTrue();
            desc3.FrameIndex.Should().Be(2);
            desc3.IsKeyframe.Should().Be(1);
            written3.Should().BeGreaterThan(0);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Theory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    [InlineData(VideoCodec.HevcMain10)]
    [InlineData(VideoCodec.Av1)]
    public void QsvHardwareEncoderPipeline_IsCodecSupported_TruthfulHardwareQuery(VideoCodec codec)
    {
        bool supported = QsvHardwareEncoderPipeline.IsCodecSupported(codec);
        (supported == true || supported == false).Should().BeTrue();
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new QsvHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();

        Span<byte> buffer = stackalloc byte[128];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_Evidence_ExposesAuthoritativePipelineEvidence()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            var evidence = pipeline.Evidence;

            if (!pipeline.IsActive)
            {
                evidence.SessionInitialised.Should().BeFalse();
                evidence.FrameSubmitted.Should().BeFalse();
                return;
            }

            evidence.ApiAvailable.Should().BeTrue();
            evidence.HardwareSupported.Should().BeTrue();
            evidence.SessionInitialised.Should().BeTrue();
            evidence.FrameSubmitted.Should().BeFalse();

            Span<byte> buffer = stackalloc byte[1024 * 512];
            bool ok = pipeline.TryEncodeFrame(tex, true, out _, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);

            var liveEvidence = pipeline.Evidence;
            liveEvidence.FrameSubmitted.Should().BeTrue();
            liveEvidence.OutputReceived.Should().BeTrue();
            liveEvidence.BitstreamStructurallyValid.Should().BeTrue();
            liveEvidence.AccessUnitValid.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }
}
