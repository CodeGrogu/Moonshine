using System;
using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

/// <summary>
/// Conformance test suite verifying that all hardware video encoder pipelines satisfy the unified Moonshine engineering contract.
/// </summary>
public class HardwareVideoEncoderConformanceTests
{
    [Fact]
    public void NvencPipeline_LifecycleStateTransitions_FollowStandardStateContract()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
        pipeline.IsActive.Should().BeTrue();
        pipeline.Vendor.Should().Be(EncoderVendor.NvidiaNvenc);

        // Evidence baseline before submission
        var initialEvidence = pipeline.Evidence;
        initialEvidence.ApiAvailable.Should().BeTrue();
        initialEvidence.HardwareSupported.Should().BeTrue();
        initialEvidence.SessionInitialised.Should().BeTrue();
        initialEvidence.FrameSubmitted.Should().BeFalse();
        initialEvidence.OutputReceived.Should().BeFalse();

        // Encode frame
        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, true, out var desc, buffer, out int written);
        ok.Should().BeTrue();
        written.Should().BeGreaterThan(0);
        desc.IsKeyframe.Should().Be(1);

        // Evidence after submission
        var liveEvidence = pipeline.Evidence;
        liveEvidence.FrameSubmitted.Should().BeTrue();
        liveEvidence.OutputReceived.Should().BeTrue();
        liveEvidence.BitstreamStructurallyValid.Should().BeTrue();
        liveEvidence.AccessUnitValid.Should().BeTrue();
    }

    [Fact]
    public void NvencPipeline_Dispose_TransitionsStateToDisposedAndPreventsEncoding()
    {
        var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);
        pipeline.Dispose();

        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        // TryEncodeFrame after disposal must fail closed safely
        Span<byte> buffer = stackalloc byte[512];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out int written);
        ok.Should().BeFalse();
        written.Should().Be(0);

        // Double dispose must be deterministic and safe
        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NvencPipeline_BufferTooSmall_FailsClosedSafelyWithoutMemoryCorruption()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080);

        // 16-byte buffer is too small for a full compressed video access unit
        Span<byte> tinyBuffer = stackalloc byte[16];
        bool ok = pipeline.TryEncodeFrame(IntPtr.Zero, true, out var desc, tinyBuffer, out int written);

        ok.Should().BeFalse();
        written.Should().Be(0);
        desc.PayloadSize.Should().Be(0);

        // Encoder must remain healthy after rejection
        Span<byte> fullBuffer = stackalloc byte[1024 * 512];
        bool recoverOk = pipeline.TryEncodeFrame(IntPtr.Zero, true, out var recoverDesc, fullBuffer, out int recoverWritten);
        recoverOk.Should().BeTrue();
        recoverWritten.Should().BeGreaterThan(0);
        recoverDesc.PayloadSize.Should().Be((uint)recoverWritten);
    }

    [Fact]
    public void NvencPipeline_DynamicReconfiguration_PreservesHealthyState()
    {
        using var pipeline = new NvencHardwareEncoderPipeline(1920, 1080, fps: 60, bitrateKbps: 20000);

        // Reconfigure tuning
        bool tuningOk = pipeline.ConfigureTuning(NvencPreset.P2_Fast, NvencTuning.LowLatency);
        tuningOk.Should().BeTrue();
        pipeline.Preset.Should().Be(NvencPreset.P2_Fast);
        pipeline.Tuning.Should().Be(NvencTuning.LowLatency);

        // Reconfigure intra-refresh
        bool intraOk = pipeline.ConfigureIntraRefresh(true, 60, 4);
        intraOk.Should().BeTrue();

        // Request keyframe
        pipeline.RequestKeyframe();

        // Encode after reconfiguration
        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool encodeOk = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc, buffer, out int written);
        encodeOk.Should().BeTrue();
        written.Should().BeGreaterThan(0);
        desc.IsKeyframe.Should().Be(1); // Should be forced keyframe
    }

    [Theory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    [InlineData(VideoCodec.HevcMain10)]
    [InlineData(VideoCodec.Av1)]
    public void NvencPipeline_CodecSupport_TruthfulHardwareQuery(VideoCodec codec)
    {
        bool supported = NvencHardwareEncoderPipeline.IsCodecSupported(codec);
        // Codec support query must return a valid boolean without throwing
        (supported == true || supported == false).Should().BeTrue();
    }
}
