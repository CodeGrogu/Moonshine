using System;

namespace Moonshine.Host.Encoding;

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

        return codec switch
        {
            VideoCodec.H264 => ValidateH264(bitstream, out isKeyframe),
            VideoCodec.Hevc or VideoCodec.HevcMain10 => ValidateHevc(bitstream, out isKeyframe),
            VideoCodec.Av1 => ValidateAv1(bitstream, out isKeyframe),
            _ => bitstream.Length > 0
        };
    }

    private static bool ValidateH264(ReadOnlySpan<byte> bitstream, out bool isKeyframe)
    {
        isKeyframe = false;
        int offset = 0;
        bool foundValidNalu = false;

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

                    // H.264 NAL Unit Types: 5 = IDR Slice, 7 = SPS, 8 = PPS
                    if (nalUnitType == 5 || nalUnitType == 7 || nalUnitType == 8)
                    {
                        isKeyframe = true;
                    }
                }
                offset += startCodeLen;
            }
            else
            {
                offset++;
            }
        }

        return foundValidNalu;
    }

    private static bool ValidateHevc(ReadOnlySpan<byte> bitstream, out bool isKeyframe)
    {
        isKeyframe = false;
        int offset = 0;
        bool foundValidNalu = false;

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

                    // HEVC NAL Unit Types: 19 = IDR_W_RADL, 20 = IDR_N_LP, 32 = VPS, 33 = SPS, 34 = PPS
                    if (nalUnitType == 19 || nalUnitType == 20 || nalUnitType == 32 || nalUnitType == 33 || nalUnitType == 34)
                    {
                        isKeyframe = true;
                    }
                }
                offset += startCodeLen;
            }
            else
            {
                offset++;
            }
        }

        return foundValidNalu;
    }

    private static bool ValidateAv1(ReadOnlySpan<byte> bitstream, out bool isKeyframe)
    {
        isKeyframe = false;
        if (bitstream.Length < 1) return false;

        int offset = 0;
        bool foundValidObu = false;

        while (offset < bitstream.Length)
        {
            byte header = bitstream[offset];
            // Forbidden bit must be 0
            if ((header & 0x80) != 0)
            {
                return false;
            }

            int obuType = (header >> 3) & 0x0F;
            // Valid OBU types are 1..8:
            // 1 = Sequence Header, 2 = Temporal Delimiter, 3 = Frame Header, 4 = Tile Group,
            // 5 = Metadata, 6 = Frame, 7 = Redundant Frame Header, 8 = Tile List
            if (obuType < 1 || obuType > 8)
            {
                return false;
            }

            foundValidObu = true;
            if (obuType == 1) // Sequence Header signifies keyframe
            {
                isKeyframe = true;
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

        return foundValidObu;
    }
}
