using System;
using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Result of an access unit structural bitstream validation check.
/// </summary>
public readonly record struct AccessUnitValidationResult(
    bool IsValid,
    bool HasStructurallyValidPayload,
    bool HasCodecHeaders,
    bool HasRandomAccessMarker,
    bool ContainsFrameData,
    bool IsCompleteAccessUnit,
    int NaluCount,
    bool HasParameterSets = false,
    bool HasIdr = false,
    bool HasRandomAccessPoint = false
);

/// <summary>
/// High-performance zero-allocation bitstream structural validator for H.264, HEVC, and AV1 compressed payloads.
/// </summary>
public static class BitstreamValidator
{
    /// <summary>
    /// Validates whether a compressed bitstream payload contains structurally valid NAL units or OBU sequences.
    /// </summary>
    public static bool ValidateBitstream(VideoCodec codec, ReadOnlySpan<byte> bitstream, out bool isKeyframe)
    {
        isKeyframe = false;
        if (bitstream.Length < 4) return false;

        var result = ValidateAccessUnit(codec, bitstream);
        isKeyframe = result.HasCodecHeaders || result.HasRandomAccessMarker || result.HasParameterSets || result.HasIdr || result.HasRandomAccessPoint;
        return result.IsValid;
    }

    /// <summary>
    /// Validates an access unit bitstream structure and extracts parameter set, keyframe, and random access point flags.
    /// </summary>
    public static AccessUnitValidationResult ValidateAccessUnit(VideoCodec codec, ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.Length < 4)
        {
            return new AccessUnitValidationResult(
                IsValid: false,
                HasStructurallyValidPayload: false,
                HasCodecHeaders: false,
                HasRandomAccessMarker: false,
                ContainsFrameData: false,
                IsCompleteAccessUnit: false,
                NaluCount: 0,
                HasParameterSets: false,
                HasIdr: false,
                HasRandomAccessPoint: false
            );
        }

        return codec switch
        {
            VideoCodec.H264 => ValidateH264AccessUnit(bitstream),
            VideoCodec.Hevc or VideoCodec.HevcMain10 => ValidateHevcAccessUnit(bitstream),
            VideoCodec.Av1 => ValidateAv1AccessUnit(bitstream),
            _ => new AccessUnitValidationResult(
                IsValid: false,
                HasStructurallyValidPayload: false,
                HasCodecHeaders: false,
                HasRandomAccessMarker: false,
                ContainsFrameData: false,
                IsCompleteAccessUnit: false,
                NaluCount: 0,
                HasParameterSets: false,
                HasIdr: false,
                HasRandomAccessPoint: false
            )
        };
    }

    private static AccessUnitValidationResult ValidateH264AccessUnit(ReadOnlySpan<byte> bitstream)
    {
        int offset = 0;
        bool foundValidNalu = false;
        bool hasSps = false;
        bool hasPps = false;
        bool hasIdr = false;
        bool hasNonIdr = false;
        int naluCount = 0;

        while (offset + 3 < bitstream.Length)
        {
            int startCodeLen = 0;
            if (bitstream[offset] == 0 && bitstream[offset + 1] == 0)
            {
                if (bitstream[offset + 2] == 1)
                {
                    startCodeLen = 3;
                }
                else if (offset + 3 < bitstream.Length && bitstream[offset + 2] == 0 && bitstream[offset + 3] == 1)
                {
                    startCodeLen = 4;
                }
            }

            if (startCodeLen > 0)
            {
                int naluHeaderIdx = offset + startCodeLen;
                if (naluHeaderIdx < bitstream.Length)
                {
                    byte header = bitstream[naluHeaderIdx];
                    int nalUnitType = header & 0x1F;
                    foundValidNalu = true;
                    naluCount++;

                    // H.264 NAL Unit Types: 1 = Non-IDR Slice, 2..4 = Slice Partitions, 5 = IDR Slice, 7 = SPS, 8 = PPS
                    if (nalUnitType == 7) hasSps = true;
                    else if (nalUnitType == 8) hasPps = true;
                    else if (nalUnitType == 5) hasIdr = true;
                    else if (nalUnitType == 1 || (nalUnitType >= 2 && nalUnitType <= 4)) hasNonIdr = true;
                }
                offset += startCodeLen;
            }
            else
            {
                offset++;
            }
        }

        bool hasCodecHeaders = hasSps || hasPps;
        bool hasRandomAccessMarker = hasIdr || hasSps;
        bool containsFrameData = hasIdr || hasNonIdr;
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: foundValidNalu,
            HasStructurallyValidPayload: foundValidNalu,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: naluCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasIdr,
            HasRandomAccessPoint: hasRandomAccessMarker
        );
    }

    private static AccessUnitValidationResult ValidateHevcAccessUnit(ReadOnlySpan<byte> bitstream)
    {
        int offset = 0;
        bool foundValidNalu = false;
        bool hasVps = false;
        bool hasSps = false;
        bool hasPps = false;
        bool hasIdr = false;
        bool hasCra = false;
        bool hasTrail = false;
        int naluCount = 0;

        while (offset + 3 < bitstream.Length)
        {
            int startCodeLen = 0;
            if (bitstream[offset] == 0 && bitstream[offset + 1] == 0)
            {
                if (bitstream[offset + 2] == 1)
                {
                    startCodeLen = 3;
                }
                else if (offset + 3 < bitstream.Length && bitstream[offset + 2] == 0 && bitstream[offset + 3] == 1)
                {
                    startCodeLen = 4;
                }
            }

            if (startCodeLen > 0)
            {
                int naluHeaderIdx = offset + startCodeLen;
                if (naluHeaderIdx < bitstream.Length)
                {
                    byte header = bitstream[naluHeaderIdx];
                    int nalUnitType = (header >> 1) & 0x3F;
                    foundValidNalu = true;
                    naluCount++;

                    // HEVC NAL Unit Types: 0..3 = TRAIL, 4..9 = TSA/STSA/RADL/RASL, 19 = IDR_W_RADL, 20 = IDR_N_LP, 21 = CRA, 32 = VPS, 33 = SPS, 34 = PPS
                    if (nalUnitType == 32) hasVps = true;
                    else if (nalUnitType == 33) hasSps = true;
                    else if (nalUnitType == 34) hasPps = true;
                    else if (nalUnitType == 19 || nalUnitType == 20) hasIdr = true;
                    else if (nalUnitType == 21) hasCra = true;
                    else if (nalUnitType <= 3 || (nalUnitType >= 4 && nalUnitType <= 9)) hasTrail = true;
                }
                offset += startCodeLen;
            }
            else
            {
                offset++;
            }
        }

        bool hasCodecHeaders = hasVps || hasSps || hasPps;
        bool hasRandomAccessMarker = hasIdr || hasCra;
        bool containsFrameData = hasIdr || hasCra || hasTrail;
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: foundValidNalu,
            HasStructurallyValidPayload: foundValidNalu,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: naluCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasIdr,
            HasRandomAccessPoint: hasRandomAccessMarker
        );
    }

    private static AccessUnitValidationResult ValidateAv1AccessUnit(ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.Length < 1)
        {
            return new AccessUnitValidationResult(
                IsValid: false,
                HasStructurallyValidPayload: false,
                HasCodecHeaders: false,
                HasRandomAccessMarker: false,
                ContainsFrameData: false,
                IsCompleteAccessUnit: false,
                NaluCount: 0,
                HasParameterSets: false,
                HasIdr: false,
                HasRandomAccessPoint: false
            );
        }

        int offset = 0;
        bool foundValidObu = false;
        bool hasSeqHeader = false;
        bool hasFrameHeader = false;
        bool hasTileGroup = false;
        bool hasFrame = false;
        int obuCount = 0;

        while (offset < bitstream.Length)
        {
            byte header = bitstream[offset];
            // Forbidden bit must be 0
            if ((header & 0x80) != 0)
            {
                return new AccessUnitValidationResult(
                    IsValid: false,
                    HasStructurallyValidPayload: false,
                    HasCodecHeaders: false,
                    HasRandomAccessMarker: false,
                    ContainsFrameData: false,
                    IsCompleteAccessUnit: false,
                    NaluCount: obuCount,
                    HasParameterSets: false,
                    HasIdr: false,
                    HasRandomAccessPoint: false
                );
            }

            int obuType = (header >> 3) & 0x0F;
            // Valid OBU types are 1..8:
            // 1 = Sequence Header, 2 = Temporal Delimiter, 3 = Frame Header, 4 = Tile Group,
            // 5 = Metadata, 6 = Frame, 7 = Redundant Frame Header, 8 = Tile List
            if (obuType < 1 || obuType > 8)
            {
                return new AccessUnitValidationResult(
                    IsValid: false,
                    HasStructurallyValidPayload: false,
                    HasCodecHeaders: false,
                    HasRandomAccessMarker: false,
                    ContainsFrameData: false,
                    IsCompleteAccessUnit: false,
                    NaluCount: obuCount,
                    HasParameterSets: false,
                    HasIdr: false,
                    HasRandomAccessPoint: false
                );
            }

            foundValidObu = true;
            obuCount++;

            if (obuType == 1) // Sequence Header signifies keyframe / parameter sets
            {
                hasSeqHeader = true;
            }
            else if (obuType == 3) // Frame Header
            {
                hasFrameHeader = true;
            }
            else if (obuType == 4) // Tile Group
            {
                hasTileGroup = true;
            }
            else if (obuType == 6) // Frame (Header + Tile Group)
            {
                hasFrame = true;
            }

            bool extensionFlag = ((header >> 2) & 0x01) != 0;
            bool hasSizeField = ((header >> 1) & 0x01) != 0;

            offset++;
            if (extensionFlag)
            {
                if (offset >= bitstream.Length) break;
                offset++; // Skip OBU extension header
            }

            if (hasSizeField)
            {
                ulong obuSize = 0;
                int shift = 0;
                int lebBytes = 0;
                bool validLeb = false;
                while (offset < bitstream.Length && lebBytes < 8)
                {
                    byte b = bitstream[offset++];
                    lebBytes++;
                    obuSize |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0)
                    {
                        validLeb = true;
                        break;
                    }
                    shift += 7;
                }

                if (!validLeb) break;
                if ((ulong)offset + obuSize > (ulong)bitstream.Length)
                {
                    break;
                }
                offset += (int)obuSize;
            }
            else
            {
                // Without explicit size field, OBU spans remainder of bitstream
                break;
            }
        }

        bool hasCodecHeaders = hasSeqHeader;
        bool hasRandomAccessMarker = hasSeqHeader;
        bool containsFrameData = hasFrame || (hasFrameHeader && hasTileGroup);
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: foundValidObu,
            HasStructurallyValidPayload: foundValidObu,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: obuCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasSeqHeader,
            HasRandomAccessPoint: hasRandomAccessMarker
        );
    }
}
