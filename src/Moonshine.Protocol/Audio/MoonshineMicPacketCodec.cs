using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Audio;

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine Microphone Packet Headers.
/// Wire format is 20 bytes packed big-endian.
/// </summary>
public static class MoonshineMicPacketCodec
{
    public const int HeaderSize = 20;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHeader(in MoonshineMicPacketHeader header, Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], header.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], header.SampleIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination[12..14], header.PayloadSize);
        destination[14] = header.Channels;
        destination[15] = (byte)header.Codec;
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], header.SampleRate);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadHeader(ReadOnlySpan<byte> source, out MoonshineMicPacketHeader header)
    {
        header = default;
        if (source.Length < HeaderSize)
        {
            return false;
        }

        header.StreamId = BinaryPrimitives.ReadUInt32BigEndian(source[0..4]);
        header.SampleIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]);
        header.PayloadSize = BinaryPrimitives.ReadUInt16BigEndian(source[12..14]);
        header.Channels = source[14];
        header.Codec = (MoonshineAudioCodec)source[15];
        header.SampleRate = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]);
        return true;
    }
}
