using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Moonshine.Protocol.Contracts;

public static class MoonshineProtocolConstants
{
    public const uint Magic = 0x4D53484EU; // 'MSHN'
    public const ushort Version10 = 0x0001;
    public const int HeaderSize = 32;
}

public enum MoonshineMessageType : ushort
{
    None = 0x0000,

    // Control & Session
    Hello = 0x0101,
    HelloResponse = 0x0102,
    SessionSetup = 0x0103,
    SessionSetupResponse = 0x0104,
    KeepAlive = 0x0105,
    KeepAliveAck = 0x0106,
    Teardown = 0x0107,

    // Media
    VideoPacket = 0x0201,
    AudioPacket = 0x0301,
    MicPacket = 0x0401,

    // Feedback & QoS
    FeedbackLossStats = 0x0501,
    IdrRequest = 0x0502,

    // Input
    InputKeyboard = 0x0601,
    InputMouse = 0x0602,
    InputGamepad = 0x0603,

    // Telemetry
    TelemetryReport = 0x0701
}

public enum MoonshineErrorCode : uint
{
    Success = 0,
    InvalidMagic = 1,
    UnsupportedVersion = 2,
    MalformedHeader = 3,
    BufferTooSmall = 4,
    PayloadTruncated = 5,
    InvalidSession = 6,
    AuthenticationFailed = 7,
    StreamNotFound = 8,
    DuplicateSequence = 9,
    StaleTimestamp = 10,
    UnsupportedCodec = 11
}

[Flags]
public enum MoonshineCapabilities : uint
{
    None = 0,
    Av1 = 1 << 0,
    Hevc = 1 << 1,
    H264 = 1 << 2,
    Hdr10 = 1 << 3,
    Surround71 = 1 << 4,
    HighPollRateInput = 1 << 5,
    ReedSolomonFec = 1 << 6
}

[Flags]
public enum MoonshineVideoAttributes : byte
{
    None = 0,
    Keyframe = 1 << 0,
    FrameStart = 1 << 1,
    FrameEnd = 1 << 2,
    Hdr10Present = 1 << 3
}

public enum MoonshineVideoCodec : byte
{
    Unknown = 0,
    Av1 = 1,
    Hevc = 2,
    H264 = 3
}

public enum MoonshineAudioCodec : byte
{
    Unknown = 0,
    Opus = 1,
    Pcm16 = 2,
    Float32 = 3
}

public enum MoonshineColorFormat : byte
{
    Unknown = 0,
    Nv12 = 1,
    P010Hdr10 = 2
}

#pragma warning disable CA1051 // Visible instance fields in blittable wire structs

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MoonshinePacketHeader(
    uint Magic,
    ushort Version,
    MoonshineMessageType MessageType,
    uint PayloadSize,
    uint SequenceNumber,
    ulong SessionId,
    ulong TimestampUs);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineHelloPayload
{
    public ushort ClientVersionMajor;
    public ushort ClientVersionMinor;
    public MoonshineCapabilities CapabilitiesMask;
    public ulong ClientNonce;
    public Guid ClientUuid;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineHelloResponsePayload
{
    public ushort ServerVersionMajor;
    public ushort ServerVersionMinor;
    public MoonshineCapabilities NegotiatedCapabilities;
    public ulong AssignedSessionId;
    public ulong ServerNonce;
    public Guid ChallengeSalt;
    public uint SessionLeaseSeconds;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineSessionSetupPayload
{
    public uint VideoWidth;
    public uint VideoHeight;
    public uint VideoFps;
    public uint VideoBitrateKbps;
    public MoonshineVideoCodec VideoCodec;
    public MoonshineColorFormat VideoColorFormat;
    public byte AudioChannels;
    public MoonshineAudioCodec AudioCodec;
    public uint AudioSampleRate;
    public uint AudioBitrateKbps;
    public ushort ClientUdpVideoPort;
    public ushort ClientUdpAudioPort;
    public ushort ClientUdpFeedbackPort;
    public ushort Reserved;
    public uint MtuPayloadSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineSessionSetupResponsePayload
{
    public MoonshineErrorCode StatusCode;
    public uint VideoStreamId;
    public uint AudioStreamId;
    public uint FeedbackStreamId;
    public ushort HostUdpVideoPort;
    public ushort HostUdpAudioPort;
    public ushort HostUdpFeedbackPort;
    public ushort HostUdpInputPort;
    public uint NegotiatedMtu;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineVideoPacketHeader
{
    public uint StreamId;
    public ulong FrameIndex;
    public uint PacketIndex;
    public uint TotalPackets;
    public uint FecBlockIndex;
    public ushort PayloadSize;
    public byte PacketType; // 0: Data, 1: Parity
    public MoonshineVideoAttributes Flags;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineAudioPacketHeader
{
    public uint StreamId;
    public ulong SampleIndex;
    public uint SampleRate;
    public ushort FrameDurationUs;
    public ushort PayloadSize;
    public byte Channels;
    public MoonshineAudioCodec Codec;
    public ushort Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineMicPacketHeader
{
    public uint StreamId;
    public ulong SampleIndex;
    public ushort PayloadSize;
    public byte Channels;
    public MoonshineAudioCodec Codec;
    public uint SampleRate;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineFeedbackLossStatsPayload
{
    public uint StreamId;
    public ulong LastReceivedFrameIndex;
    public uint PacketsReceived;
    public uint PacketsLost;
    public uint PacketsRecoveredFec;
    public uint RoundTripTimeUs;
    public uint JitterUs;
    public uint EstimatedBandwidthKbps;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineIdrRequestPayload
{
    public uint StreamId;
    public ulong LastValidFrameIndex;
    public uint ReasonCode;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineInputKeyboardPayload
{
    public ushort KeyCode;
    public ushort ScanCode;
    public byte IsDown;
    public byte Modifiers;
    public ushort Reserved;
    public uint TimestampOffsetUs;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineInputMousePayload
{
    public int X;
    public int Y;
    public short WheelDeltaY;
    public short WheelDeltaX;
    public ushort ButtonFlags;
    public byte IsAbsolute;
    public byte Reserved;
    public uint TimestampOffsetUs;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineInputGamepadPayload
{
    public byte GamepadIndex;
    public byte Reserved;
    public ushort ButtonMask;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLx;
    public short ThumbLy;
    public short ThumbRx;
    public short ThumbRy;
    public ushort MotorLeft;
    public ushort MotorRight;
    public uint TimestampOffsetUs;
    public ushort Reserved2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineTelemetryReportPayload
{
    public uint EncodeLatencyUs;
    public uint DecodeLatencyUs;
    public uint RenderLatencyUs;
    public uint NetworkLatencyUs;
    public uint FramesRendered;
    public uint FramesDropped;
    public uint FecRecoveredFrames;
    public uint Reserved;
}

#pragma warning restore CA1051

/// <summary>
/// High-performance zero-allocation binary codec for Moonshine Native Binary Protocol envelopes.
/// </summary>
public static class MoonshineProtocolCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHeader(in MoonshinePacketHeader header, Span<byte> destination)
    {
        if (destination.Length < MoonshineProtocolConstants.HeaderSize)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], header.Magic);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..6], header.Version);
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], (ushort)header.MessageType);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], header.PayloadSize);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], header.SequenceNumber);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..24], header.SessionId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..32], header.TimestampUs);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadHeader(ReadOnlySpan<byte> source, out MoonshinePacketHeader header)
    {
        header = default;

        if (source.Length < MoonshineProtocolConstants.HeaderSize)
        {
            return MoonshineErrorCode.BufferTooSmall;
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        ushort version = BinaryPrimitives.ReadUInt16BigEndian(source[4..6]);
        ushort messageType = BinaryPrimitives.ReadUInt16BigEndian(source[6..8]);
        uint payloadSize = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);
        uint sequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        ulong sessionId = BinaryPrimitives.ReadUInt64BigEndian(source[16..24]);
        ulong timestampUs = BinaryPrimitives.ReadUInt64BigEndian(source[24..32]);

        header = new MoonshinePacketHeader(
            Magic: magic,
            Version: version,
            MessageType: (MoonshineMessageType)messageType,
            PayloadSize: payloadSize,
            SequenceNumber: sequenceNumber,
            SessionId: sessionId,
            TimestampUs: timestampUs);

        if (magic != MoonshineProtocolConstants.Magic)
        {
            return MoonshineErrorCode.InvalidMagic;
        }

        if (version != MoonshineProtocolConstants.Version10)
        {
            return MoonshineErrorCode.UnsupportedVersion;
        }

        if (source.Length < MoonshineProtocolConstants.HeaderSize + payloadSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteVideoHeader(in MoonshineVideoPacketHeader videoHeader, Span<byte> destination)
    {
        if (destination.Length < 32) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], videoHeader.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], videoHeader.FrameIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], videoHeader.PacketIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], videoHeader.TotalPackets);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], videoHeader.FecBlockIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination[24..26], videoHeader.PayloadSize);
        destination[26] = videoHeader.PacketType;
        destination[27] = (byte)videoHeader.Flags;
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], videoHeader.Reserved);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadVideoHeader(ReadOnlySpan<byte> source, out MoonshineVideoPacketHeader videoHeader)
    {
        videoHeader = default;
        if (source.Length < 32) return false;

        videoHeader = new MoonshineVideoPacketHeader
        {
            StreamId = BinaryPrimitives.ReadUInt32BigEndian(source[..4]),
            FrameIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]),
            PacketIndex = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]),
            TotalPackets = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]),
            FecBlockIndex = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]),
            PayloadSize = BinaryPrimitives.ReadUInt16BigEndian(source[24..26]),
            PacketType = source[26],
            Flags = (MoonshineVideoAttributes)source[27],
            Reserved = BinaryPrimitives.ReadUInt32BigEndian(source[28..32])
        };

        return true;
    }
}
