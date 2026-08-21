using System.Buffers.Binary;

namespace Moonshine.Host.Audio;

/// <summary>
/// Ultra-low latency, zero-allocation RTP Audio Packetiser.
/// Encapsulates multi-channel PCM or Opus compressed audio frames with standard
/// 12-byte RFC 3550 RTP headers, monotonic timestamps, and sequential tracking.
/// </summary>
public sealed class RtpAudioPacketiser
{
    public const int RtpHeaderSize = 12;
    private readonly byte _payloadType;
    private readonly uint _ssrc;
    private ushort _sequenceNumber;
    private readonly Lock _lock = new();

    public byte PayloadType => _payloadType;
    public uint Ssrc => _ssrc;
    public ushort CurrentSequenceNumber => Volatile.Read(ref _sequenceNumber);

    public RtpAudioPacketiser(byte payloadType = 97, uint ssrc = 0x12345678, ushort initialSeq = 0)
    {
        _payloadType = payloadType;
        _ssrc = ssrc;
        _sequenceNumber = initialSeq;
    }

    /// <summary>
    /// Packetises an audio payload into an RTP packet with zero GC allocations.
    /// </summary>
    public bool TryPacketise(
        ReadOnlySpan<byte> audioPayload,
        uint timestamp,
        bool marker,
        Span<byte> outRtpPacket,
        out int bytesWritten
    )
    {
        bytesWritten = 0;
        if (outRtpPacket.Length < RtpHeaderSize + audioPayload.Length)
        {
            return false;
        }

        ushort seq;
        lock (_lock)
        {
            seq = _sequenceNumber++;
        }

        // Byte 0: Version=2 (0x80), Padding=0, Extension=0, CSRC count=0
        outRtpPacket[0] = 0x80;

        // Byte 1: Marker bit (bit 7) + 7-bit Payload Type
        outRtpPacket[1] = (byte)((marker ? 0x80 : 0x00) | (_payloadType & 0x7F));

        // Bytes 2-3: 16-bit Monotonic Sequence Number (Big Endian)
        BinaryPrimitives.WriteUInt16BigEndian(outRtpPacket.Slice(2, 2), seq);

        // Bytes 4-7: 32-bit Audio Timestamp (Big Endian)
        BinaryPrimitives.WriteUInt32BigEndian(outRtpPacket.Slice(4, 4), timestamp);

        // Bytes 8-11: 32-bit SSRC (Big Endian)
        BinaryPrimitives.WriteUInt32BigEndian(outRtpPacket.Slice(8, 4), _ssrc);

        // Bytes 12+: Payload
        audioPayload.CopyTo(outRtpPacket.Slice(RtpHeaderSize));
        bytesWritten = RtpHeaderSize + audioPayload.Length;

        return true;
    }
}
