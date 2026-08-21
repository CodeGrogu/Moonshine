using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Audio;

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine Audio Packet Headers.
/// Wire format is 24 bytes packed big-endian.
/// </summary>
public static class MoonshineAudioPacketCodec
{
    public const int HeaderSize = 24;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHeader(in MoonshineAudioPacketHeader header, Span<byte> destination)
    {
        if (destination.Length < HeaderSize) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], header.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], header.SampleIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], header.SampleRate);
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..18], header.FrameDurationUs);
        BinaryPrimitives.WriteUInt16BigEndian(destination[18..20], header.PayloadSize);
        destination[20] = header.Channels;
        destination[21] = (byte)header.Codec;
        BinaryPrimitives.WriteUInt16BigEndian(destination[22..24], header.Reserved);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadHeader(ReadOnlySpan<byte> source, out MoonshineAudioPacketHeader header)
    {
        header = default;
        if (source.Length < HeaderSize) return false;

        header.StreamId = BinaryPrimitives.ReadUInt32BigEndian(source[0..4]);
        header.SampleIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]);
        header.SampleRate = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        header.FrameDurationUs = BinaryPrimitives.ReadUInt16BigEndian(source[16..18]);
        header.PayloadSize = BinaryPrimitives.ReadUInt16BigEndian(source[18..20]);
        header.Channels = source[20];
        header.Codec = (MoonshineAudioCodec)source[21];
        header.Reserved = BinaryPrimitives.ReadUInt16BigEndian(source[22..24]);
        return true;
    }
}
