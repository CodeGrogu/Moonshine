using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.RTP;

/// <summary>
/// Blittable representation of an RTP Header (RFC 3550) used for low-latency GameStream video packets.
/// Layout is 12 bytes in big-endian network byte order.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RtpHeader
{
    public readonly byte Flags;
    public readonly byte PayloadType;
    public readonly ushort SequenceNumberRaw;
    public readonly uint TimestampRaw;
    public readonly uint SsrcRaw;

    public int Version => (Flags >> 6) & 0x03;
    public bool HasPadding => (Flags & 0x20) != 0;
    public bool HasExtension => (Flags & 0x10) != 0;
    public int CsrcCount => Flags & 0x0F;
    public bool Marker => (PayloadType & 0x80) != 0;
    public byte PayloadId => (byte)(PayloadType & 0x7F);

    public ushort SequenceNumber => BinaryPrimitives.ReverseEndianness(SequenceNumberRaw);
    public uint Timestamp => BinaryPrimitives.ReverseEndianness(TimestampRaw);
    public uint Ssrc => BinaryPrimitives.ReverseEndianness(SsrcRaw);

    public const int Size = 12;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParse(ReadOnlySpan<byte> source, out RtpHeader header, out ReadOnlySpan<byte> payload)
    {
        if (source.Length < Size)
        {
            header = default;
            payload = default;
            return false;
        }

        header = MemoryMarshal.Read<RtpHeader>(source);
        if (header.Version != 2)
        {
            header = default;
            payload = default;
            return false;
        }

        int offset = Size + (header.CsrcCount * 4);
        if (offset > source.Length)
        {
            header = default;
            payload = default;
            return false;
        }

        if (header.HasExtension)
        {
            if (source.Length - offset < 4)
            {
                header = default;
                payload = default;
                return false;
            }

            int extensionLength = BinaryPrimitives.ReadUInt16BigEndian(source[(offset + 2)..]) * 4;
            offset += 4;
            if (extensionLength > source.Length - offset)
            {
                header = default;
                payload = default;
                return false;
            }

            offset += extensionLength;
        }

        int payloadLength = source.Length - offset;
        if (header.HasPadding)
        {
            if (payloadLength == 0)
            {
                header = default;
                payload = default;
                return false;
            }

            int paddingLength = source[^1];
            if (paddingLength == 0 || paddingLength > payloadLength)
            {
                header = default;
                payload = default;
                return false;
            }

            payloadLength -= paddingLength;
        }

        payload = source.Slice(offset, payloadLength);
        return true;
    }
}

/// <summary>
/// Sequence number unwrapper to handle 16-bit to 64-bit monotonically increasing packet counters.
/// </summary>
public struct RtpSequenceUnwrapper
{
    private uint _highestSeq;
    private uint _cycles;
    private bool _initialized;

    public ulong Unwrap(ushort sequenceNumber)
    {
        if (!_initialized)
        {
            _highestSeq = sequenceNumber;
            _initialized = true;
            return sequenceNumber;
        }

        short diff = (short)(sequenceNumber - (ushort)_highestSeq);

        if (diff > 0)
        {
            if (sequenceNumber < _highestSeq)
            {
                _cycles += 0x10000;
            }
            _highestSeq = sequenceNumber;
            return _cycles + sequenceNumber;
        }
        else if (diff < 0)
        {
            if (sequenceNumber > _highestSeq && _cycles >= 0x10000)
            {
                return (_cycles - 0x10000) + sequenceNumber;
            }
            return _cycles + sequenceNumber;
        }

        return _cycles + sequenceNumber;
    }

    public void Reset()
    {
        _highestSeq = 0;
        _cycles = 0;
        _initialized = false;
    }
}
