using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Feedback;

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine feedback and QoS protocol packets.
/// Encodes and decodes FeedbackLossStats and IdrRequest datagrams with standard MSHN envelope headers.
/// </summary>
public static class MoonshineFeedbackCodec
{
    public const int LossStatsPayloadSize = 40;
    public const int IdrRequestPayloadSize = 16;

    public const int LossStatsPacketSize = MoonshineProtocolConstants.HeaderSize + LossStatsPayloadSize; // 32 + 40 = 72 bytes
    public const int IdrRequestPacketSize = MoonshineProtocolConstants.HeaderSize + IdrRequestPayloadSize; // 32 + 16 = 48 bytes

    /// <summary>
    /// Encodes a complete framed FeedbackLossStats datagram into the destination buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteLossStats(
        in MoonshineFeedbackLossStatsPayload payload,
        Span<byte> destination,
        out int bytesWritten,
        ulong sessionId = 0,
        uint sequenceNumber = 0)
    {
        bytesWritten = 0;
        if (destination.Length < LossStatsPacketSize)
        {
            return false;
        }

        ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.FeedbackLossStats,
            PayloadSize: LossStatsPayloadSize,
            SequenceNumber: sequenceNumber,
            SessionId: sessionId,
            TimestampUs: timestampUs
        );

        if (!MoonshineProtocolCodec.TryWriteHeader(in header, destination))
        {
            return false;
        }

        if (!MoonshineProtocolCodec.TryWriteFeedbackLossStats(in payload, destination[MoonshineProtocolConstants.HeaderSize..]))
        {
            return false;
        }

        bytesWritten = LossStatsPacketSize;
        return true;
    }

    /// <summary>
    /// Parses a complete framed FeedbackLossStats datagram from the source buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadLossStats(
        ReadOnlySpan<byte> source,
        out MoonshinePacketHeader header,
        out MoonshineFeedbackLossStatsPayload payload)
    {
        header = default;
        payload = default;

        if (source.Length < LossStatsPacketSize)
        {
            return MoonshineErrorCode.BufferTooSmall;
        }

        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(source, out header);
        if (err != MoonshineErrorCode.Success)
        {
            return err;
        }

        if (header.MessageType != MoonshineMessageType.FeedbackLossStats)
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if (header.PayloadSize < LossStatsPayloadSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        return MoonshineProtocolCodec.TryReadFeedbackLossStats(source[MoonshineProtocolConstants.HeaderSize..], out payload);
    }

    /// <summary>
    /// Encodes a complete framed IdrRequest datagram into the destination buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteIdrRequest(
        in MoonshineIdrRequestPayload payload,
        Span<byte> destination,
        out int bytesWritten,
        ulong sessionId = 0,
        uint sequenceNumber = 0)
    {
        bytesWritten = 0;
        if (destination.Length < IdrRequestPacketSize)
        {
            return false;
        }

        ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.IdrRequest,
            PayloadSize: IdrRequestPayloadSize,
            SequenceNumber: sequenceNumber,
            SessionId: sessionId,
            TimestampUs: timestampUs
        );

        if (!MoonshineProtocolCodec.TryWriteHeader(in header, destination))
        {
            return false;
        }

        if (!MoonshineProtocolCodec.TryWriteIdrRequest(in payload, destination[MoonshineProtocolConstants.HeaderSize..]))
        {
            return false;
        }

        bytesWritten = IdrRequestPacketSize;
        return true;
    }

    /// <summary>
    /// Parses a complete framed IdrRequest datagram from the source buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadIdrRequest(
        ReadOnlySpan<byte> source,
        out MoonshinePacketHeader header,
        out MoonshineIdrRequestPayload payload)
    {
        header = default;
        payload = default;

        if (source.Length < IdrRequestPacketSize)
        {
            return MoonshineErrorCode.BufferTooSmall;
        }

        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(source, out header);
        if (err != MoonshineErrorCode.Success)
        {
            return err;
        }

        if (header.MessageType != MoonshineMessageType.IdrRequest)
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if (header.PayloadSize < IdrRequestPayloadSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        return MoonshineProtocolCodec.TryReadIdrRequest(source[MoonshineProtocolConstants.HeaderSize..], out payload);
    }
}
