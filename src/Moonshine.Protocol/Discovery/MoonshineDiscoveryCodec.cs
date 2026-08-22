using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Discovery;

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine LAN host discovery protocol packets.
/// Encodes and decodes DiscoveryProbe, DiscoveryAnnouncement, and DiscoveryResponse datagrams.
/// </summary>
public static class MoonshineDiscoveryCodec
{
    public const int ProbePayloadSize = 36;
    public const int AnnouncementPayloadSize = 192;
    public const int ProbePacketSize = MoonshineProtocolConstants.HeaderSize + ProbePayloadSize; // 68 bytes
    public const int AnnouncementPacketSize = MoonshineProtocolConstants.HeaderSize + AnnouncementPayloadSize; // 224 bytes

    /// <summary>
    /// Encodes a complete framed DiscoveryProbe datagram.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteProbe(
        in MoonshineDiscoveryProbePayload payload,
        Span<byte> destination,
        out int bytesWritten,
        uint sequenceNumber = 0)
    {
        bytesWritten = 0;
        if (destination.Length < ProbePacketSize)
        {
            return false;
        }

        ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.DiscoveryProbe,
            PayloadSize: ProbePayloadSize,
            SequenceNumber: sequenceNumber,
            SessionId: 0,
            TimestampUs: timestampUs
        );

        if (!MoonshineProtocolCodec.TryWriteHeader(header, destination))
        {
            return false;
        }

        Span<byte> payloadSpan = destination.Slice(MoonshineProtocolConstants.HeaderSize, ProbePayloadSize);
        BinaryPrimitives.WriteUInt16BigEndian(payloadSpan[..2], payload.ClientVersionMajor);
        BinaryPrimitives.WriteUInt16BigEndian(payloadSpan[2..4], payload.ClientVersionMinor);
        payload.ClientUuid.AsSpan().CopyTo(payloadSpan[4..20]);
        BinaryPrimitives.WriteUInt32BigEndian(payloadSpan[20..24], (uint)payload.DesiredCapabilities);
        BinaryPrimitives.WriteUInt32BigEndian(payloadSpan[24..28], payload.Reserved);
        BinaryPrimitives.WriteUInt64BigEndian(payloadSpan[28..36], payload.ProbeNonce);

        bytesWritten = ProbePacketSize;
        return true;
    }

    /// <summary>
    /// Decodes a framed DiscoveryProbe datagram.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadProbe(
        ReadOnlySpan<byte> source,
        out MoonshinePacketHeader header,
        out MoonshineDiscoveryProbePayload payload)
    {
        payload = default;
        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(source, out header);
        if (err != MoonshineErrorCode.Success)
        {
            return err;
        }

        if (header.MessageType != MoonshineMessageType.DiscoveryProbe)
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if (header.PayloadSize < ProbePayloadSize || source.Length < ProbePacketSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        ReadOnlySpan<byte> payloadSpan = source.Slice(MoonshineProtocolConstants.HeaderSize, ProbePayloadSize);
        payload = new MoonshineDiscoveryProbePayload
        {
            ClientVersionMajor = BinaryPrimitives.ReadUInt16BigEndian(payloadSpan[..2]),
            ClientVersionMinor = BinaryPrimitives.ReadUInt16BigEndian(payloadSpan[2..4]),
            ClientUuid = new MoonshineUuid128(payloadSpan[4..20]),
            DesiredCapabilities = (MoonshineCapabilities)BinaryPrimitives.ReadUInt32BigEndian(payloadSpan[20..24]),
            Reserved = BinaryPrimitives.ReadUInt32BigEndian(payloadSpan[24..28]),
            ProbeNonce = BinaryPrimitives.ReadUInt64BigEndian(payloadSpan[28..36])
        };

        return MoonshineErrorCode.Success;
    }

    /// <summary>
    /// Encodes a complete framed DiscoveryAnnouncement datagram.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteAnnouncement(
        in MoonshineDiscoveryAnnouncementPayload payload,
        Span<byte> destination,
        out int bytesWritten,
        uint sequenceNumber = 0)
    {
        return TryWriteAnnouncementOrResponse(
            payload,
            MoonshineMessageType.DiscoveryAnnouncement,
            destination,
            out bytesWritten,
            sequenceNumber,
            sessionId: 0);
    }

    /// <summary>
    /// Encodes a complete framed DiscoveryResponse datagram.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteResponse(
        in MoonshineDiscoveryAnnouncementPayload payload,
        Span<byte> destination,
        out int bytesWritten,
        uint sequenceNumber = 0,
        ulong sessionId = 0)
    {
        return TryWriteAnnouncementOrResponse(
            payload,
            MoonshineMessageType.DiscoveryResponse,
            destination,
            out bytesWritten,
            sequenceNumber,
            sessionId);
    }

    private static unsafe bool TryWriteAnnouncementOrResponse(
        in MoonshineDiscoveryAnnouncementPayload payload,
        MoonshineMessageType messageType,
        Span<byte> destination,
        out int bytesWritten,
        uint sequenceNumber,
        ulong sessionId)
    {
        bytesWritten = 0;
        if (destination.Length < AnnouncementPacketSize)
        {
            return false;
        }

        ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: messageType,
            PayloadSize: AnnouncementPayloadSize,
            SequenceNumber: sequenceNumber,
            SessionId: sessionId,
            TimestampUs: timestampUs
        );

        if (!MoonshineProtocolCodec.TryWriteHeader(header, destination))
        {
            return false;
        }

        Span<byte> p = destination.Slice(MoonshineProtocolConstants.HeaderSize, AnnouncementPayloadSize);
        BinaryPrimitives.WriteUInt16BigEndian(p[..2], payload.HostVersionMajor);
        BinaryPrimitives.WriteUInt16BigEndian(p[2..4], payload.HostVersionMinor);
        payload.HostUuid.AsSpan().CopyTo(p[4..20]);
        BinaryPrimitives.WriteUInt32BigEndian(p[20..24], (uint)payload.SupportedCapabilities);
        BinaryPrimitives.WriteUInt32BigEndian(p[24..28], payload.ControlTcpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[28..32], payload.DiscoveryUdpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[32..36], payload.VideoUdpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[36..40], payload.AudioUdpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[40..44], payload.ControlFeedbackUdpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[44..48], payload.MicUdpPort);
        BinaryPrimitives.WriteUInt32BigEndian(p[48..52], payload.MaxBitrateKbps);
        p[52] = payload.SupportsHdr10;
        p[53] = payload.SupportsVirtualAudio;
        p[54] = payload.SupportsMicBackchannel;
        p[55] = payload.IsPaired;

        fixed (byte* hostPtr = payload.Hostname)
        {
            new ReadOnlySpan<byte>(hostPtr, 64).CopyTo(p.Slice(56, 64));
        }

        fixed (byte* gpuPtr = payload.GpuName)
        {
            new ReadOnlySpan<byte>(gpuPtr, 64).CopyTo(p.Slice(120, 64));
        }

        BinaryPrimitives.WriteUInt64BigEndian(p.Slice(184, 8), payload.AdvertisementNonce);

        bytesWritten = AnnouncementPacketSize;
        return true;
    }

    /// <summary>
    /// Decodes a framed DiscoveryAnnouncement or DiscoveryResponse datagram.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe MoonshineErrorCode TryReadAnnouncementOrResponse(
        ReadOnlySpan<byte> source,
        out MoonshinePacketHeader header,
        out MoonshineDiscoveryAnnouncementPayload payload)
    {
        payload = default;
        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(source, out header);
        if (err != MoonshineErrorCode.Success)
        {
            return err;
        }

        if (header.MessageType != MoonshineMessageType.DiscoveryAnnouncement &&
            header.MessageType != MoonshineMessageType.DiscoveryResponse)
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if (header.PayloadSize < AnnouncementPayloadSize || source.Length < AnnouncementPacketSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        ReadOnlySpan<byte> p = source.Slice(MoonshineProtocolConstants.HeaderSize, AnnouncementPayloadSize);
        payload = new MoonshineDiscoveryAnnouncementPayload
        {
            HostVersionMajor = BinaryPrimitives.ReadUInt16BigEndian(p[..2]),
            HostVersionMinor = BinaryPrimitives.ReadUInt16BigEndian(p[2..4]),
            HostUuid = new MoonshineUuid128(p[4..20]),
            SupportedCapabilities = (MoonshineCapabilities)BinaryPrimitives.ReadUInt32BigEndian(p[20..24]),
            ControlTcpPort = BinaryPrimitives.ReadUInt32BigEndian(p[24..28]),
            DiscoveryUdpPort = BinaryPrimitives.ReadUInt32BigEndian(p[28..32]),
            VideoUdpPort = BinaryPrimitives.ReadUInt32BigEndian(p[32..36]),
            AudioUdpPort = BinaryPrimitives.ReadUInt32BigEndian(p[36..40]),
            ControlFeedbackUdpPort = BinaryPrimitives.ReadUInt32BigEndian(p[40..44]),
            MicUdpPort = BinaryPrimitives.ReadUInt32BigEndian(p[44..48]),
            MaxBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(p[48..52]),
            SupportsHdr10 = p[52],
            SupportsVirtualAudio = p[53],
            SupportsMicBackchannel = p[54],
            IsPaired = p[55]
        };

        fixed (byte* hostPtr = payload.Hostname)
        {
            p.Slice(56, 64).CopyTo(new Span<byte>(hostPtr, 64));
        }

        fixed (byte* gpuPtr = payload.GpuName)
        {
            p.Slice(120, 64).CopyTo(new Span<byte>(gpuPtr, 64));
        }

        payload.AdvertisementNonce = BinaryPrimitives.ReadUInt64BigEndian(p.Slice(184, 8));

        return MoonshineErrorCode.Success;
    }

    /// <summary>
    /// Helper to safely copy a managed string into a fixed UTF-8 buffer.
    /// </summary>
    public static unsafe void SetFixedUtf8String(byte* destination, int capacity, string value)
    {
        var destSpan = new Span<byte>(destination, capacity);
        destSpan.Clear();
        if (string.IsNullOrEmpty(value) || capacity <= 1) return;

        int maxBytes = capacity - 1;
        var encoder = Encoding.UTF8.GetEncoder();
        encoder.Convert(value.AsSpan(), destSpan[..maxBytes], flush: true, out _, out int bytesUsed, out _);
        destSpan[bytesUsed] = 0; // Null terminator
    }

    /// <summary>
    /// Helper to read a null-terminated UTF-8 string from a fixed buffer without allocations when empty.
    /// </summary>
    public static unsafe string GetFixedUtf8String(byte* source, int capacity)
    {
        var span = new ReadOnlySpan<byte>(source, capacity);
        int nullIdx = span.IndexOf((byte)0);
        if (nullIdx == 0) return string.Empty;
        if (nullIdx > 0) span = span[..nullIdx];

        return Encoding.UTF8.GetString(span);
    }
}
