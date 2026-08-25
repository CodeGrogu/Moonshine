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
    bool HasRandomAccessPoint = false,
    uint ProfileIdc = 0,
    uint LevelIdc = 0,
    int PictureOrderCount = 0,
    bool HasAud = false,
    bool HasCra = false,
    bool HasVps = false,
    bool HasSps = false,
    bool HasPps = false
);

/// <summary>
/// Zero-allocation bit-level reader with on-the-fly emulation prevention byte (0x03) removal for NAL payloads.
/// </summary>
internal ref struct NalBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _byteOffset;
    private int _bitOffset;
    private byte _currentByte;
    private bool _hasCurrentByte;
    private byte _prev1;
    private byte _prev2;

    public NalBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _byteOffset = 0;
        _bitOffset = 0;
        _currentByte = 0;
        _hasCurrentByte = false;
        _prev1 = 0xFF;
        _prev2 = 0xFF;
    }

    private bool FetchNextByte(out byte b)
    {
        if (_byteOffset >= _data.Length)
        {
            b = 0;
            return false;
        }

        byte val = _data[_byteOffset++];
        if (_prev2 == 0x00 && _prev1 == 0x00 && val == 0x03)
        {
            if (_byteOffset >= _data.Length)
            {
                b = 0;
                return false;
            }
            val = _data[_byteOffset++];
            _prev2 = 0x00;
            _prev1 = val;
        }
        else
        {
            _prev2 = _prev1;
            _prev1 = val;
        }

        b = val;
        return true;
    }

    public bool ReadBit(out int bit)
    {
        if (!_hasCurrentByte || _bitOffset == 8)
        {
            if (!FetchNextByte(out _currentByte))
            {
                bit = 0;
                return false;
            }
            _hasCurrentByte = true;
            _bitOffset = 0;
        }

        bit = (_currentByte >> (7 - _bitOffset)) & 0x01;
        _bitOffset++;
        return true;
    }

    public bool ReadBits(int count, out uint value)
    {
        value = 0;
        for (int i = 0; i < count; i++)
        {
            if (!ReadBit(out int bit))
            {
                return false;
            }
            value = (value << 1) | (uint)bit;
        }
        return true;
    }

    public bool ReadUe(out uint value)
    {
        value = 0;
        int zeroCount = 0;
        while (true)
        {
            if (!ReadBit(out int bit))
            {
                return false;
            }
            if (bit == 1)
            {
                break;
            }
            zeroCount++;
            if (zeroCount > 31)
            {
                return false;
            }
        }

        if (zeroCount == 0)
        {
            value = 0;
            return true;
        }

        if (!ReadBits(zeroCount, out uint suffix))
        {
            return false;
        }

        value = (1u << zeroCount) - 1u + suffix;
        return true;
    }

    public bool ReadSe(out int value)
    {
        value = 0;
        if (!ReadUe(out uint ueVal))
        {
            return false;
        }
        value = (ueVal & 1) != 0 ? (int)((ueVal + 1) / 2) : -(int)(ueVal / 2);
        return true;
    }
}

/// <summary>
/// High-performance zero-allocation bitstream structural validator for H.264, HEVC, and AV1 compressed payloads.
/// </summary>
public static class BitstreamValidator
{
    /// <summary>
    /// Decodes an unsigned LEB128 variable-length integer up to 8 bytes per AV1 specification Section 4.10.5.
    /// </summary>
    public static bool TryDecodeLeb128(ReadOnlySpan<byte> data, out ulong value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        int shift = 0;

        for (int i = 0; i < 8 && i < data.Length; i++)
        {
            byte b = data[i];
            bytesRead++;
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
            shift += 7;
        }

        return false;
    }

    /// <summary>
    /// Validates whether a compressed bitstream payload contains structurally valid NAL units or OBU sequences.
    /// </summary>
    public static bool ValidateBitstream(VideoCodec codec, ReadOnlySpan<byte> bitstream, out bool isKeyframe)
    {
        isKeyframe = false;
        if (bitstream.IsEmpty) return false;

        var result = ValidateAccessUnit(codec, bitstream);
        isKeyframe = result.HasCodecHeaders || result.HasRandomAccessMarker || result.HasParameterSets || result.HasIdr || result.HasRandomAccessPoint;
        return result.IsValid;
    }

    /// <summary>
    /// Validates an access unit bitstream structure and extracts parameter set, keyframe, and random access point flags.
    /// </summary>
    public static AccessUnitValidationResult ValidateAccessUnit(VideoCodec codec, ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.IsEmpty)
        {
            return CreateInvalidResult();
        }

        return codec switch
        {
            VideoCodec.H264 => ValidateH264AccessUnit(bitstream),
            VideoCodec.Hevc or VideoCodec.HevcMain10 => ValidateHevcAccessUnit(bitstream),
            VideoCodec.Av1 => ValidateAv1AccessUnit(bitstream),
            _ => CreateInvalidResult()
        };
    }

    private static AccessUnitValidationResult CreateInvalidResult() => new(
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

    private static AccessUnitValidationResult ValidateH264AccessUnit(ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.Length < 4)
        {
            return CreateInvalidResult();
        }

        bool hasSps = false;
        bool hasPps = false;
        bool hasIdr = false;
        bool hasNonIdr = false;
        bool hasAud = false;
        uint profileIdc = 0;
        uint levelIdc = 0;
        int poc = 0;
        int naluCount = 0;

        int currentOffset = 0;
        int naluStart = -1;

        while (currentOffset + 2 < bitstream.Length)
        {
            int scLen = 0;
            if (bitstream[currentOffset] == 0 && bitstream[currentOffset + 1] == 0)
            {
                if (bitstream[currentOffset + 2] == 1)
                {
                    scLen = 3;
                }
                else if (currentOffset + 3 < bitstream.Length && bitstream[currentOffset + 2] == 0 && bitstream[currentOffset + 3] == 1)
                {
                    scLen = 4;
                }
            }

            if (scLen > 0)
            {
                if (naluStart < 0)
                {
                    for (int z = 0; z < currentOffset; z++)
                    {
                        if (bitstream[z] != 0)
                        {
                            return CreateInvalidResult();
                        }
                    }
                }
                else
                {
                    int nalPayloadStart = naluStart;
                    int nalPayloadEnd = currentOffset;

                    if (nalPayloadEnd > nalPayloadStart)
                    {
                        if (!ProcessH264Nalu(bitstream[nalPayloadStart..nalPayloadEnd], ref hasSps, ref hasPps, ref hasIdr, ref hasNonIdr, ref hasAud, ref profileIdc, ref levelIdc, ref poc))
                        {
                            return CreateInvalidResult();
                        }
                        naluCount++;
                    }
                }

                naluStart = currentOffset + scLen;
                currentOffset += scLen;
            }
            else
            {
                currentOffset++;
            }
        }

        if (naluStart >= 0)
        {
            int nalPayloadEnd = bitstream.Length;

            if (nalPayloadEnd > naluStart)
            {
                if (!ProcessH264Nalu(bitstream[naluStart..nalPayloadEnd], ref hasSps, ref hasPps, ref hasIdr, ref hasNonIdr, ref hasAud, ref profileIdc, ref levelIdc, ref poc))
                {
                    return CreateInvalidResult();
                }
                naluCount++;
            }
        }

        if (naluCount == 0)
        {
            return CreateInvalidResult();
        }

        bool hasCodecHeaders = hasSps || hasPps;
        bool hasRandomAccessMarker = hasIdr || hasSps;
        bool containsFrameData = hasIdr || hasNonIdr;
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: true,
            HasStructurallyValidPayload: true,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: naluCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasIdr,
            HasRandomAccessPoint: hasRandomAccessMarker,
            ProfileIdc: profileIdc,
            LevelIdc: levelIdc,
            PictureOrderCount: poc,
            HasAud: hasAud,
            HasCra: false,
            HasVps: false,
            HasSps: hasSps,
            HasPps: hasPps
        );
    }

    private static bool ProcessH264Nalu(
        ReadOnlySpan<byte> naluData,
        ref bool hasSps,
        ref bool hasPps,
        ref bool hasIdr,
        ref bool hasNonIdr,
        ref bool hasAud,
        ref uint profileIdc,
        ref uint levelIdc,
        ref int poc)
    {
        _ = poc;
        if (naluData.IsEmpty) return false;
        byte header = naluData[0];
        if ((header & 0x80) != 0) return false;

        int nalRefIdc = (header >> 5) & 0x03;
        int nalUnitType = header & 0x1F;

        if (nalUnitType == 0 || nalUnitType > 23) return false;

        ReadOnlySpan<byte> rbsp = naluData[1..];

        if (nalUnitType == 7)
        {
            hasSps = true;
            if (rbsp.Length >= 3)
            {
                profileIdc = rbsp[0];
                levelIdc = rbsp[2];
                if (levelIdc == 0) return false;
            }
        }
        else if (nalUnitType == 8)
        {
            hasPps = true;
        }
        else if (nalUnitType == 5)
        {
            if (nalRefIdc == 0) return false;
            hasIdr = true;

            if (!rbsp.IsEmpty)
            {
                var reader = new NalBitReader(rbsp);
                if (reader.ReadUe(out _) && reader.ReadUe(out uint sliceType))
                {
                    uint modSlice = sliceType % 5;
                    if (modSlice != 2 && modSlice != 4) return false;
                }
            }
        }
        else if (nalUnitType == 1 || (nalUnitType >= 2 && nalUnitType <= 4))
        {
            hasNonIdr = true;
        }
        else if (nalUnitType == 9)
        {
            hasAud = true;
        }

        return true;
    }

    private static AccessUnitValidationResult ValidateHevcAccessUnit(ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.Length < 5)
        {
            return CreateInvalidResult();
        }

        int naluCount = 0;
        bool hasVps = false;
        bool hasSps = false;
        bool hasPps = false;
        bool hasIdr = false;
        bool hasCra = false;
        bool hasBla = false;
        bool hasTrail = false;
        bool hasAud = false;

        int currentOffset = 0;
        int naluStart = -1;

        while (currentOffset + 2 < bitstream.Length)
        {
            int scLen = 0;
            if (bitstream[currentOffset] == 0 && bitstream[currentOffset + 1] == 0)
            {
                if (bitstream[currentOffset + 2] == 1)
                {
                    scLen = 3;
                }
                else if (currentOffset + 3 < bitstream.Length && bitstream[currentOffset + 2] == 0 && bitstream[currentOffset + 3] == 1)
                {
                    scLen = 4;
                }
            }

            if (scLen > 0)
            {
                if (naluStart < 0)
                {
                    for (int z = 0; z < currentOffset; z++)
                    {
                        if (bitstream[z] != 0)
                        {
                            return CreateInvalidResult();
                        }
                    }
                }
                else
                {
                    int nalPayloadStart = naluStart;
                    int nalPayloadEnd = currentOffset;

                    if (nalPayloadEnd > nalPayloadStart)
                    {
                        if (!ProcessHevcNalu(bitstream[nalPayloadStart..nalPayloadEnd], ref hasVps, ref hasSps, ref hasPps, ref hasIdr, ref hasCra, ref hasBla, ref hasTrail, ref hasAud))
                        {
                            return CreateInvalidResult();
                        }
                        naluCount++;
                    }
                }

                naluStart = currentOffset + scLen;
                currentOffset += scLen;
            }
            else
            {
                currentOffset++;
            }
        }

        if (naluStart >= 0)
        {
            int nalPayloadEnd = bitstream.Length;

            if (nalPayloadEnd > naluStart)
            {
                if (!ProcessHevcNalu(bitstream[naluStart..nalPayloadEnd], ref hasVps, ref hasSps, ref hasPps, ref hasIdr, ref hasCra, ref hasBla, ref hasTrail, ref hasAud))
                {
                    return CreateInvalidResult();
                }
                naluCount++;
            }
        }

        if (naluCount == 0)
        {
            return CreateInvalidResult();
        }

        bool hasCodecHeaders = hasVps || hasSps || hasPps;
        bool hasRandomAccessMarker = hasIdr || hasCra || hasBla;
        bool containsFrameData = hasIdr || hasCra || hasBla || hasTrail;
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: true,
            HasStructurallyValidPayload: true,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: naluCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasIdr,
            HasRandomAccessPoint: hasRandomAccessMarker,
            ProfileIdc: 0,
            LevelIdc: 0,
            PictureOrderCount: 0,
            HasAud: hasAud,
            HasCra: hasCra,
            HasVps: hasVps,
            HasSps: hasSps,
            HasPps: hasPps
        );
    }

    private static bool ProcessHevcNalu(
        ReadOnlySpan<byte> naluData,
        ref bool hasVps,
        ref bool hasSps,
        ref bool hasPps,
        ref bool hasIdr,
        ref bool hasCra,
        ref bool hasBla,
        ref bool hasTrail,
        ref bool hasAud)
    {
        if (naluData.Length < 2) return false;
        byte header0 = naluData[0];
        byte header1 = naluData[1];

        if ((header0 & 0x80) != 0) return false;

        int nalUnitType = (header0 >> 1) & 0x3F;
        int nuhTemporalIdPlus1 = header1 & 0x07;

        if (nuhTemporalIdPlus1 == 0) return false;
        if (nalUnitType > 63) return false;

        ReadOnlySpan<byte> rbsp = naluData[2..];

        if (nalUnitType == 32)
        {
            hasVps = true;
        }
        else if (nalUnitType == 33)
        {
            hasSps = true;
        }
        else if (nalUnitType == 34)
        {
            hasPps = true;
        }
        else if (nalUnitType == 35)
        {
            hasAud = true;
        }
        else if (nalUnitType == 19 || nalUnitType == 20)
        {
            hasIdr = true;
            VerifyHevcIrapSliceHeader(rbsp);
        }
        else if (nalUnitType == 21)
        {
            hasCra = true;
            VerifyHevcIrapSliceHeader(rbsp);
        }
        else if (nalUnitType >= 16 && nalUnitType <= 18)
        {
            hasBla = true;
            VerifyHevcIrapSliceHeader(rbsp);
        }
        else if (nalUnitType <= 3 || (nalUnitType >= 4 && nalUnitType <= 9))
        {
            hasTrail = true;
        }

        return true;
    }

    private static void VerifyHevcIrapSliceHeader(ReadOnlySpan<byte> rbsp)
    {
        if (rbsp.IsEmpty) return;
        var reader = new NalBitReader(rbsp);
        if (!reader.ReadBit(out _)) return;
        if (!reader.ReadBit(out _)) return;
        if (!reader.ReadUe(out _)) return;
    }

    private static AccessUnitValidationResult ValidateAv1AccessUnit(ReadOnlySpan<byte> bitstream)
    {
        if (bitstream.Length < 2)
        {
            return CreateInvalidResult();
        }

        int offset = 0;
        bool foundValidObu = false;
        bool hasSeqHeader = false;
        bool hasFrameHeader = false;
        bool hasTileGroup = false;
        bool hasFrame = false;
        bool hasKeyFrame = false;
        bool hasIntraOnlyFrame = false;
        bool hasTemporalDelimiter = false;
        int obuCount = 0;

        while (offset < bitstream.Length)
        {
            byte header = bitstream[offset++];
            if ((header & 0x80) != 0)
            {
                return CreateInvalidResult();
            }

            int obuType = (header >> 3) & 0x0F;
            if (obuType < 1 || (obuType > 8 && obuType != 15))
            {
                return CreateInvalidResult();
            }

            bool extensionFlag = ((header >> 2) & 0x01) != 0;
            bool hasSizeField = ((header >> 1) & 0x01) != 0;

            if (extensionFlag)
            {
                if (offset >= bitstream.Length)
                {
                    return CreateInvalidResult();
                }
                offset++;
            }

            ReadOnlySpan<byte> payload;
            if (hasSizeField)
            {
                if (!TryDecodeLeb128(bitstream[offset..], out ulong obuSize, out int lebBytes))
                {
                    return CreateInvalidResult();
                }
                offset += lebBytes;

                if ((ulong)offset + obuSize > (ulong)bitstream.Length)
                {
                    return CreateInvalidResult();
                }

                payload = bitstream.Slice(offset, (int)obuSize);
                offset += (int)obuSize;
            }
            else
            {
                payload = bitstream[offset..];
                offset = bitstream.Length;
            }

            foundValidObu = true;
            obuCount++;

            switch (obuType)
            {
                case 1:
                    hasSeqHeader = true;
                    break;
                case 2:
                    hasTemporalDelimiter = true;
                    break;
                case 3:
                    hasFrameHeader = true;
                    InspectAv1FrameHeader(payload, ref hasKeyFrame, ref hasIntraOnlyFrame);
                    break;
                case 4:
                    hasTileGroup = true;
                    break;
                case 5:
                    break;
                case 6:
                    hasFrame = true;
                    InspectAv1FrameHeader(payload, ref hasKeyFrame, ref hasIntraOnlyFrame);
                    break;
                case 7:
                    break;
                case 8:
                    break;
                case 15:
                    break;
            }
        }

        if (!foundValidObu || obuCount == 0)
        {
            return CreateInvalidResult();
        }

        bool hasCodecHeaders = hasSeqHeader;
        bool hasRandomAccessMarker = hasSeqHeader || hasKeyFrame || hasIntraOnlyFrame;
        bool containsFrameData = hasFrame || (hasFrameHeader && hasTileGroup);
        bool isCompleteAccessUnit = hasCodecHeaders && containsFrameData;

        return new AccessUnitValidationResult(
            IsValid: true,
            HasStructurallyValidPayload: true,
            HasCodecHeaders: hasCodecHeaders,
            HasRandomAccessMarker: hasRandomAccessMarker,
            ContainsFrameData: containsFrameData,
            IsCompleteAccessUnit: isCompleteAccessUnit,
            NaluCount: obuCount,
            HasParameterSets: hasCodecHeaders,
            HasIdr: hasSeqHeader || hasKeyFrame,
            HasRandomAccessPoint: hasRandomAccessMarker,
            ProfileIdc: 0,
            LevelIdc: 0,
            PictureOrderCount: 0,
            HasAud: hasTemporalDelimiter,
            HasCra: false,
            HasVps: false,
            HasSps: hasSeqHeader,
            HasPps: false
        );
    }

    private static void InspectAv1FrameHeader(ReadOnlySpan<byte> payload, ref bool hasKeyFrame, ref bool hasIntraOnlyFrame)
    {
        if (payload.IsEmpty) return;
        int showExistingFrame = (payload[0] >> 7) & 0x01;
        if (showExistingFrame == 0)
        {
            int frameType = (payload[0] >> 5) & 0x03;
            if (frameType == 0)
            {
                hasKeyFrame = true;
            }
            else if (frameType == 2)
            {
                hasIntraOnlyFrame = true;
            }
        }
    }
}
