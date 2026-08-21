using System.Buffers.Binary;

namespace Moonshine.Protocol.Audio;

/// <summary>
/// Low-overhead binary RTP packet parser and serializer for client-to-host microphone backchannel audio.
/// Operates with zero GC allocations in streaming hot paths.
/// </summary>
public readonly ref struct MicAudioPacket
{
    public const int RtpHeaderSize = 12;
    public const byte DefaultPayloadType = 98;

    public byte PayloadType { get; init; }
    public bool Marker { get; init; }
    public ushort SequenceNumber { get; init; }
    public uint Timestamp { get; init; }
    public uint Ssrc { get; init; }
    public ReadOnlySpan<byte> Payload { get; init; }

    /// <summary>
    /// Attempts to parse an incoming RTP microphone datagram.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> datagram, out MicAudioPacket packet)
    {
        packet = default;
        if (datagram.Length < RtpHeaderSize)
        {
            return false;
        }

        byte b0 = datagram[0];
        byte version = (byte)((b0 >> 6) & 0x03);
        if (version != 2)
        {
            return false;
        }

        byte b1 = datagram[1];
        bool marker = (b1 & 0x80) != 0;
        byte payloadType = (byte)(b1 & 0x7F);

        ushort sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(2, 2));
        uint timestamp = BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(4, 4));
        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(8, 4));

        ReadOnlySpan<byte> payload = datagram.Slice(RtpHeaderSize);

        packet = new MicAudioPacket
        {
            PayloadType = payloadType,
            Marker = marker,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            Ssrc = ssrc,
            Payload = payload
        };

        return true;
    }

    /// <summary>
    /// Writes an RTP microphone packet into the provided destination buffer with zero GC allocations.
    /// </summary>
    public static bool TryWrite(
        ReadOnlySpan<byte> opusPayload,
        ushort sequenceNumber,
        uint timestamp,
        uint ssrc,
        bool marker,
        byte payloadType,
        Span<byte> destination,
        out int bytesWritten
    )
    {
        bytesWritten = 0;
        if (destination.Length < RtpHeaderSize + opusPayload.Length)
        {
            return false;
        }

        destination[0] = 0x80; // V=2, P=0, X=0, CC=0
        destination[1] = (byte)((marker ? 0x80 : 0x00) | (payloadType & 0x7F));

        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), sequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), ssrc);

        opusPayload.CopyTo(destination.Slice(RtpHeaderSize));
        bytesWritten = RtpHeaderSize + opusPayload.Length;

        return true;
    }
}
