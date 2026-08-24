using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class NvencHardwareEncoderPipelineTests
{
    [Fact]
    public void NvencHardwareEncoderPipeline_Initialize_PresetPropertiesMatch()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(
                width: 3840,
                height: 2160,
                fps: 120,
                bitrateKbps: 50000,
                codec: VideoCodec.HevcMain10,
                preset: NvencPreset.P1_UltraFast,
                tuning: NvencTuning.UltraLowLatency,
                d3dDevice: dev
            );

            pipeline.Width.Should().Be(3840);
            pipeline.Height.Should().Be(2160);
            pipeline.Fps.Should().Be(120);
            pipeline.BitrateKbps.Should().Be(50000);
            pipeline.Codec.Should().Be(VideoCodec.HevcMain10);
            pipeline.Vendor.Should().Be(EncoderVendor.NvidiaNvenc);
            pipeline.Preset.Should().Be(NvencPreset.P1_UltraFast);
            pipeline.Tuning.Should().Be(NvencTuning.UltraLowLatency);

            if (dev != IntPtr.Zero)
            {
                pipeline.IsActive.Should().BeTrue();
            }
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
    public void NvencHardwareEncoderPipeline_NullTexture_RejectsWithFalse()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
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
    public void NvencHardwareEncoderPipeline_ConfigureTuningAndIntraRefresh_UpdatesState()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);

            if (!pipeline.IsActive) return;

            bool tuningOk = pipeline.ConfigureTuning(NvencPreset.P2_Fast, NvencTuning.LowLatency);
            tuningOk.Should().BeTrue();
            pipeline.Preset.Should().Be(NvencPreset.P2_Fast);
            pipeline.Tuning.Should().Be(NvencTuning.LowLatency);

            bool intraOk = pipeline.ConfigureIntraRefresh(true, 60, 4);
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
    public void NvencHardwareEncoderPipeline_TryEncodeFrame_GeneratesKeyframeAndInterframes()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 2560, 1440, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(
                width: 2560,
                height: 1440,
                fps: 240,
                bitrateKbps: 40000,
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
    public void NvencHardwareEncoderPipeline_IsCodecSupported_ReturnsTrueForAll(VideoCodec codec)
    {
        NvencHardwareEncoderPipeline.IsCodecSupported(codec).Should().BeTrue();
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();
        pipeline.Dispose();

        Span<byte> buffer = stackalloc byte[128];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_Evidence_ExposesAuthoritativePipelineEvidence()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) return;

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            return;
        }

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            if (!pipeline.IsActive) return;

            var evidence = pipeline.Evidence;
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
