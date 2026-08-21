using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.RTP;

/// <summary>
/// NVSTREAM video packet payload header (RFC 3550 payload extension for GameStream and Sunshine video streams).
/// Encapsulates frame index, multi-packet slice numbering, total frame packets, and slice flags.
/// Layout is 12 bytes in little-endian byte order.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct NvVideoHeader
{
    public readonly uint FrameIndex;
    public readonly uint PacketIndex;
    public readonly ushort TotalPackets;
    public readonly byte Flags; // 0x01 = Start, 0x02 = End, 0x04 = Keyframe
    public readonly byte Reserved;

    public const int Size = 12;

    public bool IsStartOfFrame => (Flags & 0x01) != 0;
    public bool IsEndOfFrame => (Flags & 0x02) != 0;
    public bool IsKeyframe => (Flags & 0x04) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out NvVideoHeader header, out ReadOnlySpan<byte> payload)
    {
        if (source.Length < Size)
        {
            header = default;
            payload = default;
            return false;
        }

        header = MemoryMarshal.Read<NvVideoHeader>(source);
        if (header.TotalPackets == 0 || header.PacketIndex >= header.TotalPackets)
        {
            header = default;
            payload = default;
            return false;
        }

        payload = source[Size..];
        return true;
    }
}
