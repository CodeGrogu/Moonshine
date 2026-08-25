using System;
using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Conformance test suite verifying that all hardware video encoder pipelines (NVENC, AMF, QSV)
/// satisfy the unified Moonshine engineering contract and Direct3D 11 decoder loopback.
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
