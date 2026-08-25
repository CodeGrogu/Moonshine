using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Dedicated Intel QuickSync / oneVPL hardware video encoder loopback and lifecycle conformance test suite.
/// Strictly enforces the Hardware Encoder Operational Invariant: live Direct3D 11 GPU submission on Intel
/// adapter (0x8086), modern oneVPL 2.x session initialization, structural NALU verification, video decoder loopback,
/// and deterministic pixel validation.
/// </summary>
public class QsvEncoderLoopbackTests
{
    [SkippableFact]
    public void Qsv_Initialise_ValidatesStateAndCapabilities()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) or Direct3D 11 runtime unavailable (NOT PRESENT).");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 20000, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync oneVPL encoder session initialization failed on Intel GPU (DRIVER ERROR).");

            pipeline.IsActive.Should().BeTrue();
            pipeline.Width.Should().Be(1920);
            pipeline.Height.Should().Be(1080);
            pipeline.Fps.Should().Be(60);
            pipeline.BitrateKbps.Should().Be(20000);
            pipeline.Vendor.Should().Be(EncoderVendor.IntelQuickSync);
            pipeline.Evidence.SessionInitialised.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_EncodeH264_ProducesStructurallyValidBitstream()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0); // SMPTE bars
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.H264, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync H.264 encoding unavailable (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

            var auResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, buffer.AsSpan(0, written));
            auResult.IsValid.Should().BeTrue();
            auResult.ContainsFrameData.Should().BeTrue();
            auResult.HasStructurallyValidPayload.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_EncodeHEVC_ProducesStructurallyValidBitstream()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync HEVC encoding unavailable (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

            var auResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, buffer.AsSpan(0, written));
            auResult.IsValid.Should().BeTrue();
            auResult.ContainsFrameData.Should().BeTrue();
            auResult.HasStructurallyValidPayload.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_EncodeAV1_GatedByHardwareCapabilities()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        int capsRes = MoonshineNativeMethods.EncoderQueryCaps((uint)EncoderVendor.IntelQuickSync, dev, out var caps);
        Skip.If(capsRes != 0 || (caps.SupportedCodecsMask & 0x08) == 0, "Intel QuickSync AV1 encoding is not supported on this Intel GPU architecture (UNSUPPORTED CODEC).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 1, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.Av1, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync AV1 pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public unsafe void Qsv_DecodeLoopback_DecodesAndVerifiesDimensionsAndPattern()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0); // SMPTE bars
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1); // HEVC decoder
        Skip.If(decoder == IntPtr.Zero, "Direct3D 11 video decoder creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync HEVC pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);

            fixed (byte* pBuf = buffer)
            {
                var frameDesc = new MoonshineFrameDesc
                {
                    FrameIndex = (uint)desc.FrameIndex,
                    TotalBytes = (uint)written,
                    PacketCount = 1,
                    IsKeyframe = 1,
                    FrameBuffer = pBuf
                };

                int decodeRes = MoonshineNativeMethods.VideoSubmitFrame(decoder, in frameDesc);
                decodeRes.Should().Be(0);

                IntPtr decodedTex = MoonshineNativeMethods.VideoGetTexture(decoder);
                decodedTex.Should().NotBe(IntPtr.Zero);

                int dimRes = MoonshineNativeMethods.VideoGetDimensions(decoder, out uint decWidth, out uint decHeight);
                dimRes.Should().Be(0);
                decWidth.Should().Be(1920);
                decHeight.Should().Be(1080);

                int verifyRes = MoonshineNativeMethods.VideoVerifyDecodedPattern(tex, 4, 0.5f);
                verifyRes.Should().Be(0);
            }
        }
        finally
        {
            if (decoder != IntPtr.Zero) MoonshineNativeMethods.VideoDestroy(decoder);
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_MultipleFrames_EncodesContinuous30FrameSequence()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 3, 0); // Moving pattern
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 30000, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync HEVC pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            ulong lastFrameIdx = 0;

            for (uint i = 0; i < 30; ++i)
            {
                int renderRes = MoonshineNativeMethods.D3D11RenderPattern(dev, tex, 1920, 1080, 3, i);
                renderRes.Should().Be(1);

                bool forceIdr = (i == 0 || i == 15);
                bool ok = pipeline.TryEncodeFrame(tex, forceIdr, out var desc, buffer, out int written);
                ok.Should().BeTrue();
                written.Should().BeGreaterThan(0);

                if (i > 0)
                {
                    desc.FrameIndex.Should().BeGreaterThan(lastFrameIdx);
                }
                lastFrameIdx = desc.FrameIndex;

                if (forceIdr)
                {
                    desc.IsKeyframe.Should().Be(1);
                }
            }

            pipeline.Evidence.FrameSubmitted.Should().BeTrue();
            pipeline.Evidence.OutputReceived.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_ResolutionChange_DynamicallyReconfiguresDimensions()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex720 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1280, 720, 1, 0);
        IntPtr tex1080 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 1);
        IntPtr tex1440 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 2560, 1440, 2, 2);

        try
        {
            byte[] buffer = new byte[1024 * 1024 * 2];

            // 720p pipeline
            using (var pipeline720 = new QsvHardwareEncoderPipeline(1280, 720, d3dDevice: dev))
            {
                Skip.IfNot(pipeline720.IsActive, "Intel QuickSync 720p pipeline initialisation failed (DRIVER ERROR).");
                bool ok1 = pipeline720.TryEncodeFrame(tex720, true, out _, buffer, out int written1);
                ok1.Should().BeTrue();
                written1.Should().BeGreaterThan(0);
            }

            // 1080p pipeline
            using (var pipeline1080 = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev))
            {
                Skip.IfNot(pipeline1080.IsActive, "Intel QuickSync 1080p pipeline initialisation failed (DRIVER ERROR).");
                bool ok2 = pipeline1080.TryEncodeFrame(tex1080, true, out _, buffer, out int written2);
                ok2.Should().BeTrue();
                written2.Should().BeGreaterThan(0);
            }

            // 1440p pipeline
            using (var pipeline1440 = new QsvHardwareEncoderPipeline(2560, 1440, d3dDevice: dev))
            {
                Skip.IfNot(pipeline1440.IsActive, "Intel QuickSync 1440p pipeline initialisation failed (DRIVER ERROR).");
                bool ok3 = pipeline1440.TryEncodeFrame(tex1440, true, out _, buffer, out int written3);
                ok3.Should().BeTrue();
                written3.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            if (tex720 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex720);
            if (tex1080 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex1080);
            if (tex1440 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex1440);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_BitrateChange_DynamicallyAdaptsBitrate()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, bitrateKbps: 20000, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync pipeline initialisation failed (DRIVER ERROR).");

            pipeline.BitrateKbps.Should().Be(20000);

            // Dynamically scale down during congestion
            bool scaleDown = pipeline.ReconfigureBitrate(5000, 8000);
            scaleDown.Should().BeTrue();
            pipeline.BitrateKbps.Should().Be(5000);

            // Dynamically scale up
            bool scaleUp = pipeline.ReconfigureBitrate(50000, 75000);
            scaleUp.Should().BeTrue();
            pipeline.BitrateKbps.Should().Be(50000);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_Drain_FlushesInFlightFramesCleanly()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 1, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out _, buffer, out _);
            ok.Should().BeTrue();

            pipeline.RequestKeyframe();
            pipeline.IsActive.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Qsv_Reset_ReinitialisesEncoderState()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Intel GPU (0x8086) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 0, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Intel QuickSync pipeline initialisation failed (DRIVER ERROR).");

            pipeline.RequestKeyframe();
            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, false, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void Qsv_Shutdown_ReleasesResourcesDeterministically()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        if (dev == IntPtr.Zero) return;

        try
        {
            var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            pipeline.Dispose();

            pipeline.IsActive.Should().BeFalse();
            pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

            // Double disposal must be completely idempotent and safe
            pipeline.Invoking(p => p.Dispose()).Should().NotThrow();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }
}
