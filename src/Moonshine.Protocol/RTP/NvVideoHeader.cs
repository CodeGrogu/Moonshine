using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Moonshine.Protocol.RTP;

/// <summary>
/// Packed 16-byte GameStream <c>NV_VIDEO_PACKET</c> header.
/// The header follows four reserved bytes after RTP in an unencrypted Sunshine/GameStream video datagram.
/// All multi-byte fields are little-endian.
/// </summary>
public readonly record struct NvVideoHeader(
    uint RawStreamPacketIndex,
    uint FrameIndex,
    byte Flags,
    byte ExtraFlags,
    byte MultiFecFlags,
    byte MultiFecBlocks,
    uint FecInfo)
{
    public const byte ContainsPictureDataFlag = 0x01;
    public const byte EndOfFrameFlag = 0x02;
    public const byte StartOfFrameFlag = 0x04;

    public const int Size = 16;

    /// <summary>The GameStream stream packet index occupies the lower 24 bits after the protocol's eight-bit right shift.</summary>
    public uint StreamPacketIndex => (RawStreamPacketIndex >> 8) & 0x00FF_FFFF;
    public bool ContainsPictureData => (Flags & ContainsPictureDataFlag) != 0;
    public bool IsStartOfFrame => (Flags & StartOfFrameFlag) != 0;
    public bool IsEndOfFrame => (Flags & EndOfFrameFlag) != 0;
    public byte FecBlockIndex => (byte)((MultiFecBlocks >> 4) & 0x03);
    public byte LastFecBlockIndex => (byte)((MultiFecBlocks >> 6) & 0x03);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out NvVideoHeader header, out ReadOnlySpan<byte> payload)
    {
        if (source.Length < Size)
        {
            header = default;
            payload = default;
            return false;
        }

        header = new NvVideoHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(source),
            BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
            source[8],
            source[9],
            source[10],
            source[11],
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]));
        payload = source[Size..];
        return true;
    }
}
