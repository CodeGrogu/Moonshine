using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.Control;

public enum ControlPacketType : ushort
{
    LossStats = 0x1401,
    Ping = 0x1402,
    Pong = 0x1403,
    BitrateUpdate = 0x1404,
    IdrRequest = 0x1405
}

/// <summary>
/// Encrypted control and loss feedback packet header.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ControlHeader
{
    public readonly ushort PacketTypeRaw;
    public readonly ushort PayloadLengthRaw;
    public readonly uint SequenceNumberRaw;

    public ControlPacketType PacketType => (ControlPacketType)BinaryPrimitives.ReverseEndianness(PacketTypeRaw);
    public ushort PayloadLength => BinaryPrimitives.ReverseEndianness(PayloadLengthRaw);
    public uint SequenceNumber => BinaryPrimitives.ReverseEndianness(SequenceNumberRaw);

    public const int Size = 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out ControlHeader header, out ReadOnlySpan<byte> payload)
    {
        if (source.Length < Size)
        {
            header = default;
            payload = default;
            return false;
        }

        header = MemoryMarshal.Read<ControlHeader>(source);
        payload = source[Size..];
        return true;
    }
}

/// <summary>
/// Loss statistics feedback packet sent back to Sunshine to trigger dynamic bitrate and IDR frame requests.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct LossStatsPayload
{
    public readonly uint LastGoodFrameIndex;
    public readonly uint LostPacketsTotal;
    public readonly uint RecoveredPacketsFec;
    public readonly uint RoundTripTimeUs;
}
