using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.RTP;

public enum RtcpPacketType : byte
{
    SenderReport = 200,
    ReceiverReport = 201,
    SourceDescription = 202,
    Bye = 203,
    ApplicationDefined = 204,
    TransportFeedback = 205,
    PayloadSpecificFeedback = 206
}

/// <summary>
/// RTCP loss and quality statistics feedback packet.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RtcpLossStatsPacket(
    uint Ssrc,
    uint PacketsReceived,
    uint PacketsLost,
    uint PacketsRecovered,
    uint LastSequenceNumber,
    uint JitterMicros)
{
    public const int PacketSize = 28;

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        // RTCP Header: V=2, P=0, Count=1, PT=201 (Receiver Report)
        destination[0] = 0x81;
        destination[1] = (byte)RtcpPacketType.ReceiverReport;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)(PacketSize / 4 - 1));

        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), Ssrc);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), PacketsReceived);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(12, 4), PacketsLost);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), PacketsRecovered);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(20, 4), LastSequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(24, 4), JitterMicros);

        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out RtcpLossStatsPacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        byte pt = source[1];
        if (pt != (byte)RtcpPacketType.ReceiverReport && pt != (byte)RtcpPacketType.ApplicationDefined)
        {
            packet = default;
            return false;
        }

        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4, 4));
        uint received = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8, 4));
        uint lost = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(12, 4));
        uint recovered = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(16, 4));
        uint lastSeq = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(20, 4));
        uint jitter = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(24, 4));

        packet = new RtcpLossStatsPacket(ssrc, received, lost, recovered, lastSeq, jitter);
        return true;
    }

    /// <summary>
    /// Calculates the unrecoverable packet loss fraction (0.0 to 1.0).
    /// </summary>
    public double UnrecoverableLossRate
    {
        get
        {
            uint totalExpected = PacketsReceived + PacketsLost;
            if (totalExpected == 0 || PacketsLost <= PacketsRecovered) return 0.0;

            uint unrecoverable = PacketsLost - PacketsRecovered;
            return Math.Clamp((double)unrecoverable / totalExpected, 0.0, 1.0);
        }
    }
}

/// <summary>
/// RTCP Picture Loss Indication (PLI) / Instantaneous Decoder Refresh (IDR) Keyframe Request.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RtcpIdrRequestPacket(uint SenderSsrc, uint MediaSsrc)
{
    public const int PacketSize = 12;

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < PacketSize) return -1;

        // Header: V=2, P=0, FMT=1 (PLI), PT=206 (Payload-Specific Feedback)
        destination[0] = 0x81;
        destination[1] = (byte)RtcpPacketType.PayloadSpecificFeedback;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), (ushort)(PacketSize / 4 - 1));

        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), SenderSsrc);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), MediaSsrc);

        return PacketSize;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out RtcpIdrRequestPacket packet)
    {
        if (source.Length < PacketSize)
        {
            packet = default;
            return false;
        }

        byte pt = source[1];
        if (pt != (byte)RtcpPacketType.PayloadSpecificFeedback)
        {
            packet = default;
            return false;
        }

        uint senderSsrc = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4, 4));
        uint mediaSsrc = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8, 4));
        packet = new RtcpIdrRequestPacket(senderSsrc, mediaSsrc);
        return true;
    }
}
