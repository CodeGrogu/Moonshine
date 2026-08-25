using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Video;

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine Video Packet Headers.
/// Wire format is 32 bytes packed big-endian.
/// </summary>
public static class MoonshineVideoPacketCodec
{
    public const int HeaderSize = 32;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHeader(in MoonshineVideoPacketHeader header, Span<byte> destination)
    {
        if (destination.Length < HeaderSize) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], header.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], header.FrameIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], header.PacketIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], header.TotalPackets);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], header.FecBlockIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination[24..26], header.PayloadSize);
        destination[26] = header.PacketType;
        destination[27] = (byte)header.Flags;
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], header.TotalFrameBytes);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadHeader(ReadOnlySpan<byte> source, out MoonshineVideoPacketHeader header)
    {
        header = default;
        if (source.Length < HeaderSize) return false;

        uint streamId = BinaryPrimitives.ReadUInt32BigEndian(source[0..4]);
        ulong frameIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]);
        uint packetIndex = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        uint totalPackets = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]);
        uint fecBlockIndex = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]);
        ushort payloadSize = BinaryPrimitives.ReadUInt16BigEndian(source[24..26]);
        byte packetType = source[26];
        var flags = (MoonshineVideoAttributes)source[27];
        uint totalFrameBytes = BinaryPrimitives.ReadUInt32BigEndian(source[28..32]);

        if (streamId == 0 || totalPackets == 0 || totalPackets > 65535 || packetType > 1 || 
            payloadSize == 0 || payloadSize > 65507 || totalFrameBytes == 0 ||
            (packetType == 0 && packetIndex >= totalPackets))
        {
            return false;
        }

        header = new MoonshineVideoPacketHeader
        {
            StreamId = streamId,
            FrameIndex = frameIndex,
            PacketIndex = packetIndex,
            TotalPackets = totalPackets,
            FecBlockIndex = fecBlockIndex,
            PayloadSize = payloadSize,
            PacketType = packetType,
            Flags = flags,
            TotalFrameBytes = totalFrameBytes
        };
        return true;
    }

}
