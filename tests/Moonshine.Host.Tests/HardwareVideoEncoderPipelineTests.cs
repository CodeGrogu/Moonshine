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
        byte[] av1FrameHeaderAu = [0x1A, 0x02, 0x20, 0x20]; // OBU Type 3 (Frame Header), size 2 (InterFrame: frame_type=1)
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
            d3dTexture: (IntPtr)0x1000,
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
                d3dTexture: (IntPtr)0x1000,
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
        IntPtr mockTex = (IntPtr)0x1000;
        for (ulong frameId = 1; frameId <= 100; frameId++)
        {
            bool encoded = pipeline.TryEncodeFrame(mockTex, frameId, frameId * 16666, frameId == 1, out _, buffer, out _);
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
        bool encoded101 = pipeline.TryEncodeFrame(mockTex, 101, 101 * 16666, false, out _, buffer, out _);
        encoded101.Should().BeTrue();
        bool encoded102 = pipeline.TryEncodeFrame(mockTex, 102, 102 * 16666, false, out _, buffer, out _);
        encoded102.Should().BeTrue();
        pipeline.RecordDecoderAcceptance(101);
        pipeline.Evidence.LastValidFrameId.Should().Be(102);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(101);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
        pipeline.Evidence.DecoderAccepted.Should().BeTrue();

        // 5. 104 encoded + 100 decoded -> lag is exactly DecoderAcceptanceLagWindow (4 frames) -> DecoderAcceptanceHealthy == true
        for (ulong frameId = 103; frameId <= 104; frameId++)
        {
            bool encoded = pipeline.TryEncodeFrame(mockTex, frameId, frameId * 16666, false, out _, buffer, out _);
            encoded.Should().BeTrue();
        }
        pipeline.RecordDecoderAcceptance(100);
        pipeline.Evidence.LastValidFrameId.Should().Be(104);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(100);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();
        pipeline.Evidence.DecoderAccepted.Should().BeTrue();

        // 6. 105 encoded + 100 decoded -> lag of 5 frames exceeds DecoderAcceptanceLagWindow (4 frames) -> DecoderAcceptanceHealthy == false
        bool encoded105 = pipeline.TryEncodeFrame(mockTex, 105, 105 * 16666, false, out _, buffer, out _);
        encoded105.Should().BeTrue();
        pipeline.RecordDecoderAcceptance(100);
        pipeline.Evidence.LastValidFrameId.Should().Be(105);
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(100);
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();

        // 7. Dispose -> SessionInitialised == false, decoder acceptance evidence cleared
        pipeline.Dispose();
        pipeline.Evidence.SessionInitialised.Should().BeFalse();
        pipeline.Evidence.DecoderAccepted.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptedLatestFrame.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
        pipeline.Evidence.LastDecoderAcceptedFrameId.Should().Be(0);
        pipeline.Evidence.LastValidFrameId.Should().Be(0);
    }

    [Fact]
    public void EncoderEvidencePolicy_EvaluatesCorrectlyAcrossAllBoundaryConditions()
    {
        // 1. isDisposed = true -> false
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: true,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 100
        ).Should().BeFalse("Disposed encoders must never evaluate as healthy");

        // 2. hasHandle = false -> false
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: false,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 100
        ).Should().BeFalse("Encoders without a valid handle must never evaluate as healthy");

        // 3. lastAccepted = 0 -> false
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 0
        ).Should().BeFalse("No decoder acceptance acknowledged must evaluate as not healthy");

        // 4. lastAccepted = 101, lastValid = 100 -> false (future frame rejected)
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 101
        ).Should().BeFalse("Decoder acceptance of an unencoded future frame must be rejected");

        // 5. lastAccepted = 96, lastValid = 100 (lag 4) -> true (boundary accepted)
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 96
        ).Should().BeTrue("Decoder acceptance within the maximum lag window (lag = 4) must evaluate as healthy");

        // 6. lastAccepted = 95, lastValid = 100 (lag 5) -> false (lag exceeded rejected)
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 95
        ).Should().BeFalse("Decoder acceptance exceeding the maximum lag window (lag = 5) must evaluate as not healthy");

        // 7. lastAccepted = 100, lastValid = 100 (lag 0) -> true
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: 100,
            lastDecoderAcceptedFrameId: 100
        ).Should().BeTrue("Exact frame match with zero lag (lag = 0) must evaluate as healthy");
    }

    [Fact]
    public void EncoderEvidencePolicy_DefaultDecoderAcceptanceLagWindow_EqualsFourAndMatchesAlias()
    {
        EncoderEvidencePolicy.DefaultDecoderAcceptanceLagWindow.Should().Be(4);
        EncoderEvidencePolicy.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DefaultDecoderAcceptanceLagWindow);
    }

    [Fact]
    public void EncoderEvidencePolicy_CustomMaxAcceptableLagWindow_EvaluatesThresholdsCorrectly()
    {
        const ulong currentFrame = 100;

        // Custom lag window = 2 (e.g. ultra-low-latency LAN profile)
        const ulong tightLagWindow = 2;
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 100,
            maxAcceptableLagWindow: tightLagWindow
        ).Should().BeTrue("Zero lag is healthy under tight lag window");

        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 99,
            maxAcceptableLagWindow: tightLagWindow
        ).Should().BeTrue("Lag of 1 frame is healthy when window is 2");

        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 98,
            maxAcceptableLagWindow: tightLagWindow
        ).Should().BeTrue("Lag equal to window (2 frames) is healthy");

        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 97,
            maxAcceptableLagWindow: tightLagWindow
        ).Should().BeFalse("Lag of 3 frames exceeds tight window of 2");

        // Custom lag window = 8 (e.g. high-jitter WAN profile)
        const ulong relaxedLagWindow = 8;
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 96,
            maxAcceptableLagWindow: relaxedLagWindow
        ).Should().BeTrue("Lag of 4 frames is healthy when window is 8");

        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 92,
            maxAcceptableLagWindow: relaxedLagWindow
        ).Should().BeTrue("Lag equal to window (8 frames) is healthy");

        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 91,
            maxAcceptableLagWindow: relaxedLagWindow
        ).Should().BeFalse("Lag of 9 frames exceeds relaxed window of 8");

        // State invalidation overrides relaxed window: isDisposed = true -> false
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: true,
            hasHandle: true,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 92,
            maxAcceptableLagWindow: relaxedLagWindow
        ).Should().BeFalse("Disposed state must invalidate health even with relaxed lag window");

        // State invalidation overrides relaxed window: hasHandle = false -> false
        EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(
            isDisposed: false,
            hasHandle: false,
            lastValidFrameId: currentFrame,
            lastDecoderAcceptedFrameId: 92,
            maxAcceptableLagWindow: relaxedLagWindow
        ).Should().BeFalse("Missing native handle must invalidate health even with relaxed lag window");
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_DecoderAcceptanceLagWindow_MatchesDocumentedSpecification()
    {
        EncoderEvidencePolicy.DefaultDecoderAcceptanceLagWindow.Should().Be(4);
        EncoderEvidencePolicy.DecoderAcceptanceLagWindow.Should().Be(4);
        EncoderEvidencePolicy.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DefaultDecoderAcceptanceLagWindow);
        HardwareVideoEncoderPipeline.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DecoderAcceptanceLagWindow);
        NvencHardwareEncoderPipeline.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DecoderAcceptanceLagWindow);
        AmfHardwareEncoderPipeline.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DecoderAcceptanceLagWindow);
        QsvHardwareEncoderPipeline.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DecoderAcceptanceLagWindow);
        UnifiedHardwareEncoderEngine.DecoderAcceptanceLagWindow.Should().Be(EncoderEvidencePolicy.DecoderAcceptanceLagWindow);
    }

    [Fact]
    public void SyntheticCorrelatingEncoderPipeline_IndependentFieldTracking_OperatesCorrectly()
    {
        using var pipeline = new SyntheticCorrelatingEncoderPipeline();
        Span<byte> buffer = stackalloc byte[1024];

        // Before producing any valid output
        pipeline.HasProducedValidOutput.Should().BeFalse();
        pipeline.Evidence.OutputReceived.Should().BeFalse();
        pipeline.Evidence.BitstreamStructurallyValid.Should().BeFalse();
        pipeline.Evidence.AccessUnitValid.Should().BeFalse();
        pipeline.Evidence.ApiAvailable.Should().BeTrue();
        pipeline.Evidence.SessionInitialised.Should().BeTrue();
        pipeline.IsActive.Should().BeTrue();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);

        // Submit first frame
        bool encoded = pipeline.TryEncodeFrame((IntPtr)0x1000, 1, 16666, true, out _, buffer, out int written);
        encoded.Should().BeTrue();
        written.Should().BeGreaterThan(0);
        pipeline.HasProducedValidOutput.Should().BeTrue();
        pipeline.Evidence.OutputReceived.Should().BeTrue();
        pipeline.Evidence.BitstreamStructurallyValid.Should().BeTrue();
        pipeline.Evidence.AccessUnitValid.Should().BeTrue();

        // Simulate handle loss independently of disposal
        pipeline.HasNativeHandle = false;
        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Faulted);
        pipeline.Evidence.ApiAvailable.Should().BeFalse();
        pipeline.Evidence.SessionInitialised.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();

        // Restore handle
        pipeline.HasNativeHandle = true;
        pipeline.IsActive.Should().BeTrue();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Ready);
        pipeline.Evidence.ApiAvailable.Should().BeTrue();
        pipeline.Evidence.SessionInitialised.Should().BeTrue();

        // Acknowledge frame 1
        pipeline.RecordDecoderAcceptance(1);
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeTrue();

        // Dispose pipeline
        pipeline.Dispose();
        pipeline.IsActive.Should().BeFalse();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);
        pipeline.Evidence.SessionInitialised.Should().BeFalse();
        pipeline.Evidence.DecoderAcceptanceHealthy.Should().BeFalse();
    }

    private sealed class SyntheticCorrelatingEncoderPipeline : IVideoEncoderPipeline
    {
        private bool _disposed;
        private bool _hasNativeHandle = true;
        private bool _hasProducedValidOutput;
        private bool _frameSubmitted;
        private ulong _firstValidFrameId;
        private ulong _lastValidFrameId;
        private ulong _lastDecoderAcceptedFrameId;
        private bool _hasValidFrame;
        private bool _hasDecoderAcceptance;

        public bool HasNativeHandle
        {
            get => _hasNativeHandle;
            set => _hasNativeHandle = value;
        }

        public uint Width => 1920;
        public uint Height => 1080;
        public uint Fps => 60;
        public uint BitrateKbps => 20000;
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Auto;
        public bool IsActive => !_disposed && _hasNativeHandle;
        public EncoderImplementationKind ImplementationKind => EncoderImplementationKind.HardwareAccelerated;
        public bool IsHardwareAccelerated => true;
        public bool HasProducedValidOutput => _hasProducedValidOutput;
        public Type ImplementationType => typeof(SyntheticCorrelatingEncoderPipeline);
        public EncoderRuntimeState RuntimeState => _disposed ? EncoderRuntimeState.Disposed : (!_hasNativeHandle ? EncoderRuntimeState.Faulted : EncoderRuntimeState.Ready);
        public double AverageEncodingLatencyMicroseconds => 150.0;

        public EncoderEvidence Evidence
        {
            get
            {
                ulong lastValid = _lastValidFrameId;
                ulong lastAccepted = _lastDecoderAcceptedFrameId;
                bool hasAccepted = _hasDecoderAcceptance;
                bool hasValid = _hasValidFrame;
                bool latestMatch = hasAccepted && hasValid && lastAccepted == lastValid;
                bool healthy = EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(_disposed, _hasNativeHandle, hasValid, lastValid, hasAccepted, lastAccepted);

                return new EncoderEvidence(
                    ApiAvailable: _hasNativeHandle,
                    HardwareSupported: true,
                    SessionInitialised: !_disposed && _hasNativeHandle,
                    FrameSubmitted: _frameSubmitted,
                    OutputReceived: _hasProducedValidOutput,
                    BitstreamStructurallyValid: _hasProducedValidOutput,
                    AccessUnitValid: _hasProducedValidOutput,
                    DecoderAccepted: healthy,
                    FirstValidFrameId: _firstValidFrameId,
                    LastValidFrameId: lastValid,
                    LastDecoderAcceptedFrameId: lastAccepted,
                    DecoderAcceptedLatestFrame: latestMatch,
                    DecoderAcceptanceHealthy: healthy,
                    HasDecoderAcceptance: hasAccepted,
                    HasValidFrame: hasValid
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
            if (_disposed || !_hasNativeHandle || d3dTexture == IntPtr.Zero)
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
                _hasProducedValidOutput = true;
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
            _hasDecoderAcceptance = true;
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
            if (_disposed || !_hasNativeHandle)
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
        public bool ReconfigureResolution(uint width, uint height, uint fps = 60, uint bitrateKbps = 0) => true;
        public bool Drain() => true;
        public bool Flush() => true;
        public void RequestKeyframe() { }
        public bool TryRecoverDevice(IntPtr newD3dDevice) => true;
        public void Dispose()
        {
            _disposed = true;
            _hasNativeHandle = false;
            _hasDecoderAcceptance = false;
            _hasValidFrame = false;
            _lastDecoderAcceptedFrameId = 0;
            _lastValidFrameId = 0;
        }
    }
}
