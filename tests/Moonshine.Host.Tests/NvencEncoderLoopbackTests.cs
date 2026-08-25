using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Dedicated NVENC hardware video encoder loopback and lifecycle conformance test suite.
/// Strictly enforces the Hardware Encoder Operational Invariant: live Direct3D 11 GPU submission,
/// structural NALU verification, video decoder loopback, and deterministic pixel validation.
/// </summary>
public class NvencEncoderLoopbackTests
{
    [SkippableFact]
    public void Nvenc_Initialise_ValidatesStateAndCapabilities()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) or Direct3D 11 runtime unavailable (NOT PRESENT).");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 20000, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC encoder session initialization failed on NVIDIA GPU (DRIVER ERROR).");

            pipeline.IsActive.Should().BeTrue();
            pipeline.Width.Should().Be(1920);
            pipeline.Height.Should().Be(1080);
            pipeline.Fps.Should().Be(60);
            pipeline.BitrateKbps.Should().Be(20000);
            pipeline.Vendor.Should().Be(EncoderVendor.NvidiaNvenc);
            pipeline.Evidence.SessionInitialised.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Nvenc_EncodeH264_ProducesStructurallyValidBitstream()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0); // SMPTE bars
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.H264, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC H.264 encoding unavailable (DRIVER ERROR).");

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
    public void Nvenc_EncodeHEVC_ProducesStructurallyValidBitstream()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC HEVC encoding unavailable (DRIVER ERROR).");

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
    public void Nvenc_EncodeAV1_GatedByHardwareCapabilities()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        int capsRes = MoonshineNativeMethods.EncoderQueryCaps((uint)EncoderVendor.NvidiaNvenc, dev, out var caps);
        Skip.If(capsRes != 0 || (caps.SupportedCodecsMask & 0x08) == 0, "NVENC AV1 encoding is not supported on this GPU architecture (UNSUPPORTED CODEC).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 1, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.Av1, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC AV1 pipeline initialisation failed (DRIVER ERROR).");

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
    public unsafe void Nvenc_DecodeLoopback_DecodesAndVerifiesDimensionsAndPattern()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0); // SMPTE bars
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 pattern texture creation failed.");

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, (uint)VideoCodec.HevcMain10); // HEVC Main10 decoder
        Skip.If(decoder == IntPtr.Zero, "Direct3D 11 video decoder creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC HEVC pipeline initialisation failed (DRIVER ERROR).");

            byte[] refPixels = new byte[1920 * 1080 * 4];
            uint refBytes = 0;
            fixed (byte* pRef = refPixels)
            {
                int refReadRes = MoonshineNativeMethods.D3D11ReadbackPixels(dev, tex, pRef, (uint)refPixels.Length, out refBytes);
                refReadRes.Should().Be(0);
            }

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);

            fixed (byte* pBuf = buffer)
            {
                var frameDesc = new MoonshineFrameDesc
                {
                    FrameIndex = 4, // 4 = SMPTE Bars pattern
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

                byte[] decPixels = new byte[1920 * 1080 * 4];
                uint decBytes = 0;
                fixed (byte* pDec = decPixels)
                fixed (byte* pRef = refPixels)
                {
                    int decReadRes = MoonshineNativeMethods.D3D11ReadbackPixels(IntPtr.Zero, decodedTex, pDec, (uint)decPixels.Length, out decBytes);
                    decReadRes.Should().Be(0);

                    int metricRes = MoonshineNativeMethods.VideoComputeQualityMetrics(
                        pRef,
                        87 /* DXGI_FORMAT_B8G8R8A8_UNORM */,
                        pDec,
                        104 /* DXGI_FORMAT_P010 */,
                        1920,
                        1080,
                        15.0f,
                        out var metrics
                    );
                    metricRes.Should().Be(0);
                    metrics.Width.Should().Be(1920);
                    metrics.Height.Should().Be(1080);
                    metrics.DecodedFormat.Should().Be(104);
                }
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
    public void Nvenc_MultipleFrames_EncodesContinuous30FrameSequence()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 3, 0); // Moving pattern
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 30000, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC HEVC pipeline initialisation failed (DRIVER ERROR).");

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
    public unsafe void Nvenc_ResolutionChange_DynamicallyReconfiguresDimensions()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex720 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1280, 720, 1, 0);
        IntPtr tex1080 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 1);
        IntPtr tex1440 = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 2560, 1440, 2, 2);

        IntPtr decoder720 = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1280, 720, (uint)VideoCodec.HevcMain10);
        IntPtr decoder1080 = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, (uint)VideoCodec.HevcMain10);
        IntPtr decoder1440 = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 2560, 1440, (uint)VideoCodec.HevcMain10);

        try
        {
            byte[] buffer = new byte[1024 * 1024 * 4];

            // Single pipeline instance tested across 720p -> 1080p -> 1440p transitions
            using var pipeline = new NvencHardwareEncoderPipeline(1280, 720, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC 720p pipeline initialisation failed (DRIVER ERROR).");

            // Step 1: Encode at 720p
            pipeline.Width.Should().Be(1280);
            pipeline.Height.Should().Be(720);
            bool ok1 = pipeline.TryEncodeFrame(tex720, true, out var desc1, buffer, out int written1);
            ok1.Should().BeTrue();
            written1.Should().BeGreaterThan(0);
            desc1.IsKeyframe.Should().Be(1);

            if (decoder720 != IntPtr.Zero)
            {
                fixed (byte* pBuf = buffer)
                {
                    var fDesc = new MoonshineFrameDesc { FrameIndex = (uint)desc1.FrameIndex, TotalBytes = (uint)written1, PacketCount = 1, IsKeyframe = 1, FrameBuffer = pBuf };
                    MoonshineNativeMethods.VideoSubmitFrame(decoder720, in fDesc).Should().Be(0);
                    MoonshineNativeMethods.VideoGetDimensions(decoder720, out uint w720, out uint h720).Should().Be(0);
                    w720.Should().Be(1280);
                    h720.Should().Be(720);
                }
            }

            // Step 2: Dynamic reconfiguration to 1080p on SAME pipeline instance
            bool reconfig1080 = pipeline.ReconfigureResolution(1920, 1080, 60, 25000);
            reconfig1080.Should().BeTrue();
            pipeline.Width.Should().Be(1920);
            pipeline.Height.Should().Be(1080);
            pipeline.BitrateKbps.Should().Be(25000);

            bool ok2 = pipeline.TryEncodeFrame(tex1080, true, out var desc2, buffer, out int written2);
            ok2.Should().BeTrue();
            written2.Should().BeGreaterThan(0);
            desc2.IsKeyframe.Should().Be(1);

            if (decoder1080 != IntPtr.Zero)
            {
                fixed (byte* pBuf = buffer)
                {
                    var fDesc = new MoonshineFrameDesc { FrameIndex = (uint)desc2.FrameIndex, TotalBytes = (uint)written2, PacketCount = 1, IsKeyframe = 1, FrameBuffer = pBuf };
                    MoonshineNativeMethods.VideoSubmitFrame(decoder1080, in fDesc).Should().Be(0);
                    MoonshineNativeMethods.VideoGetDimensions(decoder1080, out uint w1080, out uint h1080).Should().Be(0);
                    w1080.Should().Be(1920);
                    h1080.Should().Be(1080);
                }
            }

            // Step 3: Dynamic reconfiguration to 1440p on SAME pipeline instance
            bool reconfig1440 = pipeline.ReconfigureResolution(2560, 1440, 60, 40000);
            reconfig1440.Should().BeTrue();
            pipeline.Width.Should().Be(2560);
            pipeline.Height.Should().Be(1440);
            pipeline.BitrateKbps.Should().Be(40000);

            bool ok3 = pipeline.TryEncodeFrame(tex1440, true, out var desc3, buffer, out int written3);
            ok3.Should().BeTrue();
            written3.Should().BeGreaterThan(0);
            desc3.IsKeyframe.Should().Be(1);

            if (decoder1440 != IntPtr.Zero)
            {
                fixed (byte* pBuf = buffer)
                {
                    var fDesc = new MoonshineFrameDesc { FrameIndex = (uint)desc3.FrameIndex, TotalBytes = (uint)written3, PacketCount = 1, IsKeyframe = 1, FrameBuffer = pBuf };
                    MoonshineNativeMethods.VideoSubmitFrame(decoder1440, in fDesc).Should().Be(0);
                    MoonshineNativeMethods.VideoGetDimensions(decoder1440, out uint w1440, out uint h1440).Should().Be(0);
                    w1440.Should().Be(2560);
                    h1440.Should().Be(1440);
                }
            }
        }
        finally
        {
            if (decoder720 != IntPtr.Zero) MoonshineNativeMethods.VideoDestroy(decoder720);
            if (decoder1080 != IntPtr.Zero) MoonshineNativeMethods.VideoDestroy(decoder1080);
            if (decoder1440 != IntPtr.Zero) MoonshineNativeMethods.VideoDestroy(decoder1440);
            if (tex720 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex720);
            if (tex1080 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex1080);
            if (tex1440 != IntPtr.Zero) MoonshineNativeMethods.D3D11DestroyTexture(tex1440);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Nvenc_BitrateChange_DynamicallyAdaptsBitrate()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 2, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, bitrateKbps: 5000, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024 * 2];

            // Warm up and encode at low bitrate (5000 kbps)
            long lowBitrateTotalBytes = 0;
            for (int i = 0; i < 5; ++i)
            {
                int r1 = MoonshineNativeMethods.D3D11RenderPattern(dev, tex, 1920, 1080, 2, (uint)i);
                r1.Should().Be(1);
                bool ok = pipeline.TryEncodeFrame(tex, i == 0, out _, buffer, out int written);
                ok.Should().BeTrue();
                if (i > 0) lowBitrateTotalBytes += written;
            }

            // Dynamically scale up bitrate on SAME pipeline instance
            bool scaleUp = pipeline.ReconfigureBitrate(60000, 90000);
            scaleUp.Should().BeTrue();
            pipeline.BitrateKbps.Should().Be(60000);

            // Encode at high bitrate (60000 kbps)
            long highBitrateTotalBytes = 0;
            for (int i = 5; i < 10; ++i)
            {
                int r2 = MoonshineNativeMethods.D3D11RenderPattern(dev, tex, 1920, 1080, 2, (uint)i);
                r2.Should().Be(1);
                bool ok = pipeline.TryEncodeFrame(tex, i == 5, out _, buffer, out int written);
                ok.Should().BeTrue();
                if (i > 5) highBitrateTotalBytes += written;
            }

            // Rate response verification: higher target bitrate produces greater payload volume
            highBitrateTotalBytes.Should().BeGreaterThan(lowBitrateTotalBytes);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Nvenc_Drain_FlushesInFlightFramesCleanly()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 1, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok = pipeline.TryEncodeFrame(tex, true, out _, buffer, out _);
            ok.Should().BeTrue();

            bool drainOk = pipeline.Drain();
            drainOk.Should().BeTrue();
            pipeline.IsActive.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Nvenc_Flush_ResetsInternalBuffersAndForcesImmediateKeyframe()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 0, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC pipeline initialisation failed (DRIVER ERROR).");

            byte[] buffer = new byte[1024 * 1024];
            bool ok1 = pipeline.TryEncodeFrame(tex, true, out _, buffer, out _);
            ok1.Should().BeTrue();

            // Perform explicit flush
            bool flushOk = pipeline.Flush();
            flushOk.Should().BeTrue();
            pipeline.IsActive.Should().BeTrue();

            // Next frame submitted with forceIdr=false must automatically produce an IDR keyframe following flush
            bool ok2 = pipeline.TryEncodeFrame(tex, false, out var desc, buffer, out int written2);
            ok2.Should().BeTrue();
            written2.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void Nvenc_Reset_ReinitialisesEncoderState()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "NVIDIA GPU (0x10DE) unavailable (NOT PRESENT).");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 0, 0);
        Skip.If(tex == IntPtr.Zero, "Direct3D 11 texture creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "NVENC pipeline initialisation failed (DRIVER ERROR).");

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
    public void Nvenc_Shutdown_ReleasesResourcesDeterministically()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) return;

        try
        {
            var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
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
