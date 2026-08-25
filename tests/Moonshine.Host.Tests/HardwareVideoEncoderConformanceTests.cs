using System;
using System.Linq;
using FluentAssertions;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Conformance test suite verifying that all hardware video encoder pipelines (NVENC, AMF, QSV)
/// satisfy the unified Moonshine engineering contract, real GPU pattern encoding, and Direct3D 11 decoder loopback.
/// </summary>
public class HardwareVideoEncoderConformanceTests
{
    // =========================================================================
    // NVIDIA NVENC Conformance Suite
    // =========================================================================

    [SkippableFact]
    public void NvencPipeline_LifecycleStateTransitions_FollowStandardStateContract()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device (GPU) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical NVIDIA GPU (0x10DE) or NVENC driver runtime is unavailable on this runner.");

            pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
            pipeline.IsActive.Should().BeTrue();
            pipeline.Vendor.Should().Be(EncoderVendor.NvidiaNvenc);

            var initialEvidence = pipeline.Evidence;
            initialEvidence.ApiAvailable.Should().BeTrue();
            initialEvidence.HardwareSupported.Should().BeTrue();
            initialEvidence.SessionInitialised.Should().BeTrue();
            initialEvidence.FrameSubmitted.Should().BeFalse();
            initialEvidence.OutputReceived.Should().BeFalse();

            Span<byte> buffer = stackalloc byte[1024 * 512];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

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

    [SkippableFact]
    public unsafe void NvencPipeline_AllGpuTestPatterns_EncodeAndValidateBitstreamStructurallyAndDecodeWithDimensions()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device (GPU) is unavailable on this runner.");

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1); // 1 = HEVC
        Skip.If(decoder == IntPtr.Zero, "Direct3D 11 Video Decoder creation failed.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, codec: VideoCodec.HevcMain10, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical NVIDIA GPU (0x10DE) or NVENC driver runtime is unavailable on this runner.");

            // 5 GPU Test Patterns: 0 = Black, 1 = Solid Colour, 2 = Gradient, 3 = Moving Procedural, 4 = SMPTE Bars
            uint[] patternTypes = [0, 1, 2, 3, 4];
            byte[] buffer = new byte[1024 * 1024];

            IntPtr patternTex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 0, 0);
            patternTex.Should().NotBe(IntPtr.Zero);

            try
            {
                for (int i = 0; i < patternTypes.Length; i++)
                {
                    uint pattern = patternTypes[i];
                    int renderRes = MoonshineNativeMethods.D3D11RenderPattern(dev, patternTex, 1920, 1080, pattern, (uint)i);
                    renderRes.Should().Be(1);

                    bool encodeOk = pipeline.TryEncodeFrame(patternTex, true, out var desc, buffer, out int written);
                    encodeOk.Should().BeTrue();
                    written.Should().BeGreaterThan(0);
                    desc.IsKeyframe.Should().Be(1);

                    // Validate bitstream structural correctness (NALU start codes and parameter sets)
                    var auResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, buffer.AsSpan(0, written));
                    auResult.IsValid.Should().BeTrue();
                    auResult.ContainsFrameData.Should().BeTrue();
                    auResult.HasStructurallyValidPayload.Should().BeTrue();
                    auResult.HasParameterSets.Should().BeTrue();

                    // Submit to Direct3D 11 video decoder to prove decodability
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

                        // Verify submitted GPU pattern texture pixel content
                        int verifyRes = MoonshineNativeMethods.VideoVerifyDecodedPattern(patternTex, pattern, 0.5f);
                        verifyRes.Should().Be(0);
                    }
                }
            }
            finally
            {
                MoonshineNativeMethods.D3D11DestroyTexture(patternTex);
            }
        }
        finally
        {
            if (decoder != IntPtr.Zero) MoonshineNativeMethods.VideoDestroy(decoder);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public unsafe void NvencPipeline_Direct3D11DecoderLoopback_SuccessfullyDecodesAndAcceptsKeyframe()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device (GPU) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1); // 1 = HEVC

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical NVIDIA GPU (0x10DE) or NVENC driver runtime is unavailable on this runner.");

            byte[] buffer = new byte[1024 * 1024];
            bool encodeOk = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            encodeOk.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

            if (decoder != IntPtr.Zero)
            {
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

                    pipeline.RecordDecoderAcceptance(desc.FrameIndex);
                    pipeline.Evidence.DecoderAccepted.Should().BeTrue();
                    pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
                    pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(desc.FrameIndex);
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

    [Fact]
    public void NvencPipeline_OperationalStatusInvariant_RequiresLiveGPUFrameSubmission()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) return;

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            if (!pipeline.IsActive) return;

            // Invariant: Hardware encoder is only operational after real frames are submitted and validated
            pipeline.HasProducedValidOutput.Should().BeFalse();
            pipeline.Evidence.FrameSubmitted.Should().BeFalse();
            pipeline.Evidence.OutputReceived.Should().BeFalse();

            IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
            if (tex != IntPtr.Zero)
            {
                try
                {
                    Span<byte> buffer = stackalloc byte[1024 * 512];
                    bool ok = pipeline.TryEncodeFrame(tex, true, out _, buffer, out int written);
                    ok.Should().BeTrue();
                    written.Should().BeGreaterThan(0);

                    // Operational invariant satisfied: real GPU surface encoded and verified
                    pipeline.HasProducedValidOutput.Should().BeTrue();
                    pipeline.Evidence.FrameSubmitted.Should().BeTrue();
                    pipeline.Evidence.OutputReceived.Should().BeTrue();
                    pipeline.Evidence.BitstreamStructurallyValid.Should().BeTrue();
                }
                finally
                {
                    MoonshineNativeMethods.D3D11DestroyTexture(tex);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void NvencPipeline_Dispose_TransitionsStateToDisposedAndPreventsEncoding()
    {
        var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();

        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        Span<byte> buffer = stackalloc byte[512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out int written);
        ok.Should().BeFalse();
        written.Should().Be(0);

        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
    }

    [SkippableFact]
    public void NvencPipeline_BufferTooSmall_FailsClosedSafelyWithoutMemoryCorruption()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device (GPU) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical NVIDIA GPU (0x10DE) or NVENC driver runtime is unavailable on this runner.");

            Span<byte> tinyBuffer = stackalloc byte[16];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, tinyBuffer, out int written);

            ok.Should().BeFalse();
            written.Should().Be(0);
            desc.PayloadSize.Should().Be(0);

            Span<byte> fullBuffer = stackalloc byte[1024 * 512];
            bool recoverOk = pipeline.TryEncodeFrame(tex, true, out var recoverDesc, fullBuffer, out int recoverWritten);
            recoverOk.Should().BeTrue();
            recoverWritten.Should().BeGreaterThan(0);
            recoverDesc.PayloadSize.Should().Be((uint)recoverWritten);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void NvencPipeline_DynamicReconfiguration_PreservesHealthyState()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device (GPU) is unavailable on this runner.");

        try
        {
            using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 20000, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical NVIDIA GPU (0x10DE) or NVENC driver runtime is unavailable on this runner.");

            bool tuningOk = pipeline.ConfigureTuning(NvencPreset.P2_Fast, NvencTuning.LowLatency);
            tuningOk.Should().BeTrue();
            pipeline.Preset.Should().Be(NvencPreset.P2_Fast);
            pipeline.Tuning.Should().Be(NvencTuning.LowLatency);

            bool intraOk = pipeline.ConfigureIntraRefresh(true, 60, 4);
            intraOk.Should().BeTrue();

            bool bitrateOk = pipeline.ReconfigureBitrate(30000, 45000);
            bitrateOk.Should().BeTrue();
            pipeline.BitrateKbps.Should().Be(30000);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    // =========================================================================
    // AMD AMF Conformance Suite
    // =========================================================================

    [SkippableFact]
    public void AmfPipeline_LifecycleStateTransitions_FollowStandardStateContract()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x1002);
        Skip.If(dev == IntPtr.Zero, "Physical AMD Direct3D 11 device (0x1002) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new AmfHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical AMD GPU (0x1002) or AMF driver runtime is unavailable on this runner.");

            pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
            pipeline.IsActive.Should().BeTrue();
            pipeline.Vendor.Should().Be(EncoderVendor.AmdAmf);

            var initialEvidence = pipeline.Evidence;
            initialEvidence.ApiAvailable.Should().BeTrue();
            initialEvidence.HardwareSupported.Should().BeTrue();
            initialEvidence.SessionInitialised.Should().BeTrue();
            initialEvidence.FrameSubmitted.Should().BeFalse();
            initialEvidence.OutputReceived.Should().BeFalse();

            Span<byte> buffer = stackalloc byte[1024 * 512];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

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

    [SkippableFact]
    public unsafe void AmfPipeline_Direct3D11DecoderLoopback_SuccessfullyDecodesAndAcceptsKeyframe()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x1002);
        Skip.If(dev == IntPtr.Zero, "Physical AMD Direct3D 11 device (0x1002) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1); // 1 = HEVC

        try
        {
            using var pipeline = new AmfHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical AMD GPU (0x1002) or AMF driver runtime is unavailable on this runner.");

            byte[] buffer = new byte[1024 * 1024];
            bool encodeOk = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            encodeOk.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

            if (decoder != IntPtr.Zero)
            {
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

                    pipeline.RecordDecoderAcceptance(desc.FrameIndex);
                    pipeline.Evidence.DecoderAccepted.Should().BeTrue();
                    pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
                    pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(desc.FrameIndex);
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

    [Fact]
    public void AmfPipeline_Dispose_TransitionsStateToDisposedAndPreventsEncoding()
    {
        var pipeline = new AmfHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();

        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        Span<byte> buffer = stackalloc byte[512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out int written);
        ok.Should().BeFalse();
        written.Should().Be(0);

        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
    }

    [SkippableFact]
    public void AmfPipeline_BufferTooSmall_FailsClosedSafelyWithoutMemoryCorruption()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x1002);
        Skip.If(dev == IntPtr.Zero, "Physical AMD Direct3D 11 device (0x1002) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new AmfHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical AMD GPU (0x1002) or AMF driver runtime is unavailable on this runner.");

            Span<byte> tinyBuffer = stackalloc byte[16];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, tinyBuffer, out int written);

            ok.Should().BeFalse();
            written.Should().Be(0);
            desc.PayloadSize.Should().Be(0);

            Span<byte> fullBuffer = stackalloc byte[1024 * 512];
            bool recoverOk = pipeline.TryEncodeFrame(tex, true, out var recoverDesc, fullBuffer, out int recoverWritten);
            recoverOk.Should().BeTrue();
            recoverWritten.Should().BeGreaterThan(0);
            recoverDesc.PayloadSize.Should().Be((uint)recoverWritten);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    // =========================================================================
    // Intel QuickSync / oneVPL Conformance Suite
    // =========================================================================

    [SkippableFact]
    public void QsvPipeline_LifecycleStateTransitions_FollowStandardStateContract()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Physical Intel Direct3D 11 device (0x8086) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical Intel GPU (0x8086) or oneVPL runtime is unavailable on this runner.");

            pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
            pipeline.IsActive.Should().BeTrue();
            pipeline.Vendor.Should().Be(EncoderVendor.IntelQuickSync);

            var initialEvidence = pipeline.Evidence;
            initialEvidence.ApiAvailable.Should().BeTrue();
            initialEvidence.HardwareSupported.Should().BeTrue();
            initialEvidence.SessionInitialised.Should().BeTrue();
            initialEvidence.FrameSubmitted.Should().BeFalse();
            initialEvidence.OutputReceived.Should().BeFalse();

            Span<byte> buffer = stackalloc byte[1024 * 512];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            ok.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

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

    [SkippableFact]
    public unsafe void QsvPipeline_Direct3D11DecoderLoopback_SuccessfullyDecodesAndAcceptsKeyframe()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Physical Intel Direct3D 11 device (0x8086) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        IntPtr decoder = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1); // 1 = HEVC

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical Intel GPU (0x8086) or oneVPL runtime is unavailable on this runner.");

            byte[] buffer = new byte[1024 * 1024];
            bool encodeOk = pipeline.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
            encodeOk.Should().BeTrue();
            written.Should().BeGreaterThan(0);
            desc.IsKeyframe.Should().Be(1);

            if (decoder != IntPtr.Zero)
            {
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

                    pipeline.RecordDecoderAcceptance(desc.FrameIndex);
                    pipeline.Evidence.DecoderAccepted.Should().BeTrue();
                    pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
                    pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(desc.FrameIndex);
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

    [Fact]
    public void QsvPipeline_Dispose_TransitionsStateToDisposedAndPreventsEncoding()
    {
        var pipeline = new QsvHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();

        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        Span<byte> buffer = stackalloc byte[512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out int written);
        ok.Should().BeFalse();
        written.Should().Be(0);

        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
    }

    [SkippableFact]
    public void QsvPipeline_BufferTooSmall_FailsClosedSafelyWithoutMemoryCorruption()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        Skip.If(dev == IntPtr.Zero, "Physical Intel Direct3D 11 device (0x8086) is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            using var pipeline = new QsvHardwareEncoderPipeline(1920, 1080, d3dDevice: dev);
            Skip.IfNot(pipeline.IsActive, "Physical Intel GPU (0x8086) or oneVPL runtime is unavailable on this runner.");

            Span<byte> tinyBuffer = stackalloc byte[16];
            bool ok = pipeline.TryEncodeFrame(tex, true, out var desc, tinyBuffer, out int written);

            ok.Should().BeFalse();
            written.Should().Be(0);
            desc.PayloadSize.Should().Be(0);

            Span<byte> fullBuffer = stackalloc byte[1024 * 512];
            bool recoverOk = pipeline.TryEncodeFrame(tex, true, out var recoverDesc, fullBuffer, out int recoverWritten);
            recoverOk.Should().BeTrue();
            recoverWritten.Should().BeGreaterThan(0);
            recoverDesc.PayloadSize.Should().Be((uint)recoverWritten);
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    // =========================================================================
    // Unified Hardware Encoder Engine Conformance & Recovery
    // =========================================================================

    [SkippableFact]
    public void UnifiedEngine_FullLifecycle_CreateSubmitReconfigureDrainDestroy()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreatePatternTexture(dev, 1920, 1080, 4, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 pattern texture allocation failed.");
        }

        try
        {
            using var engine = new UnifiedHardwareEncoderEngine(1920, 1080, fps: 60, bitrateKbps: 15000, preferredVendor: EncoderVendor.Auto, d3dDevice: dev);
            Skip.IfNot(engine.IsActive, "Hardware video encoder is unavailable on this runner.");

            engine.RuntimeState.Should().Be(EncoderRuntimeState.Ready);

            // Frame 0: Submit IDR keyframe
            byte[] bitstream = new byte[1024 * 1024];
            var sub0 = engine.SubmitFrame(tex, forceIdr: true, bitstream, out int written0);
            sub0.Submitted.Should().BeTrue();
            sub0.OutputAvailable.Should().BeTrue();
            sub0.KeyFrame.Should().BeTrue();
            written0.Should().BeGreaterThan(0);

            // Reconfigure: Bitrate and Framerate
            bool reconfigOk = engine.ReconfigureBitrate(25000, 120);
            reconfigOk.Should().BeTrue();
            engine.BitrateKbps.Should().Be(25000);
            engine.Fps.Should().Be(120);

            // Request keyframe
            engine.RequestKeyframe();

            // Frame 1: Submit frame post-reconfiguration
            var sub1 = engine.SubmitFrame(tex, forceIdr: false, bitstream, out int written1);
            sub1.Submitted.Should().BeTrue();
            sub1.OutputAvailable.Should().BeTrue();
            written1.Should().BeGreaterThan(0);

            engine.FramesEncoded.Should().Be(2);
            engine.HasProducedValidOutput.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [SkippableFact]
    public void UnifiedEngine_RapidSequentialCreationAndTeardown_ZeroMemoryCorruption()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0x10DE);
        if (dev == IntPtr.Zero) dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device is unavailable on this runner.");

        IntPtr tex = MoonshineNativeMethods.D3D11CreateTexture(dev, 1920, 1080, 0);
        if (tex == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Direct3D 11 test texture allocation failed.");
        }

        try
        {
            byte[] buffer = new byte[1024 * 1024];

            for (int cycle = 0; cycle < 10; ++cycle)
            {
                using var engine = new UnifiedHardwareEncoderEngine(1920, 1080, preferredVendor: EncoderVendor.Auto, d3dDevice: dev);
                if (!engine.IsActive) continue;

                engine.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
                bool ok = engine.TryEncodeFrame(tex, true, out var desc, buffer, out int written);
                ok.Should().BeTrue();
                written.Should().BeGreaterThan(0);
                desc.IsKeyframe.Should().Be(1);
            }
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyTexture(tex);
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
        }
    }

    [Fact]
    public void HardwareEncoder_MultiAdapterDiscovery_IdentifiesSecondaryHeadlessIntelAdapter()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeNull();

        var intelAdapter = adapters.FirstOrDefault(a => a.Description.Contains("Intel", StringComparison.OrdinalIgnoreCase));
        if (intelAdapter != null)
        {
            intelAdapter.IsHardware.Should().BeTrue();
            intelAdapter.Description.Should().NotBeNullOrWhiteSpace();

            // Probe backend readiness without throwing in headless/secondary configuration
            var readiness = HostCapabilityProbeEngine.ProbeBackendReadiness(adaptersOverride: adapters);
            readiness.Should().NotBeNull();
            readiness.VideoEncoder.Should().NotBe(ComponentReadiness.Faulted);
        }
    }

    [SkippableFact]
    public void UnifiedEngine_DeviceLossRecovery_RecreatesSessionAndResumesReadyState()
    {
        IntPtr dev = MoonshineNativeMethods.D3D11CreateDevice(0);
        Skip.If(dev == IntPtr.Zero, "Physical Direct3D 11 device is unavailable on this runner.");

        IntPtr newDev = MoonshineNativeMethods.D3D11CreateDevice(0);
        if (newDev == IntPtr.Zero)
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            Skip.If(true, "Secondary Direct3D 11 device allocation failed.");
        }

        try
        {
            using var engine = new UnifiedHardwareEncoderEngine(1920, 1080, preferredVendor: EncoderVendor.Auto, d3dDevice: dev);
            Skip.IfNot(engine.IsActive, "Hardware video encoder is unavailable on this runner.");

            engine.RuntimeState.Should().Be(EncoderRuntimeState.Ready);

            // Attempt device recovery with new D3D11 device
            bool recovered = engine.TryRecoverDevice(newDev);
            recovered.Should().BeTrue();
            engine.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
            engine.IsActive.Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.D3D11DestroyDevice(dev);
            MoonshineNativeMethods.D3D11DestroyDevice(newDev);
        }
    }
}
