using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class BitstreamValidatorTests
{
    [Fact]
    public void BitstreamValidator_H264Keyframe_ValidatesSuccessfully()
    {
        byte[] h264Sps = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.H264, h264Sps, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();

        byte[] h264Idr = [0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00];
        valid = BitstreamValidator.ValidateBitstream(VideoCodec.H264, h264Idr, out isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();
    }

    [Fact]
    public void BitstreamValidator_H264InterFrame_ValidatesSuccessfully()
    {
        byte[] h264NonIdr = [0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.H264, h264NonIdr, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_HevcKeyframe_ValidatesSuccessfully()
    {
        // 0x26 >> 1 = 19 (IDR_W_RADL)
        byte[] hevcIdr = [0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.HevcMain10, hevcIdr, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();

        // 0x40 >> 1 = 32 (VPS)
        byte[] hevcVps = [0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01];
        valid = BitstreamValidator.ValidateBitstream(VideoCodec.Hevc, hevcVps, out isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();
    }

    [Fact]
    public void BitstreamValidator_HevcInterFrame_ValidatesSuccessfully()
    {
        // 0x02 >> 1 = 1 (TRAIL_R)
        byte[] hevcTrail = [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xD0];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.HevcMain10, hevcTrail, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_Av1Obu_ValidatesSuccessfully()
    {
        // OBU Sequence Header: obu_type = 1 -> (1 << 3) = 0x08 | 0x02 = 0x0A, size = 2
        byte[] av1SeqHeader = [0x0A, 0x02, 0x00, 0x00];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1SeqHeader, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();

        // OBU Frame: obu_type = 6 -> (6 << 3) = 0x30 | 0x02 = 0x32, size = 2
        byte[] av1Frame = [0x32, 0x02, 0x20, 0x30];
        valid = BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1Frame, out isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_InvalidBitstream_ReturnsFalse()
    {
        byte[] tooShort = [0x00, 0x00];
        BitstreamValidator.ValidateBitstream(VideoCodec.H264, tooShort, out _).Should().BeFalse();
        BitstreamValidator.ValidateBitstream(VideoCodec.Hevc, tooShort, out _).Should().BeFalse();
        BitstreamValidator.ValidateBitstream(VideoCodec.Av1, tooShort, out _).Should().BeFalse();

        byte[] invalidGarbage = [0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA];
        BitstreamValidator.ValidateBitstream(VideoCodec.H264, invalidGarbage, out _).Should().BeFalse();
        BitstreamValidator.ValidateBitstream(VideoCodec.Hevc, invalidGarbage, out _).Should().BeFalse();
        BitstreamValidator.ValidateBitstream(VideoCodec.Av1, invalidGarbage, out _).Should().BeFalse();

        // AV1 with forbidden bit set (bit 7 = 1)
        byte[] av1ForbiddenBit = [0x8A, 0x02, 0x00, 0x00];
        BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1ForbiddenBit, out _).Should().BeFalse();

        // AV1 with invalid obu_type 0
        byte[] av1InvalidType0 = [0x02, 0x01, 0x00, 0x00];
        BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1InvalidType0, out _).Should().BeFalse();

        // AV1 with invalid obu_type 9
        byte[] av1InvalidType9 = [0x48, 0x01, 0x00, 0x00];
        BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1InvalidType9, out _).Should().BeFalse();
    }

    [Fact]
    public void EncoderRuntimeState_EnumValues_MatchExpected()
    {
        ((int)EncoderRuntimeState.Uninitialised).Should().Be(0);
        ((int)EncoderRuntimeState.Initialising).Should().Be(1);
        ((int)EncoderRuntimeState.Ready).Should().Be(2);
        ((int)EncoderRuntimeState.Encoding).Should().Be(3);
        ((int)EncoderRuntimeState.Faulted).Should().Be(4);
        ((int)EncoderRuntimeState.Disposed).Should().Be(5);
    }

    [Fact]
    public void EncoderResult_EnumValues_MatchExpected()
    {
        ((int)EncoderResult.Success).Should().Be(0);
        ((int)EncoderResult.NotAvailable).Should().Be(1);
        ((int)EncoderResult.UnsupportedCodec).Should().Be(2);
        ((int)EncoderResult.UnsupportedFormat).Should().Be(3);
        ((int)EncoderResult.InvalidConfiguration).Should().Be(4);
        ((int)EncoderResult.DeviceLost).Should().Be(5);
        ((int)EncoderResult.ResourceFailure).Should().Be(6);
        ((int)EncoderResult.EncoderFailure).Should().Be(7);
        ((int)EncoderResult.OutputUnavailable).Should().Be(8);
        ((int)EncoderResult.OutputInvalid).Should().Be(9);
        ((int)EncoderResult.Timeout).Should().Be(10);
    }

    [Fact]
    public void EncodeSubmissionResult_Instantiation_SetsProperties()
    {
        var desc = new MoonshineEncodedPacketDesc { FrameIndex = 1, PayloadSize = 100 };
        var submission = new EncodeSubmissionResult(
            Submitted: true,
            OutputAvailable: true,
            KeyFrame: true,
            BytesWritten: 100,
            PacketDesc: desc,
            Result: EncoderResult.Success
        );

        submission.Submitted.Should().BeTrue();
        submission.OutputAvailable.Should().BeTrue();
        submission.KeyFrame.Should().BeTrue();
        submission.BytesWritten.Should().Be(100);
        submission.PacketDesc.FrameIndex.Should().Be(1);
        submission.Result.Should().Be(EncoderResult.Success);
    }

    [Fact]
    public void EncoderEvidence_Instantiation_SetsAllAuthoritativeFields()
    {
        var evidence = new EncoderEvidence(
            ApiAvailable: true,
            HardwareSupported: true,
            SessionInitialised: true,
            FrameSubmitted: true,
            OutputReceived: true,
            BitstreamStructurallyValid: true,
            AccessUnitValid: true,
            DecoderAccepted: true,
            FirstValidFrameId: 100,
            LastValidFrameId: 150,
            LastDecoderAcceptedFrameId: 150,
            DecoderAcceptedLatestFrame: true,
            DecoderAcceptanceHealthy: true
        );

        evidence.ApiAvailable.Should().BeTrue();
        evidence.HardwareSupported.Should().BeTrue();
        evidence.SessionInitialised.Should().BeTrue();
        evidence.FrameSubmitted.Should().BeTrue();
        evidence.OutputReceived.Should().BeTrue();
        evidence.BitstreamStructurallyValid.Should().BeTrue();
        evidence.AccessUnitValid.Should().BeTrue();
        evidence.DecoderAccepted.Should().BeTrue();
        evidence.FirstValidFrameId.Should().Be(100);
        evidence.LastValidFrameId.Should().Be(150);
        evidence.LastDecoderAcceptedFrameId.Should().Be(150);
        evidence.DecoderAcceptedLatestFrame.Should().BeTrue();
        evidence.DecoderAcceptanceHealthy.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessUnit_H264_IdentifiesAllStructuralProperties()
    {
        // Combined SPS (7), PPS (8), IDR (5)
        byte[] h264KeyframeAu = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28, // SPS (7)
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80, // PPS (8)
            0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00        // IDR (5)
        ];

        var result = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264KeyframeAu);
        result.IsValid.Should().BeTrue();
        result.HasStructurallyValidPayload.Should().BeTrue();
        result.HasCodecHeaders.Should().BeTrue();
        result.HasRandomAccessMarker.Should().BeTrue();
        result.ContainsFrameData.Should().BeTrue();
        result.IsCompleteAccessUnit.Should().BeTrue();
        result.NaluCount.Should().Be(3);
        result.HasParameterSets.Should().BeTrue();
        result.HasIdr.Should().BeTrue();
        result.HasRandomAccessPoint.Should().BeTrue();

        // Non-IDR P-slice (1)
        byte[] h264PSlice = [0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24];
        var nonIdrResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264PSlice);
        nonIdrResult.IsValid.Should().BeTrue();
        nonIdrResult.HasStructurallyValidPayload.Should().BeTrue();
        nonIdrResult.HasCodecHeaders.Should().BeFalse();
        nonIdrResult.HasRandomAccessMarker.Should().BeFalse();
        nonIdrResult.ContainsFrameData.Should().BeTrue();
        nonIdrResult.IsCompleteAccessUnit.Should().BeFalse();
        nonIdrResult.NaluCount.Should().Be(1);
        nonIdrResult.HasParameterSets.Should().BeFalse();
        nonIdrResult.HasIdr.Should().BeFalse();
        nonIdrResult.HasRandomAccessPoint.Should().BeFalse();

        // Parameter sets only (SPS 7 + PPS 8) without slice data
        byte[] h264ParamSetsOnly = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28,
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80
        ];
        var paramResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264ParamSetsOnly);
        paramResult.IsValid.Should().BeTrue();
        paramResult.HasStructurallyValidPayload.Should().BeTrue();
        paramResult.HasCodecHeaders.Should().BeTrue();
        paramResult.HasRandomAccessMarker.Should().BeTrue();
        paramResult.ContainsFrameData.Should().BeFalse();
        paramResult.IsCompleteAccessUnit.Should().BeFalse();
        paramResult.NaluCount.Should().Be(2);
    }

    [Fact]
    public void ValidateAccessUnit_Hevc_IdentifiesAllStructuralProperties()
    {
        // Combined VPS (32), SPS (33), PPS (34), IDR (19)
        byte[] hevcKeyframeAu = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, // VPS (32)
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, // SPS (33)
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0, // PPS (34)
            0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE  // IDR (19)
        ];

        var result = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, hevcKeyframeAu);
        result.IsValid.Should().BeTrue();
        result.HasStructurallyValidPayload.Should().BeTrue();
        result.HasCodecHeaders.Should().BeTrue();
        result.HasRandomAccessMarker.Should().BeTrue();
        result.ContainsFrameData.Should().BeTrue();
        result.IsCompleteAccessUnit.Should().BeTrue();
        result.NaluCount.Should().Be(4);
        result.HasParameterSets.Should().BeTrue();
        result.HasIdr.Should().BeTrue();
        result.HasRandomAccessPoint.Should().BeTrue();

        // CRA (21) - 0x2A >> 1 = 21
        byte[] hevcCra = [0x00, 0x00, 0x00, 0x01, 0x2A, 0x01, 0x11, 0x22];
        var craResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcCra);
        craResult.IsValid.Should().BeTrue();
        craResult.HasStructurallyValidPayload.Should().BeTrue();
        craResult.HasCodecHeaders.Should().BeFalse();
        craResult.HasRandomAccessMarker.Should().BeTrue();
        craResult.ContainsFrameData.Should().BeTrue();
        craResult.IsCompleteAccessUnit.Should().BeFalse();
        craResult.NaluCount.Should().Be(1);
        craResult.HasParameterSets.Should().BeFalse();
        craResult.HasIdr.Should().BeFalse();
        craResult.HasRandomAccessPoint.Should().BeTrue();

        // TRAIL (1) - 0x02 >> 1 = 1
        byte[] hevcTrail = [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xD0];
        var trailResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcTrail);
        trailResult.IsValid.Should().BeTrue();
        trailResult.HasStructurallyValidPayload.Should().BeTrue();
        trailResult.HasCodecHeaders.Should().BeFalse();
        trailResult.HasRandomAccessMarker.Should().BeFalse();
        trailResult.ContainsFrameData.Should().BeTrue();
        trailResult.IsCompleteAccessUnit.Should().BeFalse();
        trailResult.NaluCount.Should().Be(1);
        trailResult.HasParameterSets.Should().BeFalse();
        trailResult.HasIdr.Should().BeFalse();
        trailResult.HasRandomAccessPoint.Should().BeFalse();

        // Parameter sets only (VPS 32 + SPS 33 + PPS 34) without slice data
        byte[] hevcParamSetsOnly = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0
        ];
        var paramResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcParamSetsOnly);
        paramResult.IsValid.Should().BeTrue();
        paramResult.HasStructurallyValidPayload.Should().BeTrue();
        paramResult.HasCodecHeaders.Should().BeTrue();
        paramResult.HasRandomAccessMarker.Should().BeFalse();
        paramResult.ContainsFrameData.Should().BeFalse();
        paramResult.IsCompleteAccessUnit.Should().BeFalse();
        paramResult.NaluCount.Should().Be(3);
    }

    [Fact]
    public void ValidateAccessUnit_Av1_IdentifiesAllStructuralProperties()
    {
        // OBU Sequence Header: obu_type = 1 -> (1 << 3) = 0x08 | 0x02 = 0x0A, size = 2
        byte[] av1SeqHeader = [0x0A, 0x02, 0x00, 0x00];
        var seqResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1SeqHeader);
        seqResult.IsValid.Should().BeTrue();
        seqResult.HasStructurallyValidPayload.Should().BeTrue();
        seqResult.HasCodecHeaders.Should().BeTrue();
        seqResult.HasRandomAccessMarker.Should().BeTrue();
        seqResult.ContainsFrameData.Should().BeFalse();
        seqResult.IsCompleteAccessUnit.Should().BeFalse();
        seqResult.NaluCount.Should().Be(1);
        seqResult.HasParameterSets.Should().BeTrue();
        seqResult.HasIdr.Should().BeTrue();
        seqResult.HasRandomAccessPoint.Should().BeTrue();

        // OBU Frame: obu_type = 6 -> (6 << 3) = 0x30 | 0x02 = 0x32, size = 2
        byte[] av1Frame = [0x32, 0x02, 0x20, 0x30];
        var frameResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1Frame);
        frameResult.IsValid.Should().BeTrue();
        frameResult.HasStructurallyValidPayload.Should().BeTrue();
        frameResult.HasCodecHeaders.Should().BeFalse();
        frameResult.HasRandomAccessMarker.Should().BeFalse();
        frameResult.ContainsFrameData.Should().BeTrue();
        frameResult.IsCompleteAccessUnit.Should().BeFalse();
        frameResult.NaluCount.Should().Be(1);
        frameResult.HasParameterSets.Should().BeFalse();
        frameResult.HasIdr.Should().BeFalse();
        frameResult.HasRandomAccessPoint.Should().BeFalse();

        // OBU Frame Header alone: obu_type = 3 -> (3 << 3) = 0x18 | 0x02 = 0x1A (without tile group -> ContainsFrameData = false)
        byte[] av1FrameHeader = [0x1A, 0x02, 0x20, 0x30];
        var frameHeaderResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1FrameHeader);
        frameHeaderResult.IsValid.Should().BeTrue();
        frameHeaderResult.HasStructurallyValidPayload.Should().BeTrue();
        frameHeaderResult.HasCodecHeaders.Should().BeFalse();
        frameHeaderResult.HasRandomAccessMarker.Should().BeFalse();
        frameHeaderResult.ContainsFrameData.Should().BeFalse();
        frameHeaderResult.IsCompleteAccessUnit.Should().BeFalse();
        frameHeaderResult.NaluCount.Should().Be(1);
        frameHeaderResult.HasParameterSets.Should().BeFalse();
        frameHeaderResult.HasIdr.Should().BeFalse();
        frameHeaderResult.HasRandomAccessPoint.Should().BeFalse();
    }

    [Fact]
    public void ValidateAccessUnit_Av1_CompleteKeyframeAndTileGroup_ValidatesCorrectly()
    {
        // Combined Sequence Header (OBU 1: 0x0A) + Frame (OBU 6: 0x32)
        // obu_size LEB128 = 2 bytes payload
        byte[] av1CompleteFrameAu = [
            0x0A, 0x02, 0x11, 0x22, // OBU 1 (Sequence Header), size = 2
            0x32, 0x02, 0x33, 0x44  // OBU 6 (Frame), size = 2
        ];
        var completeResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1CompleteFrameAu);
        completeResult.IsValid.Should().BeTrue();
        completeResult.HasStructurallyValidPayload.Should().BeTrue();
        completeResult.HasCodecHeaders.Should().BeTrue();
        completeResult.HasRandomAccessMarker.Should().BeTrue();
        completeResult.ContainsFrameData.Should().BeTrue();
        completeResult.IsCompleteAccessUnit.Should().BeTrue();
        completeResult.NaluCount.Should().Be(2);

        // Combined Sequence Header (OBU 1) + Frame Header (OBU 3: 0x1A) + Tile Group (OBU 4: 0x22)
        byte[] av1CompleteTileGroupAu = [
            0x0A, 0x02, 0x11, 0x22, // OBU 1 (Sequence Header)
            0x1A, 0x02, 0x55, 0x66, // OBU 3 (Frame Header)
            0x22, 0x02, 0x77, 0x88  // OBU 4 (Tile Group)
        ];
        var completeTileResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1CompleteTileGroupAu);
        completeTileResult.IsValid.Should().BeTrue();
        completeTileResult.HasStructurallyValidPayload.Should().BeTrue();
        completeTileResult.HasCodecHeaders.Should().BeTrue();
        completeTileResult.HasRandomAccessMarker.Should().BeTrue();
        completeTileResult.ContainsFrameData.Should().BeTrue();
        completeTileResult.IsCompleteAccessUnit.Should().BeTrue();
        completeTileResult.NaluCount.Should().Be(3);

        // Frame Header (OBU 3) + Tile Group (OBU 4) WITHOUT Sequence Header
        byte[] av1FrameHeaderTileGroupNoSeq = [
            0x1A, 0x02, 0x55, 0x66, // OBU 3 (Frame Header)
            0x22, 0x02, 0x77, 0x88  // OBU 4 (Tile Group)
        ];
        var noSeqResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1FrameHeaderTileGroupNoSeq);
        noSeqResult.IsValid.Should().BeTrue();
        noSeqResult.HasStructurallyValidPayload.Should().BeTrue();
        noSeqResult.HasCodecHeaders.Should().BeFalse();
        noSeqResult.ContainsFrameData.Should().BeTrue();
        noSeqResult.IsCompleteAccessUnit.Should().BeFalse();

        // Standalone Tile Group (OBU 4: 0x22) without Frame Header
        byte[] av1StandaloneTileGroup = [0x22, 0x02, 0x77, 0x88];
        var standaloneTileResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1StandaloneTileGroup);
        standaloneTileResult.IsValid.Should().BeTrue();
        standaloneTileResult.HasStructurallyValidPayload.Should().BeTrue();
        standaloneTileResult.HasCodecHeaders.Should().BeFalse();
        standaloneTileResult.ContainsFrameData.Should().BeFalse();
        standaloneTileResult.IsCompleteAccessUnit.Should().BeFalse();
    }

    [Fact]
    public void ValidateAccessUnit_H264_CompleteAccessUnit_RequiresBothHeadersAndSliceData()
    {
        // Standalone IDR slice (5) without SPS/PPS
        byte[] h264StandaloneIdr = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00];
        var idrResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264StandaloneIdr);
        idrResult.IsValid.Should().BeTrue();
        idrResult.HasCodecHeaders.Should().BeFalse();
        idrResult.ContainsFrameData.Should().BeTrue();
        idrResult.IsCompleteAccessUnit.Should().BeFalse();

        // SPS + PPS + Non-IDR P-slice
        byte[] h264CompleteNonIdr = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28, // SPS (7)
            0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80, // PPS (8)
            0x00, 0x00, 0x00, 0x01, 0x41, 0x9A, 0x24        // Non-IDR (1)
        ];
        var completeNonIdrResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264CompleteNonIdr);
        completeNonIdrResult.IsValid.Should().BeTrue();
        completeNonIdrResult.HasCodecHeaders.Should().BeTrue();
        completeNonIdrResult.ContainsFrameData.Should().BeTrue();
        completeNonIdrResult.IsCompleteAccessUnit.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessUnit_Hevc_CompleteAccessUnit_RequiresBothHeadersAndSliceData()
    {
        // Standalone IDR slice (19) without VPS/SPS/PPS
        byte[] hevcStandaloneIdr = [0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE];
        var idrResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcStandaloneIdr);
        idrResult.IsValid.Should().BeTrue();
        idrResult.HasCodecHeaders.Should().BeFalse();
        idrResult.ContainsFrameData.Should().BeTrue();
        idrResult.IsCompleteAccessUnit.Should().BeFalse();

        // VPS + SPS + PPS + TRAIL P-slice
        byte[] hevcCompleteTrail = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, // VPS (32)
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, // SPS (33)
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0, // PPS (34)
            0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xD0        // TRAIL (1)
        ];
        var completeTrailResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcCompleteTrail);
        completeTrailResult.IsValid.Should().BeTrue();
        completeTrailResult.HasCodecHeaders.Should().BeTrue();
        completeTrailResult.ContainsFrameData.Should().BeTrue();
        completeTrailResult.IsCompleteAccessUnit.Should().BeTrue();
    }

    [Fact]
    public void BitstreamValidator_Av1_Leb128VariableLengthDecoding_BoundaryValues()
    {
        // 1-byte LEB128 boundary tests: 0 and 127
        byte[] leb1_zero = [0x00];
        BitstreamValidator.TryDecodeLeb128(leb1_zero, out ulong val1, out int read1).Should().BeTrue();
        val1.Should().Be(0);
        read1.Should().Be(1);

        byte[] leb1_127 = [0x7F];
        BitstreamValidator.TryDecodeLeb128(leb1_127, out ulong val2, out int read2).Should().BeTrue();
        val2.Should().Be(127);
        read2.Should().Be(1);

        // 2-byte LEB128 boundary tests: 128 and 16383
        byte[] leb2_128 = [0x80, 0x01];
        BitstreamValidator.TryDecodeLeb128(leb2_128, out ulong val3, out int read3).Should().BeTrue();
        val3.Should().Be(128);
        read3.Should().Be(2);

        byte[] leb2_16383 = [0xFF, 0x7F];
        BitstreamValidator.TryDecodeLeb128(leb2_16383, out ulong val4, out int read4).Should().BeTrue();
        val4.Should().Be(16383);
        read4.Should().Be(2);

        // 3-byte LEB128 test: 16384
        byte[] leb3_16384 = [0x80, 0x80, 0x01];
        BitstreamValidator.TryDecodeLeb128(leb3_16384, out ulong val5, out int read5).Should().BeTrue();
        val5.Should().Be(16384);
        read5.Should().Be(3);

        // 4-byte LEB128 test: 2097152
        byte[] leb4_2m = [0x80, 0x80, 0x80, 0x01];
        BitstreamValidator.TryDecodeLeb128(leb4_2m, out ulong val6, out int read6).Should().BeTrue();
        val6.Should().Be(2097152);
        read6.Should().Be(4);

        // 8-byte LEB128 test (max standard length)
        byte[] leb8_valid = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01];
        BitstreamValidator.TryDecodeLeb128(leb8_valid, out ulong val7, out int read7).Should().BeTrue();
        read7.Should().Be(8);
        val7.Should().BeGreaterThan(0);

        // Malformed LEB128: 9 bytes without terminator
        byte[] leb_invalid_over8 = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80];
        BitstreamValidator.TryDecodeLeb128(leb_invalid_over8, out _, out _).Should().BeFalse();

        // Truncated LEB128: ends with MSB=1 at end of buffer
        byte[] leb_truncated = [0x80, 0x80];
        BitstreamValidator.TryDecodeLeb128(leb_truncated, out _, out _).Should().BeFalse();

        // Empty span
        BitstreamValidator.TryDecodeLeb128(ReadOnlySpan<byte>.Empty, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_Av1_AllStandardObuTypes_ParsedAndValidated()
    {
        // Test all valid standard OBU types: 1, 2, 3, 4, 5, 6, 7, 8, 15 (Padding)
        byte[] obuPadding = [(15 << 3) | 0x02, 0x04, 0x00, 0x00, 0x00, 0x00]; // OBU 15 (Padding), size 4
        var padResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, obuPadding);
        padResult.IsValid.Should().BeTrue();
        padResult.NaluCount.Should().Be(1);

        byte[] obuMetadata = [(5 << 3) | 0x02, 0x02, 0x01, 0x02]; // OBU 5 (Metadata), size 2
        var metaResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, obuMetadata);
        metaResult.IsValid.Should().BeTrue();
        metaResult.NaluCount.Should().Be(1);

        byte[] obuTemporalDelimiter = [(2 << 3) | 0x02, 0x00]; // OBU 2 (Temporal Delimiter), size 0
        var tdResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, obuTemporalDelimiter);
        tdResult.IsValid.Should().BeTrue();
        tdResult.HasAud.Should().BeTrue();

        byte[] obuTileList = [(8 << 3) | 0x02, 0x02, 0xAA, 0xBB]; // OBU 8 (Tile List), size 2
        var tlResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, obuTileList);
        tlResult.IsValid.Should().BeTrue();

        byte[] obuRedundantFrameHeader = [(7 << 3) | 0x02, 0x02, 0xCC, 0xDD]; // OBU 7 (Redundant Frame Header), size 2
        var rfhResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, obuRedundantFrameHeader);
        rfhResult.IsValid.Should().BeTrue();
    }

    [Fact]
    public void BitstreamValidator_Av1_UncompressedHeaderKeyframeDetection()
    {
        // OBU Frame (Type 6): uncompressed_header show_existing_frame = 0, frame_type = 0 (KEY_FRAME)
        // Header byte = (6 << 3) | 0x02 = 0x32. Size = 0x02. Payload byte 0: 0x00 (bits: 00000000 -> show_existing=0, frame_type=0)
        byte[] av1KeyFrame = [0x32, 0x02, 0x00, 0x00];
        var keyResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1KeyFrame);
        keyResult.IsValid.Should().BeTrue();
        keyResult.HasRandomAccessPoint.Should().BeTrue();
        keyResult.HasIdr.Should().BeTrue();

        // OBU Frame (Type 6): uncompressed_header show_existing_frame = 0, frame_type = 2 (INTRA_ONLY_FRAME)
        // Payload byte 0: 0x40 (bits: 01000000 -> show_existing=0, frame_type=2)
        byte[] av1IntraOnly = [0x32, 0x02, 0x40, 0x00];
        var intraResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1IntraOnly);
        intraResult.IsValid.Should().BeTrue();
        intraResult.HasRandomAccessPoint.Should().BeTrue();

        // OBU Frame (Type 6): uncompressed_header show_existing_frame = 0, frame_type = 1 (INTER_FRAME)
        // Payload byte 0: 0x20 (bits: 00100000 -> show_existing=0, frame_type=1)
        byte[] av1Inter = [0x32, 0x02, 0x20, 0x00];
        var interResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1Inter);
        interResult.IsValid.Should().BeTrue();
        interResult.HasRandomAccessPoint.Should().BeFalse();
        interResult.HasIdr.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_Av1_BufferOverflowPayload_RejectsTruncated()
    {
        // OBU with size field = 100 bytes, but bitstream only has 4 bytes total
        byte[] av1Overflow = [0x0A, 0x64, 0x00, 0x00];
        var res = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1Overflow);
        res.IsValid.Should().BeFalse();

        // OBU with multi-byte LEB128 stating 500 bytes on truncated payload
        byte[] av1MultiByteOverflow = [0x0A, 0xF4, 0x03, 0x01, 0x02];
        var multiRes = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1MultiByteOverflow);
        multiRes.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_H264_ProfileLevelAndPoc_ExtractedAndVerified()
    {
        // SPS with Baseline profile (66 = 0x42) and Level 4.0 (40 = 0x28)
        byte[] sps = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28];
        var spsResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, sps);
        spsResult.IsValid.Should().BeTrue();
        spsResult.ProfileIdc.Should().Be(0x42);
        spsResult.LevelIdc.Should().Be(0x28);
        spsResult.HasSps.Should().BeTrue();

        // SPS with Level 0 (invalid level)
        byte[] invalidSps = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x00];
        var invalidResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, invalidSps);
        invalidResult.IsValid.Should().BeFalse();

        // IDR slice with nal_ref_idc == 0 (must be rejected per H.264 standard 7.4.1)
        byte[] invalidIdrRefIdc = [0x00, 0x00, 0x00, 0x01, 0x05, 0x88, 0x84];
        var idrRefResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, invalidIdrRefIdc);
        idrRefResult.IsValid.Should().BeFalse();

        // AUD (Type 9)
        byte[] h264Aud = [0x00, 0x00, 0x00, 0x01, 0x09, 0x10];
        var audResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, h264Aud);
        audResult.IsValid.Should().BeTrue();
        audResult.HasAud.Should().BeTrue();

        // Forbidden zero bit set in H.264 NAL header
        byte[] forbiddenNal = [0x00, 0x00, 0x00, 0x01, 0xE7, 0x42, 0xC0, 0x28];
        var forbiddenResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, forbiddenNal);
        forbiddenResult.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BitstreamValidator_Hevc_NalUnitTypesAndSliceHeaders_ExtractedAndVerified()
    {
        // VPS (32) + SPS (33) + PPS (34)
        byte[] vpsSpsPps = [
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, // VPS (32)
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, // SPS (33)
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0  // PPS (34)
        ];
        var res = BitstreamValidator.ValidateAccessUnit(VideoCodec.HevcMain10, vpsSpsPps);
        res.IsValid.Should().BeTrue();
        res.HasVps.Should().BeTrue();
        res.HasSps.Should().BeTrue();
        res.HasPps.Should().BeTrue();

        // AUD (35) - 0x46 >> 1 = 35
        byte[] hevcAud = [0x00, 0x00, 0x00, 0x01, 0x46, 0x01, 0x10];
        var audResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcAud);
        audResult.IsValid.Should().BeTrue();
        audResult.HasAud.Should().BeTrue();

        // TemporalIdPlus1 == 0 (forbidden in HEVC standard)
        byte[] invalidTemporalId = [0x00, 0x00, 0x00, 0x01, 0x26, 0x00, 0xAF, 0xFE];
        var temporalResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, invalidTemporalId);
        temporalResult.IsValid.Should().BeFalse();

        // Forbidden zero bit set in HEVC header0
        byte[] forbiddenHevc = [0x00, 0x00, 0x00, 0x01, 0xA6, 0x01, 0xAF, 0xFE];
        var forbiddenResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, forbiddenHevc);
        forbiddenResult.IsValid.Should().BeFalse();

        // CRA (21)
        byte[] hevcCra = [0x00, 0x00, 0x00, 0x01, 0x2A, 0x01, 0x11, 0x22];
        var craResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, hevcCra);
        craResult.IsValid.Should().BeTrue();
        craResult.HasCra.Should().BeTrue();
        craResult.HasRandomAccessPoint.Should().BeTrue();
    }

    [Fact]
    public void BitstreamValidator_CorruptAnnexBPrefix_RejectsNonZeroGarbage()
    {
        // Non-zero prefix bytes before start code
        byte[] corruptPrefixH264 = [0xAA, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28];
        var resH264 = BitstreamValidator.ValidateAccessUnit(VideoCodec.H264, corruptPrefixH264);
        resH264.IsValid.Should().BeFalse();

        byte[] corruptPrefixHevc = [0xAA, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01];
        var resHevc = BitstreamValidator.ValidateAccessUnit(VideoCodec.Hevc, corruptPrefixHevc);
        resHevc.IsValid.Should().BeFalse();
    }
}
