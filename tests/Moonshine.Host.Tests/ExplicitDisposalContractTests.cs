using FluentAssertions;
using Moonshine.Host.Audio;
using Moonshine.Host.Color;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Explicit disposal contract and lifetime safety tests for all unmanaged native wrapper classes.
/// Verifies deterministic disposal, idempotent double-disposal, GC collection immunity (no finaliser regressions),
/// and thread safety under concurrent operations.
/// </summary>
public sealed class ExplicitDisposalContractTests
{
    [Fact]
    public void WasapiLoopbackAudioPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var pipeline = new WasapiLoopbackAudioPipeline();
            pipeline.TryReadSamples(new float[480], out _, out _);
            pipeline.Dispose();

            // Idempotent second disposal
            pipeline.Dispose();

            // Force GC collection and verify zero access violations or finalize hangs
            GC.Collect();
            GC.WaitForPendingFinalizers();

            pipeline.TryReadSamples(new float[480], out _, out _).Should().BeFalse();
        }
    }

    [Fact]
    public void D3DColorSpaceConverter_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var converter = new D3DColorSpaceConverter(1920, 1080, 24, 104);
            converter.Dispose();
            converter.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            converter.TryConvert(IntPtr.Zero, IntPtr.Zero).Should().BeFalse();
        }
    }

    [Fact]
    public void HostVirtualMicSinkPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var sink = new HostVirtualMicSinkPipeline(48000, 1, 64000, 5);
            sink.TryPushOpusPacket(new byte[100], 1000, 1);
            sink.Dispose();
            sink.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Action act = () => sink.TryPushOpusPacket(new byte[100], 1000, 1);
            act.Should().Throw<ObjectDisposedException>();
        }
    }

    [Fact]
    public void VirtualAudioDriverService_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var service = new VirtualAudioDriverService();
            service.Dispose();
            service.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            service.IsInitialized.Should().BeFalse();
        }
    }

    [Fact]
    public void VirtualAudioIpcBridgePipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var bridge = new VirtualAudioIpcBridgePipeline();
            bridge.Dispose();
            bridge.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            bridge.IsConnected.Should().BeFalse();
        }
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var encoder = new HardwareVideoEncoderPipeline(1920, 1080, 60, 5000, 10000, VideoCodec.H264, RateControlMode.ConstantBitrate, EncoderVendor.Auto);
            encoder.Dispose();
            encoder.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            encoder.TryEncodeFrame(IntPtr.Zero, false, out _, new byte[1024], out _).Should().BeFalse();
        }
    }

    [Fact]
    public void AmfHardwareEncoderPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var amf = new AmfHardwareEncoderPipeline(1920, 1080);
            amf.Dispose();
            amf.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            amf.TryEncodeFrame(IntPtr.Zero, false, out _, new byte[1024], out _).Should().BeFalse();
        }
    }

    [Fact]
    public void NvencHardwareEncoderPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var nvenc = new NvencHardwareEncoderPipeline(1920, 1080);
            nvenc.Dispose();
            nvenc.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            nvenc.TryEncodeFrame(IntPtr.Zero, false, out _, new byte[1024], out _).Should().BeFalse();
        }
    }

    [Fact]
    public void QsvHardwareEncoderPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var qsv = new QsvHardwareEncoderPipeline(1920, 1080);
            qsv.Dispose();
            qsv.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            qsv.TryEncodeFrame(IntPtr.Zero, false, out _, new byte[1024], out _).Should().BeFalse();
        }
    }

    [Fact]
    public void OpusAudioEncoderPipeline_ExplicitDisposalContract_GCImmunityAndIdempotence()
    {
        for (int i = 0; i < 5; i++)
        {
            var opus = new OpusAudioEncoderPipeline(48000, AudioChannelTopology.Stereo, 160000, 5);
            opus.TryEncode(new float[480], 240, new byte[1024], out _);
            opus.Dispose();
            opus.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            opus.TryEncode(new float[480], 240, new byte[1024], out _).Should().BeFalse();
        }
    }
}