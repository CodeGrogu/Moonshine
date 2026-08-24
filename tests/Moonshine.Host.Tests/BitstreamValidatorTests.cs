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
        // OBU Sequence Header: obu_type = 1 -> (1 << 3) = 0x08 | 0x02 = 0x0A
        byte[] av1SeqHeader = [0x0A, 0x0A, 0x00, 0x00];
        bool valid = BitstreamValidator.ValidateBitstream(VideoCodec.Av1, av1SeqHeader, out bool isKeyframe);
        valid.Should().BeTrue();
        isKeyframe.Should().BeTrue();

        // OBU Frame: obu_type = 6 -> (6 << 3) = 0x30 | 0x02 = 0x32
        byte[] av1Frame = [0x32, 0x10, 0x20, 0x30];
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
        byte[] av1ForbiddenBit = [0x8A, 0x0A, 0x00, 0x00];
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
            LastValidFrameId: 150
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
        nonIdrResult.IsCompleteAccessUnit.Should().BeTrue();
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
        craResult.IsCompleteAccessUnit.Should().BeTrue();
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
        trailResult.IsCompleteAccessUnit.Should().BeTrue();
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
        // OBU Sequence Header: obu_type = 1 -> (1 << 3) = 0x08 | 0x02 = 0x0A
        byte[] av1SeqHeader = [0x0A, 0x0A, 0x00, 0x00];
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

        // OBU Frame: obu_type = 6 -> (6 << 3) = 0x30 | 0x02 = 0x32
        byte[] av1Frame = [0x32, 0x10, 0x20, 0x30];
        var frameResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1Frame);
        frameResult.IsValid.Should().BeTrue();
        frameResult.HasStructurallyValidPayload.Should().BeTrue();
        frameResult.HasCodecHeaders.Should().BeFalse();
        frameResult.HasRandomAccessMarker.Should().BeFalse();
        frameResult.ContainsFrameData.Should().BeTrue();
        frameResult.IsCompleteAccessUnit.Should().BeTrue();
        frameResult.NaluCount.Should().Be(1);
        frameResult.HasParameterSets.Should().BeFalse();
        frameResult.HasIdr.Should().BeFalse();
        frameResult.HasRandomAccessPoint.Should().BeFalse();

        // OBU Frame Header: obu_type = 3 -> (3 << 3) = 0x18 | 0x02 = 0x1A
        byte[] av1FrameHeader = [0x1A, 0x10, 0x20, 0x30];
        var frameHeaderResult = BitstreamValidator.ValidateAccessUnit(VideoCodec.Av1, av1FrameHeader);
        frameHeaderResult.IsValid.Should().BeTrue();
        frameHeaderResult.HasStructurallyValidPayload.Should().BeTrue();
        frameHeaderResult.HasCodecHeaders.Should().BeFalse();
        frameHeaderResult.HasRandomAccessMarker.Should().BeFalse();
        frameHeaderResult.ContainsFrameData.Should().BeTrue();
        frameHeaderResult.IsCompleteAccessUnit.Should().BeTrue();
        frameHeaderResult.NaluCount.Should().Be(1);
        frameHeaderResult.HasParameterSets.Should().BeFalse();
        frameHeaderResult.HasIdr.Should().BeFalse();
        frameHeaderResult.HasRandomAccessPoint.Should().BeFalse();
    }
}
