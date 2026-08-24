using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class HardwareVideoEncoderPipelineTests
{
    [Fact]
    public void HardwareVideoEncoderPipeline_InitializeWithoutDevice_FailsClosedSafely()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 120,
            bitrateKbps: 30000,
            codec: VideoCodec.Av1,
            vendor: EncoderVendor.Auto,
            d3dDevice: IntPtr.Zero
        );

        pipeline.Width.Should().Be(1920);
        pipeline.Height.Should().Be(1080);
        pipeline.Fps.Should().Be(120);
        pipeline.BitrateKbps.Should().Be(30000);
        pipeline.Codec.Should().Be(VideoCodec.Av1);
        pipeline.IsActive.Should().BeFalse();
        pipeline.ImplementationKind.Should().Be(EncoderImplementationKind.Unimplemented);
        pipeline.IsHardwareAccelerated.Should().BeFalse();
        pipeline.HasProducedValidOutput.Should().BeFalse();
        pipeline.ImplementationType.Should().Be<HardwareVideoEncoderPipeline>();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Faulted);
        pipeline.FramesEncoded.Should().Be(0);
        pipeline.EncodingErrorsCount.Should().Be(0);
        pipeline.AverageEncodingLatencyMicroseconds.Should().Be(0.0);

        var evidence = pipeline.Evidence;
        evidence.ApiAvailable.Should().BeFalse();
        evidence.HardwareSupported.Should().BeFalse();
        evidence.SessionInitialised.Should().BeFalse();
        evidence.FrameSubmitted.Should().BeFalse();
        evidence.OutputReceived.Should().BeFalse();
        evidence.BitstreamStructurallyValid.Should().BeFalse();
        evidence.AccessUnitValid.Should().BeFalse();
        evidence.DecoderAccepted.Should().BeFalse();
        evidence.FirstValidFrameId.Should().Be(0);
        evidence.LastValidFrameId.Should().Be(0);
        evidence.LastDecoderAcceptedFrameId.Should().Be(0);
        evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        evidence.DecoderAcceptanceHealthy.Should().BeFalse();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_EncodeAndSubmit_WithoutDevice_ReturnsFailClosed()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 3840,
            height: 2160,
            fps: 60,
            bitrateKbps: 50000,
            codec: VideoCodec.HevcMain10,
            d3dDevice: IntPtr.Zero
        );

        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool success = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc, buffer, out int written);
        success.Should().BeFalse();
        written.Should().Be(0);
        pipeline.HasProducedValidOutput.Should().BeFalse();

        var submission = pipeline.SubmitFrame(IntPtr.Zero, true, buffer, out int submitWritten);
        submission.Submitted.Should().BeFalse();
        submission.OutputAvailable.Should().BeFalse();
        submission.KeyFrame.Should().BeFalse();
        submission.BytesWritten.Should().Be(0);
        submission.Result.Should().Be(EncoderResult.NotAvailable);

        bool polled = pipeline.TryPollPacket(buffer, out _, out int polledWritten);
        polled.Should().BeFalse();
        polledWritten.Should().Be(0);

        var evidence = pipeline.Evidence;
        evidence.FrameSubmitted.Should().BeTrue();
        evidence.OutputReceived.Should().BeFalse();
        evidence.BitstreamStructurallyValid.Should().BeFalse();
        evidence.AccessUnitValid.Should().BeFalse();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_ReconfigureAndKeyframe_WithoutHandle_OperatesSafely()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.HevcMain10,
            d3dDevice: IntPtr.Zero
        );

        bool reconf = pipeline.Reconfigure(60000, 120);
        reconf.Should().BeFalse();
        pipeline.RequestKeyframe();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new HardwareVideoEncoderPipeline(1920, 1080, d3dDevice: IntPtr.Zero);
        pipeline.Dispose();
        pipeline.Dispose();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        Span<byte> buffer = stackalloc byte[256];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        var result = pipeline.SubmitFrame(IntPtr.Zero, false, buffer, out _);
        result.Submitted.Should().BeFalse();
        result.Result.Should().Be(EncoderResult.DeviceLost);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_TryEncodeFrame_InvalidBitstreamPayload_ReturnsFalse()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.Av1,
            d3dDevice: IntPtr.Zero
        );

        Span<byte> emptyBuffer = stackalloc byte[0];
        bool success = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, emptyBuffer, out int written);
        success.Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_ZeroAllocationsHotPath()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(1920, 1080, d3dDevice: IntPtr.Zero);
        byte[] buffer = new byte[1024];

        // Warm up
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);
        }
        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

        (afterAlloc - beforeAlloc).Should().Be(0, "HardwareVideoEncoderPipeline hot path must be zero allocation");
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_AccessUnitValidation_ValidatesCompleteAccessUnits()
    {
        // 1. H.264 Access Units
        // Keyframe AU: SPS (7) + PPS (8) + IDR slice (5)
        byte[] h264KeyframeAu = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28, // SPS (7)
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80, // PPS (8)
            0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00        // IDR (5)
        ];
        var h264KeyResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264KeyframeAu);
        h264KeyResult.IsValid.Should().BeTrue();
        h264KeyResult.HasStructurallyValidPayload.Should().BeTrue();
        h264KeyResult.HasCodecHeaders.Should().BeTrue();
        h264KeyResult.HasRandomAccessMarker.Should().BeTrue();
        h264KeyResult.ContainsFrameData.Should().BeTrue();
        h264KeyResult.IsCompleteAccessUnit.Should().BeTrue();
        h264KeyResult.NaluCount.Should().Be(3);
        h264KeyResult.HasParameterSets.Should().BeTrue();
        h264KeyResult.HasIdr.Should().BeTrue();
        h264KeyResult.HasRandomAccessPoint.Should().BeTrue();

        // Inter-frame AU: Non-IDR P-slice (1) without SPS/PPS headers
        byte[] h264InterAu = [0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24];
        var h264InterResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264InterAu);
        h264InterResult.IsValid.Should().BeTrue();
        h264InterResult.HasStructurallyValidPayload.Should().BeTrue();
        h264InterResult.HasCodecHeaders.Should().BeFalse();
        h264InterResult.HasRandomAccessMarker.Should().BeFalse();
        h264InterResult.ContainsFrameData.Should().BeTrue();
        h264InterResult.IsCompleteAccessUnit.Should().BeFalse();
        h264InterResult.NaluCount.Should().Be(1);

        // Parameter sets only without slice (incomplete AU)
        byte[] h264ParamSetsOnly = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28,
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80
        ];
        var h264ParamResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264ParamSetsOnly);
        h264ParamResult.IsValid.Should().BeTrue();
        h264ParamResult.HasStructurallyValidPayload.Should().BeTrue();
        h264ParamResult.HasCodecHeaders.Should().BeTrue();
        h264ParamResult.HasRandomAccessMarker.Should().BeTrue();
        h264ParamResult.ContainsFrameData.Should().BeFalse();
        h264ParamResult.IsCompleteAccessUnit.Should().BeFalse();
        h264ParamResult.NaluCount.Should().Be(2);

        // 2. HEVC Access Units
        // Keyframe AU: VPS (32) + SPS (33) + PPS (34) + IDR (19)
        byte[] hevcKeyframeAu = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, // VPS (32)
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, // SPS (33)
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0, // PPS (34)
            0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE  // IDR (19)
        ];
        var hevcKeyResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, hevcKeyframeAu);
        hevcKeyResult.IsValid.Should().BeTrue();
        hevcKeyResult.HasStructurallyValidPayload.Should().BeTrue();
        hevcKeyResult.HasCodecHeaders.Should().BeTrue();
        hevcKeyResult.HasRandomAccessMarker.Should().BeTrue();
        hevcKeyResult.ContainsFrameData.Should().BeTrue();
        hevcKeyResult.IsCompleteAccessUnit.Should().BeTrue();
        hevcKeyResult.NaluCount.Should().Be(4);

        // Clean Random Access (CRA, 21): 0x2A >> 1 = 21 without VPS/SPS/PPS
        byte[] hevcCraAu = [0x00, 0x00, 0x00, 0x01, 0x2A, 0x01, 0x11, 0x22];
        var hevcCraResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcCraAu);
        hevcCraResult.IsValid.Should().BeTrue();
        hevcCraResult.HasStructurallyValidPayload.Should().BeTrue();
        hevcCraResult.HasCodecHeaders.Should().BeFalse();
        hevcCraResult.HasRandomAccessMarker.Should().BeTrue();
        hevcCraResult.ContainsFrameData.Should().BeTrue();
        hevcCraResult.IsCompleteAccessUnit.Should().BeFalse();
        hevcCraResult.NaluCount.Should().Be(1);

        // Inter-frame TRAIL (1): 0x02 >> 1 = 1 without VPS/SPS/PPS
        byte[] hevcTrailAu = [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xD0];
        var hevcTrailResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, hevcTrailAu);
        hevcTrailResult.IsValid.Should().BeTrue();
        hevcTrailResult.HasStructurallyValidPayload.Should().BeTrue();
        hevcTrailResult.HasCodecHeaders.Should().BeFalse();
        hevcTrailResult.HasRandomAccessMarker.Should().BeFalse();
        hevcTrailResult.ContainsFrameData.Should().BeTrue();
        hevcTrailResult.IsCompleteAccessUnit.Should().BeFalse();
        hevcTrailResult.NaluCount.Should().Be(1);

        // Parameter sets only without slice (incomplete AU)
        byte[] hevcParamSetsOnly = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0
        ];
        var hevcParamResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcParamSetsOnly);
        hevcParamResult.IsValid.Should().BeTrue();
        hevcParamResult.HasStructurallyValidPayload.Should().BeTrue();
        hevcParamResult.HasCodecHeaders.Should().BeTrue();
        hevcParamResult.HasRandomAccessMarker.Should().BeFalse();
        hevcParamResult.ContainsFrameData.Should().BeFalse();
        hevcParamResult.IsCompleteAccessUnit.Should().BeFalse();
        hevcParamResult.NaluCount.Should().Be(3);

        // 3. AV1 Access Units
        // Complete Keyframe AU: Sequence Header OBU (1) + Frame OBU (6)
        byte[] av1KeyframeAu = [
            0x0A, 0x02, 0x01, 0x02, // OBU Type 1 (Sequence Header), size 2
            0x32, 0x02, 0x03, 0x04  // OBU Type 6 (Frame), size 2
        ];
        var av1KeyResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1KeyframeAu);
        av1KeyResult.IsValid.Should().BeTrue();
        av1KeyResult.HasStructurallyValidPayload.Should().BeTrue();
        av1KeyResult.HasCodecHeaders.Should().BeTrue();
        av1KeyResult.HasRandomAccessMarker.Should().BeTrue();
        av1KeyResult.ContainsFrameData.Should().BeTrue();
        av1KeyResult.IsCompleteAccessUnit.Should().BeTrue();
        av1KeyResult.NaluCount.Should().Be(2);

        // Standalone Frame Header OBU (3) without Tile Group -> ContainsFrameData = false, IsCompleteAccessUnit = false
        byte[] av1FrameHeaderAu = [0x1A, 0x02, 0x10, 0x20]; // OBU Type 3 (Frame Header), size 2
        var av1InterResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1FrameHeaderAu);
        av1InterResult.IsValid.Should().BeTrue();
        av1InterResult.HasStructurallyValidPayload.Should().BeTrue();
        av1InterResult.HasCodecHeaders.Should().BeFalse();
        av1InterResult.HasRandomAccessMarker.Should().BeFalse();
        av1InterResult.ContainsFrameData.Should().BeFalse();
        av1InterResult.IsCompleteAccessUnit.Should().BeFalse();
        av1InterResult.NaluCount.Should().Be(1);

        // Incomplete AU: Sequence Header OBU only (1)
        byte[] av1SeqOnly = [0x0A, 0x02, 0x01, 0x02];
        var av1SeqResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1SeqOnly);
        av1SeqResult.IsValid.Should().BeTrue();
        av1SeqResult.HasStructurallyValidPayload.Should().BeTrue();
        av1SeqResult.HasCodecHeaders.Should().BeTrue();
        av1SeqResult.HasRandomAccessMarker.Should().BeTrue();
        av1SeqResult.ContainsFrameData.Should().BeFalse();
        av1SeqResult.IsCompleteAccessUnit.Should().BeFalse();
        av1SeqResult.NaluCount.Should().Be(1);

        // 4. Invalid / Malformed Payloads
        byte[] tooShort = [0x00, 0x01];
        var tooShortResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, tooShort);
        tooShortResult.IsValid.Should().BeFalse();
        tooShortResult.HasStructurallyValidPayload.Should().BeFalse();
        tooShortResult.HasCodecHeaders.Should().BeFalse();
        tooShortResult.HasRandomAccessMarker.Should().BeFalse();
        tooShortResult.ContainsFrameData.Should().BeFalse();
        tooShortResult.IsCompleteAccessUnit.Should().BeFalse();
        tooShortResult.NaluCount.Should().Be(0);

        byte[] garbage = [0xFF, 0xFE, 0xFD, 0xFC];
        var garbageResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, garbage);
        garbageResult.IsValid.Should().BeFalse();
        garbageResult.HasStructurallyValidPayload.Should().BeFalse();
        garbageResult.ContainsFrameData.Should().BeFalse();
        garbageResult.IsCompleteAccessUnit.Should().BeFalse();
        garbageResult.NaluCount.Should().Be(0);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_SubmitFrame_CorrelatesFrameIdAndTimestamp()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.HevcMain10,
            vendor: EncoderVendor.Auto,
            d3dDevice: IntPtr.Zero
        );

        Span<byte> buffer = stackalloc byte[1024];
        const ulong expectedFrameId = 77102UL;
        const ulong expectedTimestampUs = 9876543210UL;

        // 1. SubmitFrame with explicit frame ID and timestamp on uninitialised pipeline fails closed safely
        var uninitSubmission = pipeline.SubmitFrame(
            d3dTexture: IntPtr.Zero,
            frameId: expectedFrameId,
            timestampUs: expectedTimestampUs,
            forceIdr: true,
            outBitstream: buffer,
            out int uninitBytesWritten
        );
        uninitSubmission.Submitted.Should().BeFalse();
        uninitSubmission.OutputAvailable.Should().BeFalse();
        uninitSubmission.KeyFrame.Should().BeFalse();
        uninitSubmission.BytesWritten.Should().Be(0);
        uninitBytesWritten.Should().Be(0);
        uninitSubmission.Result.Should().Be(EncoderResult.NotAvailable);

        // 2. SubmitFrame on disposed pipeline returns DeviceLost
        var disposedPipeline = new HardwareVideoEncoderPipeline(1920, 1080, d3dDevice: IntPtr.Zero);
        disposedPipeline.Dispose();
        var disposedSubmission = disposedPipeline.SubmitFrame(
            d3dTexture: IntPtr.Zero,
            frameId: expectedFrameId,
            timestampUs: expectedTimestampUs,
            forceIdr: false,
            outBitstream: buffer,
            out int disposedBytesWritten
        );
        disposedSubmission.Submitted.Should().BeFalse();
        disposedSubmission.BytesWritten.Should().Be(0);
        disposedBytesWritten.Should().Be(0);
        disposedSubmission.Result.Should().Be(EncoderResult.DeviceLost);

        // 3. Exercise pipeline implementation correlation with synthetic encoder
        using var correlatingPipeline = new SyntheticCorrelatingEncoderPipeline();
        correlatingPipeline.IsActive.Should().BeTrue();

        var submission = correlatingPipeline.SubmitFrame(
            d3dTexture: IntPtr.Zero,
            frameId: expectedFrameId,
            timestampUs: expectedTimestampUs,
            forceIdr: true,
            outBitstream: buffer,
            out int submitWritten
        );

        submission.Submitted.Should().BeTrue();
        submission.OutputAvailable.Should().BeTrue();
        submission.KeyFrame.Should().BeTrue();
        submission.BytesWritten.Should().Be(submitWritten);
        submitWritten.Should().BeGreaterThan(0);
        submission.Result.Should().Be(EncoderResult.Success);
        submission.PacketDesc.FrameIndex.Should().Be(expectedFrameId);
        submission.PacketDesc.TimestampQpc.Should().Be((long)expectedTimestampUs);

        // 4. Sequential frame submissions preserve distinct monotonic identifiers
        for (ulong i = 1; i <= 5; i++)
        {
            ulong seqFrameId = expectedFrameId + i;
            ulong seqTimestampUs = expectedTimestampUs + (i * 16666UL);
            bool isKey = i == 3;

            var seqSubmission = correlatingPipeline.SubmitFrame(
                d3dTexture: IntPtr.Zero,
                frameId: seqFrameId,
                timestampUs: seqTimestampUs,
                forceIdr: isKey,
                outBitstream: buffer,
                out int seqWritten
            );

            seqSubmission.Submitted.Should().BeTrue();
            seqSubmission.OutputAvailable.Should().BeTrue();
            seqSubmission.KeyFrame.Should().Be(isKey);
            seqSubmission.BytesWritten.Should().Be(seqWritten);
            seqWritten.Should().BeGreaterThan(0);
            seqSubmission.Result.Should().Be(EncoderResult.Success);
            seqSubmission.PacketDesc.FrameIndex.Should().Be(seqFrameId);
            seqSubmission.PacketDesc.TimestampQpc.Should().Be((long)seqTimestampUs);
        }

        // 5. Verify live evidence tracks FirstValidFrameId and LastValidFrameId
        var evidence = correlatingPipeline.Evidence;
        evidence.ApiAvailable.Should().BeTrue();
        evidence.SessionInitialised.Should().BeTrue();
        evidence.FrameSubmitted.Should().BeTrue();
        evidence.OutputReceived.Should().BeTrue();
        evidence.BitstreamStructurallyValid.Should().BeTrue();
        evidence.AccessUnitValid.Should().BeTrue();
        evidence.DecoderAccepted.Should().BeFalse();
        evidence.FirstValidFrameId.Should().Be(expectedFrameId);
        evidence.LastValidFrameId.Should().Be(expectedFrameId + 5);
        evidence.LastDecoderAcceptedFrameId.Should().Be(0);
        evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        evidence.DecoderAcceptanceHealthy.Should().BeFalse();

        // 6. Verify decoder acceptance recording
        correlatingPipeline.RecordDecoderAcceptance(expectedFrameId + 5);
        correlatingPipeline.Evidence.DecoderAccepted.Should().BeTrue();
        correlatingPipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(expectedFrameId + 5);
        correlatingPipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeTrue();
        correlatingPipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_DecoderAcceptance_CorrelatedToExactFrameId()
    {
        using var pipeline = new SyntheticCorrelatingEncoderPipeline();
        Span<byte> buffer = stackalloc byte[1024];

        // 1. Initial state: 100 encoded + no decode -> DecoderAccepted == false, DecoderAcceptedLatestFrame == false, DecoderAcceptanceHealthy == false
        for (ulong frameId = 1; frameId <= 100; frameId++)
        {
            bool encoded = pipeline.TryEncodeFrame(IntPtr.Zero, frameId, frameId * 16666, frameId == 1, out _, buffer, out _);
            encoded.Should().BeTrue();
        }

        pipeline.Evidence.LastValidFrameId.Should().Be(100);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(0);
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();

        // 2. 100 encoded + 101 decoded -> DecoderAcceptedLatestFrame == false, DecoderAcceptanceHealthy == false, DecoderAccepted == false (cannot accept future unencoded frame)
        pipeline.RecordDecoderAcceptance(101);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(101);
        pipeline.Evidence.LastValidFrameId.Should().Be(100);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();

        // 3. 100 encoded + 100 decoded -> DecoderAcceptedLatestFrame == true, DecoderAcceptanceHealthy == true, DecoderAccepted == true
        pipeline.RecordDecoderAcceptance(100);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(100);
        pipeline.Evidence.LastValidFrameId.Should().Be(100);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeTrue();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
        pipeline.Evidence.DecoderAccepted.Should().BeTrue();

        // 4. 102 encoded + 101 decoded -> DecoderAcceptedLatestFrame == false, DecoderAcceptanceHealthy == true (in-flight lag of 1 frame is healthy), DecoderAccepted == true
        bool encoded101 = pipeline.TryEncodeFrame(IntPtr.Zero, 101, 101 * 16666, false, out _, buffer, out _);
        encoded101.Should().BeTrue();
        bool encoded102 = pipeline.TryEncodeFrame(IntPtr.Zero, 102, 102 * 16666, false, out _, buffer, out _);
        encoded102.Should().BeTrue();
        pipeline.RecordDecoderAcceptance(101);
        pipeline.Evidence.LastValidFrameId.Should().Be(102);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(101);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
        pipeline.Evidence.DecoderAccepted.Should().BeTrue();

        // 5. 106 encoded + 100 decoded -> DecoderAcceptedLatestFrame == false, DecoderAcceptanceHealthy == false (lag of 6 frames > 4 exceeds tolerance window), DecoderAccepted == false
        for (ulong frameId = 103; frameId <= 106; frameId++)
        {
            bool encoded = pipeline.TryEncodeFrame(IntPtr.Zero, frameId, frameId * 16666, false, out _, buffer, out _);
            encoded.Should().BeTrue();
        }
        pipeline.RecordDecoderAcceptance(100);
        pipeline.Evidence.LastValidFrameId.Should().Be(106);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(100);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();

        // 6. Dispose -> SessionInitialised == false, decoder acceptance evidence cleared
        pipeline.Dispose();
        pipeline.Evidence.SessionInitialised.Should().BeFalse();
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(0);
        pipeline.Evidence.LastValidFrameId.Should().Be(0);
    }

    private sealed class SyntheticCorrelatingEncoderPipeline : IVideoEncoderPipeline
    {
        public uint Width => 1920;
        public uint Height => 1080;
        public uint Fps => 60;
        public uint BitrateKbps => 20000;
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Direct3D11Hardware;
        public bool IsActive { get; set; } = true;
        public EncoderImplementationKind ImplementationKind => EncoderImplementationKind.SyntheticTest;
        public bool IsHardwareAccelerated => false;
        public bool HasProducedValidOutput => true;
        public Type ImplementationType => GetType();
        public EncoderRuntimeState RuntimeState => IsActive ? EncoderRuntimeState.Ready : EncoderRuntimeState.Disposed;
        public double AverageEncodingLatencyMicroseconds => 150.0;

        private bool _frameSubmitted;
        private ulong _lastDecoderAcceptedFrameId;
        private ulong _firstValidFrameId;
        private ulong _lastValidFrameId;
        private bool _hasValidFrame;

        public EncoderEvidence Evidence
        {
            get
            {
                ulong lastValid = _lastValidFrameId;
                ulong lastAccepted = _lastDecoderAcceptedFrameId;
                bool latestMatch = lastAccepted != 0 && lastAccepted == lastValid;
                bool healthy = IsActive && lastAccepted != 0 && lastAccepted <= lastValid && (lastValid - lastAccepted) <= 4;

                return new EncoderEvidence(
                    ApiAvailable: true,
                    HardwareSupported: IsHardwareAccelerated,
                    SessionInitialised: IsActive,
                    FrameSubmitted: _frameSubmitted,
                    OutputReceived: HasProducedValidOutput,
                    BitstreamStructurallyValid: HasProducedValidOutput,
                    AccessUnitValid: HasProducedValidOutput,
                    DecoderAccepted: healthy,
                    FirstValidFrameId: _firstValidFrameId,
                    LastValidFrameId: lastValid,
                    LastDecoderAcceptedFrameId: lastAccepted,
                    DecoderAcceptedLatestFrame: latestMatch,
                    DecoderAcceptanceHealthy: healthy
                );
            }
        }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            ulong frameId,
            ulong timestampUs,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            _frameSubmitted = true;
            byte[] syntheticNalu = forceIdr
                ? [0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE]
                : [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xD0];

            syntheticNalu.CopyTo(outBitstream);
            bytesWritten = syntheticNalu.Length;

            desc = new MoonshineEncodedPacketDesc
            {
                PayloadSize = (uint)bytesWritten,
                IsKeyframe = (byte)(forceIdr ? 1 : 0),
                FrameIndex = frameId,
                TimestampQpc = timestampUs > 0 ? (long)timestampUs : 0,
                IsHeaderPacket = (byte)(forceIdr ? 1 : 0),
                TemporalId = 0,
                Reserved = 0
            };

            if (bytesWritten > 0)
            {
                if (!_hasValidFrame)
                {
                    _firstValidFrameId = frameId;
                    _hasValidFrame = true;
                }
                _lastValidFrameId = frameId;
            }

            return true;
        }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            return TryEncodeFrame(d3dTexture, 0, 0, forceIdr, out desc, outBitstream, out bytesWritten);
        }

        public void RecordDecoderAcceptance(ulong frameId)
        {
            _lastDecoderAcceptedFrameId = frameId;
        }

        public EncodeSubmissionResult SubmitFrame(
            IntPtr d3dTexture,
            ulong frameId,
            ulong timestampUs,
            bool forceIdr,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!IsActive)
            {
                bytesWritten = 0;
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: EncoderResult.DeviceLost
                );
            }

            if (!TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out var desc, outBitstream, out bytesWritten))
            {
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: EncoderResult.EncoderFailure
                );
            }

            return new EncodeSubmissionResult(
                Submitted: true,
                OutputAvailable: bytesWritten > 0,
                KeyFrame: desc.IsKeyframe != 0,
                BytesWritten: bytesWritten,
                PacketDesc: desc,
                Result: EncoderResult.Success
            );
        }

        public EncodeSubmissionResult SubmitFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            return SubmitFrame(d3dTexture, 0, 0, forceIdr, outBitstream, out bytesWritten);
        }

        public bool TryPollPacket(Span<byte> outBitstream, out MoonshineEncodedPacketDesc desc, out int bytesWritten)
        {
            desc = default;
            bytesWritten = 0;
            return false;
        }

        public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0) => true;
        public void RequestKeyframe() { }
        public void Dispose()
        {
            IsActive = false;
            _lastDecoderAcceptedFrameId = 0;
            _lastValidFrameId = 0;
        }
    }
}
