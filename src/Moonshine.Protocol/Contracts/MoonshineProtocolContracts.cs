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
    TelemetryReport = 0x0701,

    // Host Management & Remote Configuration
    GetHostCapabilities = 0x0801,
    HostCapabilitiesResponse = 0x0802,
    GetHostConfiguration = 0x0803,
    HostConfigurationResponse = 0x0804,
    SetHostConfiguration = 0x0805,
    SetHostConfigurationResponse = 0x0806,
    ConfigurationChanged = 0x0807,

    // Discovery
    DiscoveryProbe = 0x0901,
    DiscoveryAnnouncement = 0x0902,
    DiscoveryResponse = 0x0903,

    // Acceptance Test Suite (TODO-049)
    AcceptanceStartRun = 0x0A01,
    AcceptanceStartRunResponse = 0x0A02,
    AcceptanceStepExecute = 0x0A03,
    AcceptanceStepProgress = 0x0A04,
    AcceptanceStepCompleted = 0x0A05,
    AcceptanceEvidenceUploadChunk = 0x0A06,
    AcceptanceEvidenceUploadAck = 0x0A07
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
    UnsupportedCodec = 11,
    UnauthorizedConfiguration = 12,
    InvalidConfigurationParameter = 13
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

/// <summary>
/// Canonical 16-byte raw UUID buffer (RFC 4122 Big-Endian wire representation).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
public unsafe struct MoonshineUuid128 : IEquatable<MoonshineUuid128>
{
    public fixed byte RawBytes[16];

    public MoonshineUuid128(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, 16, nameof(source));
        fixed (byte* p = RawBytes)
        {
            source[..16].CopyTo(new Span<byte>(p, 16));
        }
    }

    public MoonshineUuid128(Guid guid)
    {
        fixed (byte* p = RawBytes)
        {
            guid.TryWriteBytes(new Span<byte>(p, 16), bigEndian: true, out _);
        }
    }

    public readonly Guid ToGuid()
    {
        fixed (byte* p = RawBytes)
        {
            return new Guid(new ReadOnlySpan<byte>(p, 16), bigEndian: true);
        }
    }

    public readonly ReadOnlySpan<byte> AsSpan()
    {
        fixed (byte* p = RawBytes)
        {
            return new ReadOnlySpan<byte>(p, 16);
        }
    }

    public readonly bool Equals(MoonshineUuid128 other) => AsSpan().SequenceEqual(other.AsSpan());
    public override readonly bool Equals(object? obj) => obj is MoonshineUuid128 other && Equals(other);
    public override readonly int GetHashCode()
    {
        fixed (byte* p = RawBytes)
        {
            return HashCode.Combine(p[0], p[1], p[2], p[3]);
        }
    }
    public static bool operator ==(MoonshineUuid128 left, MoonshineUuid128 right) => left.Equals(right);
    public static bool operator !=(MoonshineUuid128 left, MoonshineUuid128 right) => !left.Equals(right);
}

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
    public MoonshineUuid128 ClientUuid;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineHelloResponsePayload
{
    public ushort ServerVersionMajor;
    public ushort ServerVersionMinor;
    public MoonshineCapabilities NegotiatedCapabilities;
    public ulong AssignedSessionId;
    public ulong ServerNonce;
    public MoonshineUuid128 ChallengeSalt;
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
    public uint TotalFrameBytes;
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

/// <summary>
/// Quality of Service (QoS) and network loss statistics payload (40 bytes packed).
/// <para>
/// Invariant: <see cref="LastReceivedFrameIndex"/> represents the client's highest observed/processed
/// monotonic stream frame index position. Media frames strictly advance monotonically per stream,
/// ensuring out-of-order or delayed UDP feedback datagrams are deterministically detected and filtered.
/// </para>
/// </summary>
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
    public uint ReceiveQueueDepth;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineHostCapabilitiesResponsePayload
{
    public uint SupportedVideoCodecs;
    public uint SupportedAudioCodecs;
    public uint MaxEncodeWidth;
    public uint MaxEncodeHeight;
    public uint MaxEncodeFps;
    public byte SupportsHdr10;
    public byte SupportsVirtualAudio;
    public byte SupportsMicBackchannel;
    public byte Reserved;
    public uint MaxBitrateKbps;
    public uint Reserved2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineHostConfigurationPayload
{
    public uint ConfigVersion;
    public uint DisplayWidth;
    public uint DisplayHeight;
    public uint RefreshRateHz;
    public uint TargetBitrateKbps;
    public uint MaxBitrateKbps;
    public MoonshineVideoCodec PreferredCodec;
    public byte Hdr10Enabled;
    public byte AudioChannels;
    public byte AudioQualityMode;
    public uint AudioBitrateKbps;
    public ushort InputPollingRateHz;
    public byte MicPassthroughEnabled;
    public byte VirtualAudioDriverEnabled;
    public uint Reserved1;
    public uint Reserved2;
    public uint Reserved3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineSetHostConfigurationResponsePayload
{
    public MoonshineErrorCode StatusCode;
    public uint AppliedConfigVersion;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineConfigurationChangedPayload
{
    public uint NewConfigVersion;
    public uint ChangeReasonFlags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineDiscoveryProbePayload
{
    public ushort ClientVersionMajor;
    public ushort ClientVersionMinor;
    public MoonshineUuid128 ClientUuid;
    public MoonshineCapabilities DesiredCapabilities;
    public uint Reserved;
    public ulong ProbeNonce;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineDiscoveryAnnouncementPayload
{
    public ushort HostVersionMajor;
    public ushort HostVersionMinor;
    public MoonshineUuid128 HostUuid;
    public MoonshineCapabilities SupportedCapabilities;
    public uint ControlTcpPort;
    public uint DiscoveryUdpPort;
    public uint VideoUdpPort;
    public uint AudioUdpPort;
    public uint ControlFeedbackUdpPort;
    public uint MicUdpPort;
    public uint MaxBitrateKbps;
    public byte SupportsHdr10;
    public byte SupportsVirtualAudio;
    public byte SupportsMicBackchannel;
    public byte IsPaired;
    public fixed byte Hostname[64];
    public fixed byte GpuName[64];
    public ulong AdvertisementNonce;
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

        if (messageType == 0 || !Enum.IsDefined<MoonshineMessageType>((MoonshineMessageType)messageType))
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if (payloadSize > 1_048_576) // 1 MB envelope ceiling
        {
            return MoonshineErrorCode.MalformedHeader;
        }

        if ((ulong)source.Length < (ulong)MoonshineProtocolConstants.HeaderSize + payloadSize)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        // Validate message-specific payload minimum bounds
        var msgType = (MoonshineMessageType)messageType;
        uint minPayload = GetMinimumPayloadSize(msgType);
        if (payloadSize < minPayload)
        {
            return MoonshineErrorCode.PayloadTruncated;
        }

        return MoonshineErrorCode.Success;
    }

    /// <summary>
    /// Evaluates whether candidate 16-bit sequence number is newer than previous using RFC 1982 modular serial arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewerSequence16(ushort candidate, ushort previous)
    {
        return candidate != previous && unchecked((short)(candidate - previous)) > 0;
    }

    /// <summary>
    /// Evaluates whether candidate 32-bit sequence number is newer than previous using RFC 1982 modular serial arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewerSequence(uint candidate, uint previous)
    {
        return candidate != previous && unchecked((int)(candidate - previous)) > 0;
    }

    /// <summary>
    /// Evaluates whether candidate 32-bit sequence number is newer than previous using RFC 1982 modular serial arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewerSequence32(uint candidate, uint previous) => IsNewerSequence(candidate, previous);

    /// <summary>
    /// Evaluates whether candidate frame index is newer than previous using modular serial arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewerFrameIndex(ulong candidate, ulong previous)
    {
        return candidate != previous && unchecked((long)(candidate - previous)) > 0;
    }

    /// <summary>
    /// Evaluates whether candidate 64-bit sequence number / frame index is newer than previous using modular serial arithmetic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewerSequence64(ulong candidate, ulong previous) => IsNewerFrameIndex(candidate, previous);

    /// <summary>
    /// Returns the signed 16-bit modular sequence distance (candidate - previous) under RFC 1982 rules.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SequenceDistance16(ushort candidate, ushort previous) => unchecked((short)(candidate - previous));

    /// <summary>
    /// Returns the signed 32-bit modular sequence distance (candidate - previous) under RFC 1982 rules.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SequenceDistance32(uint candidate, uint previous) => unchecked((int)(candidate - previous));

    /// <summary>
    /// Returns the signed 64-bit modular sequence distance (candidate - previous) under RFC 1982 rules.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SequenceDistance64(ulong candidate, ulong previous) => unchecked((long)(candidate - previous));

    /// <summary>
    /// Returns whether the given message type strictly requires a non-zero negotiated Session ID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RequiresSessionId(MoonshineMessageType messageType) =>
        messageType switch
        {
            MoonshineMessageType.Hello => false,
            MoonshineMessageType.HelloResponse => false,
            MoonshineMessageType.DiscoveryProbe => false,
            MoonshineMessageType.DiscoveryAnnouncement => false,
            MoonshineMessageType.DiscoveryResponse => false,
            MoonshineMessageType.GetHostCapabilities => false,
            MoonshineMessageType.GetHostConfiguration => false,
            MoonshineMessageType.SetHostConfiguration => false,
            MoonshineMessageType.ConfigurationChanged => false,
            _ => true
        };

    /// <summary>
    /// Returns whether the given message type requires HMAC authentication envelopes when security is enabled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RequiresAuthentication(MoonshineMessageType messageType)
    {
        return messageType switch
        {
            MoonshineMessageType.GetHostCapabilities => true,
            MoonshineMessageType.HostCapabilitiesResponse => true,
            MoonshineMessageType.GetHostConfiguration => true,
            MoonshineMessageType.HostConfigurationResponse => true,
            MoonshineMessageType.SetHostConfiguration => true,
            MoonshineMessageType.SetHostConfigurationResponse => true,
            MoonshineMessageType.ConfigurationChanged => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns the minimum payload size in bytes for a given message type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetMinimumPayloadSize(MoonshineMessageType messageType)
    {
        return messageType switch
        {
            MoonshineMessageType.Hello => 32,
            MoonshineMessageType.HelloResponse => 48,
            MoonshineMessageType.SessionSetup => 40,
            MoonshineMessageType.SessionSetupResponse => 32,
            MoonshineMessageType.FeedbackLossStats => 40,
            MoonshineMessageType.IdrRequest => 16,
            MoonshineMessageType.InputKeyboard => 12,
            MoonshineMessageType.InputMouse => 20,
            MoonshineMessageType.InputGamepad => 24,
            MoonshineMessageType.TelemetryReport => 32,
            MoonshineMessageType.GetHostCapabilities => 4,
            MoonshineMessageType.HostCapabilitiesResponse => 32,
            MoonshineMessageType.GetHostConfiguration => 4,
            MoonshineMessageType.HostConfigurationResponse => 48,
            MoonshineMessageType.SetHostConfiguration => 48,
            MoonshineMessageType.SetHostConfigurationResponse => 8,
            MoonshineMessageType.ConfigurationChanged => 8,
            MoonshineMessageType.VideoPacket => 32,
            MoonshineMessageType.AudioPacket => 24,
            MoonshineMessageType.MicPacket => 20,
            _ => 0
        };
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
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], videoHeader.TotalFrameBytes);

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
            TotalFrameBytes = BinaryPrimitives.ReadUInt32BigEndian(source[28..32])
        };

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHello(in MoonshineHelloPayload hello, Span<byte> destination)
    {
        if (destination.Length < 32) return false;

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], hello.ClientVersionMajor);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], hello.ClientVersionMinor);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], (uint)hello.CapabilitiesMask);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], hello.ClientNonce);
        hello.ClientUuid.AsSpan().CopyTo(destination[16..32]);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadHello(ReadOnlySpan<byte> source, out MoonshineHelloPayload hello)
    {
        hello = default;
        if (source.Length < 32) return false;

        ushort clientMajor = BinaryPrimitives.ReadUInt16BigEndian(source[..2]);
        ushort clientMinor = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]);
        uint caps = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        ulong nonce = BinaryPrimitives.ReadUInt64BigEndian(source[8..16]);

        if (clientMajor == 0 && clientMinor == 0)
        {
            return false;
        }

        hello = new MoonshineHelloPayload
        {
            ClientVersionMajor = clientMajor,
            ClientVersionMinor = clientMinor,
            CapabilitiesMask = (MoonshineCapabilities)caps,
            ClientNonce = nonce,
            ClientUuid = new MoonshineUuid128(source[16..32])
        };

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteKeyboardInput(in MoonshineInputKeyboardPayload payload, Span<byte> destination)
    {
        if (destination.Length < 12) return false;

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], payload.KeyCode);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], payload.ScanCode);
        destination[4] = payload.IsDown;
        destination[5] = payload.Modifiers;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], payload.Reserved);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.TimestampOffsetUs);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadKeyboardInput(ReadOnlySpan<byte> source, out MoonshineInputKeyboardPayload payload)
    {
        payload = default;
        if (source.Length < 12) return MoonshineErrorCode.BufferTooSmall;

        byte isDown = source[4];
        if (isDown > 1) return MoonshineErrorCode.InvalidConfigurationParameter;

        payload = new MoonshineInputKeyboardPayload
        {
            KeyCode = BinaryPrimitives.ReadUInt16BigEndian(source[..2]),
            ScanCode = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]),
            IsDown = isDown,
            Modifiers = source[5],
            Reserved = BinaryPrimitives.ReadUInt16BigEndian(source[6..8]),
            TimestampOffsetUs = BinaryPrimitives.ReadUInt32BigEndian(source[8..12])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteMouseInput(in MoonshineInputMousePayload payload, Span<byte> destination)
    {
        if (destination.Length < 20) return false;

        BinaryPrimitives.WriteInt32BigEndian(destination[..4], payload.X);
        BinaryPrimitives.WriteInt32BigEndian(destination[4..8], payload.Y);
        BinaryPrimitives.WriteInt16BigEndian(destination[8..10], payload.WheelDeltaY);
        BinaryPrimitives.WriteInt16BigEndian(destination[10..12], payload.WheelDeltaX);
        BinaryPrimitives.WriteUInt16BigEndian(destination[12..14], payload.ButtonFlags);
        destination[14] = payload.IsAbsolute;
        destination[15] = payload.Reserved;
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], payload.TimestampOffsetUs);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadMouseInput(ReadOnlySpan<byte> source, out MoonshineInputMousePayload payload)
    {
        payload = default;
        if (source.Length < 20) return MoonshineErrorCode.BufferTooSmall;

        byte isAbsolute = source[14];
        if (isAbsolute > 1) return MoonshineErrorCode.InvalidConfigurationParameter;

        payload = new MoonshineInputMousePayload
        {
            X = BinaryPrimitives.ReadInt32BigEndian(source[..4]),
            Y = BinaryPrimitives.ReadInt32BigEndian(source[4..8]),
            WheelDeltaY = BinaryPrimitives.ReadInt16BigEndian(source[8..10]),
            WheelDeltaX = BinaryPrimitives.ReadInt16BigEndian(source[10..12]),
            ButtonFlags = BinaryPrimitives.ReadUInt16BigEndian(source[12..14]),
            IsAbsolute = isAbsolute,
            Reserved = source[15],
            TimestampOffsetUs = BinaryPrimitives.ReadUInt32BigEndian(source[16..20])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteGamepadInput(in MoonshineInputGamepadPayload payload, Span<byte> destination)
    {
        if (destination.Length < 24) return false;

        destination[0] = payload.GamepadIndex;
        destination[1] = payload.Reserved;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], payload.ButtonMask);
        destination[4] = payload.LeftTrigger;
        destination[5] = payload.RightTrigger;
        BinaryPrimitives.WriteInt16BigEndian(destination[6..8], payload.ThumbLx);
        BinaryPrimitives.WriteInt16BigEndian(destination[8..10], payload.ThumbLy);
        BinaryPrimitives.WriteInt16BigEndian(destination[10..12], payload.ThumbRx);
        BinaryPrimitives.WriteInt16BigEndian(destination[12..14], payload.ThumbRy);
        BinaryPrimitives.WriteUInt16BigEndian(destination[14..16], payload.MotorLeft);
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..18], payload.MotorRight);
        BinaryPrimitives.WriteUInt32BigEndian(destination[18..22], payload.TimestampOffsetUs);
        BinaryPrimitives.WriteUInt16BigEndian(destination[22..24], payload.Reserved2);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadGamepadInput(ReadOnlySpan<byte> source, out MoonshineInputGamepadPayload payload)
    {
        payload = default;
        if (source.Length < 24) return MoonshineErrorCode.BufferTooSmall;

        byte gamepadIndex = source[0];
        if (gamepadIndex > 3) return MoonshineErrorCode.InvalidConfigurationParameter;

        payload = new MoonshineInputGamepadPayload
        {
            GamepadIndex = gamepadIndex,
            Reserved = source[1],
            ButtonMask = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]),
            LeftTrigger = source[4],
            RightTrigger = source[5],
            ThumbLx = BinaryPrimitives.ReadInt16BigEndian(source[6..8]),
            ThumbLy = BinaryPrimitives.ReadInt16BigEndian(source[8..10]),
            ThumbRx = BinaryPrimitives.ReadInt16BigEndian(source[10..12]),
            ThumbRy = BinaryPrimitives.ReadInt16BigEndian(source[12..14]),
            MotorLeft = BinaryPrimitives.ReadUInt16BigEndian(source[14..16]),
            MotorRight = BinaryPrimitives.ReadUInt16BigEndian(source[16..18]),
            TimestampOffsetUs = BinaryPrimitives.ReadUInt32BigEndian(source[18..22]),
            Reserved2 = BinaryPrimitives.ReadUInt16BigEndian(source[22..24])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteFeedbackLossStats(in MoonshineFeedbackLossStatsPayload payload, Span<byte> destination)
    {
        if (destination.Length < 40) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], payload.LastReceivedFrameIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.PacketsReceived);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], payload.PacketsLost);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], payload.PacketsRecoveredFec);
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..28], payload.RoundTripTimeUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], payload.JitterUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[32..36], payload.EstimatedBandwidthKbps);
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..40], payload.ReceiveQueueDepth);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadFeedbackLossStats(ReadOnlySpan<byte> source, out MoonshineFeedbackLossStatsPayload payload)
    {
        payload = default;
        if (source.Length < 40) return MoonshineErrorCode.BufferTooSmall;

        uint streamId = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        if (streamId == 0) return MoonshineErrorCode.StreamNotFound;

        payload = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = streamId,
            LastReceivedFrameIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]),
            PacketsReceived = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]),
            PacketsLost = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]),
            PacketsRecoveredFec = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]),
            RoundTripTimeUs = BinaryPrimitives.ReadUInt32BigEndian(source[24..28]),
            JitterUs = BinaryPrimitives.ReadUInt32BigEndian(source[28..32]),
            EstimatedBandwidthKbps = BinaryPrimitives.ReadUInt32BigEndian(source[32..36]),
            ReceiveQueueDepth = BinaryPrimitives.ReadUInt32BigEndian(source[36..40])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHelloResponse(in MoonshineHelloResponsePayload payload, Span<byte> destination)
    {
        if (destination.Length < 48) return false;

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], payload.ServerVersionMajor);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], payload.ServerVersionMinor);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], (uint)payload.NegotiatedCapabilities);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], payload.AssignedSessionId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..24], payload.ServerNonce);
        payload.ChallengeSalt.AsSpan().CopyTo(destination[24..40]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[40..44], payload.SessionLeaseSeconds);
        BinaryPrimitives.WriteUInt32BigEndian(destination[44..48], payload.Reserved);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadHelloResponse(ReadOnlySpan<byte> source, out MoonshineHelloResponsePayload payload)
    {
        payload = default;
        if (source.Length < 48) return MoonshineErrorCode.BufferTooSmall;

        ushort serverMajor = BinaryPrimitives.ReadUInt16BigEndian(source[..2]);
        ushort serverMinor = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]);
        uint caps = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        ulong sessionId = BinaryPrimitives.ReadUInt64BigEndian(source[8..16]);
        ulong serverNonce = BinaryPrimitives.ReadUInt64BigEndian(source[16..24]);
        var salt = new MoonshineUuid128(source[24..40]);
        uint leaseSec = BinaryPrimitives.ReadUInt32BigEndian(source[40..44]);
        uint reserved = BinaryPrimitives.ReadUInt32BigEndian(source[44..48]);

        if (serverMajor == 0 && serverMinor == 0)
        {
            return MoonshineErrorCode.UnsupportedVersion;
        }

        if (sessionId == 0)
        {
            return MoonshineErrorCode.InvalidSession;
        }

        payload = new MoonshineHelloResponsePayload
        {
            ServerVersionMajor = serverMajor,
            ServerVersionMinor = serverMinor,
            NegotiatedCapabilities = (MoonshineCapabilities)caps,
            AssignedSessionId = sessionId,
            ServerNonce = serverNonce,
            ChallengeSalt = salt,
            SessionLeaseSeconds = leaseSec,
            Reserved = reserved
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteSessionSetup(in MoonshineSessionSetupPayload payload, Span<byte> destination)
    {
        if (destination.Length < 40) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.VideoWidth);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.VideoHeight);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.VideoFps);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.VideoBitrateKbps);
        destination[16] = (byte)payload.VideoCodec;
        destination[17] = (byte)payload.VideoColorFormat;
        destination[18] = payload.AudioChannels;
        destination[19] = (byte)payload.AudioCodec;
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], payload.AudioSampleRate);
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..28], payload.AudioBitrateKbps);
        BinaryPrimitives.WriteUInt16BigEndian(destination[28..30], payload.ClientUdpVideoPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[30..32], payload.ClientUdpAudioPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[32..34], payload.ClientUdpFeedbackPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[34..36], payload.Reserved);
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..40], payload.MtuPayloadSize);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadSessionSetup(ReadOnlySpan<byte> source, out MoonshineSessionSetupPayload payload)
    {
        payload = default;
        if (source.Length < 40) return MoonshineErrorCode.BufferTooSmall;

        uint videoWidth = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        uint videoHeight = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        uint videoFps = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);
        uint videoBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        var videoCodec = (MoonshineVideoCodec)source[16];
        var videoColorFormat = (MoonshineColorFormat)source[17];
        byte audioChannels = source[18];
        var audioCodec = (MoonshineAudioCodec)source[19];
        uint audioSampleRate = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]);
        uint audioBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[24..28]);
        ushort clientUdpVideoPort = BinaryPrimitives.ReadUInt16BigEndian(source[28..30]);
        ushort clientUdpAudioPort = BinaryPrimitives.ReadUInt16BigEndian(source[30..32]);
        ushort clientUdpFeedbackPort = BinaryPrimitives.ReadUInt16BigEndian(source[32..34]);
        ushort reserved = BinaryPrimitives.ReadUInt16BigEndian(source[34..36]);
        uint mtuPayloadSize = BinaryPrimitives.ReadUInt32BigEndian(source[36..40]);

        if (videoWidth == 0 || videoWidth > 16384 || videoHeight == 0 || videoHeight > 16384 || videoFps == 0 || videoFps > 1000 ||
            videoCodec == MoonshineVideoCodec.Unknown || audioCodec == MoonshineAudioCodec.Unknown ||
            (audioChannels != 1 && audioChannels != 2 && audioChannels != 6 && audioChannels != 8) ||
            audioSampleRate < 8000 || audioSampleRate > 384000 ||
            mtuPayloadSize < 576 || mtuPayloadSize > 65507)
        {
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        payload = new MoonshineSessionSetupPayload
        {
            VideoWidth = videoWidth,
            VideoHeight = videoHeight,
            VideoFps = videoFps,
            VideoBitrateKbps = videoBitrateKbps,
            VideoCodec = videoCodec,
            VideoColorFormat = videoColorFormat,
            AudioChannels = audioChannels,
            AudioCodec = audioCodec,
            AudioSampleRate = audioSampleRate,
            AudioBitrateKbps = audioBitrateKbps,
            ClientUdpVideoPort = clientUdpVideoPort,
            ClientUdpAudioPort = clientUdpAudioPort,
            ClientUdpFeedbackPort = clientUdpFeedbackPort,
            Reserved = reserved,
            MtuPayloadSize = mtuPayloadSize
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteSessionSetupResponse(in MoonshineSessionSetupResponsePayload payload, Span<byte> destination)
    {
        if (destination.Length < 32) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)payload.StatusCode);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.VideoStreamId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.AudioStreamId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.FeedbackStreamId);
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..18], payload.HostUdpVideoPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[18..20], payload.HostUdpAudioPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[20..22], payload.HostUdpFeedbackPort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[22..24], payload.HostUdpInputPort);
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..28], payload.NegotiatedMtu);
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], payload.Reserved);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadSessionSetupResponse(ReadOnlySpan<byte> source, out MoonshineSessionSetupResponsePayload payload)
    {
        payload = default;
        if (source.Length < 32) return MoonshineErrorCode.BufferTooSmall;

        var statusCode = (MoonshineErrorCode)BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        uint videoStreamId = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        uint audioStreamId = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);
        uint feedbackStreamId = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        ushort hostUdpVideoPort = BinaryPrimitives.ReadUInt16BigEndian(source[16..18]);
        ushort hostUdpAudioPort = BinaryPrimitives.ReadUInt16BigEndian(source[18..20]);
        ushort hostUdpFeedbackPort = BinaryPrimitives.ReadUInt16BigEndian(source[20..22]);
        ushort hostUdpInputPort = BinaryPrimitives.ReadUInt16BigEndian(source[22..24]);
        uint negotiatedMtu = BinaryPrimitives.ReadUInt32BigEndian(source[24..28]);
        uint reserved = BinaryPrimitives.ReadUInt32BigEndian(source[28..32]);

        if (statusCode == MoonshineErrorCode.Success)
        {
            if (videoStreamId == 0 || audioStreamId == 0 || negotiatedMtu < 576 || negotiatedMtu > 65507)
            {
                return MoonshineErrorCode.InvalidConfigurationParameter;
            }
        }

        payload = new MoonshineSessionSetupResponsePayload
        {
            StatusCode = statusCode,
            VideoStreamId = videoStreamId,
            AudioStreamId = audioStreamId,
            FeedbackStreamId = feedbackStreamId,
            HostUdpVideoPort = hostUdpVideoPort,
            HostUdpAudioPort = hostUdpAudioPort,
            HostUdpFeedbackPort = hostUdpFeedbackPort,
            HostUdpInputPort = hostUdpInputPort,
            NegotiatedMtu = negotiatedMtu,
            Reserved = reserved
        };

        return MoonshineErrorCode.Success;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteTelemetryReport(in MoonshineTelemetryReportPayload payload, Span<byte> destination)
    {
        if (destination.Length < 32) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.EncodeLatencyUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.DecodeLatencyUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.RenderLatencyUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.NetworkLatencyUs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], payload.FramesRendered);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], payload.FramesDropped);
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..28], payload.FecRecoveredFrames);
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], payload.Reserved);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadTelemetryReport(ReadOnlySpan<byte> source, out MoonshineTelemetryReportPayload payload)
    {
        payload = default;
        if (source.Length < 32) return MoonshineErrorCode.BufferTooSmall;

        payload = new MoonshineTelemetryReportPayload
        {
            EncodeLatencyUs = BinaryPrimitives.ReadUInt32BigEndian(source[..4]),
            DecodeLatencyUs = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]),
            RenderLatencyUs = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]),
            NetworkLatencyUs = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]),
            FramesRendered = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]),
            FramesDropped = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]),
            FecRecoveredFrames = BinaryPrimitives.ReadUInt32BigEndian(source[24..28]),
            Reserved = BinaryPrimitives.ReadUInt32BigEndian(source[28..32])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteIdrRequest(in MoonshineIdrRequestPayload payload, Span<byte> destination)
    {
        if (destination.Length < 16) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.StreamId);
        BinaryPrimitives.WriteUInt64BigEndian(destination[4..12], payload.LastValidFrameIndex);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.ReasonCode);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadIdrRequest(ReadOnlySpan<byte> source, out MoonshineIdrRequestPayload payload)
    {
        payload = default;
        if (source.Length < 16) return MoonshineErrorCode.BufferTooSmall;

        uint streamId = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        uint reasonCode = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);

        if (streamId == 0) return MoonshineErrorCode.StreamNotFound;
        if (reasonCode == 0) return MoonshineErrorCode.InvalidConfigurationParameter;

        payload = new MoonshineIdrRequestPayload
        {
            StreamId = streamId,
            LastValidFrameIndex = BinaryPrimitives.ReadUInt64BigEndian(source[4..12]),
            ReasonCode = reasonCode
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteGetHostCapabilities(uint queryMask, Span<byte> destination)
    {
        if (destination.Length < 4) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], queryMask);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadGetHostCapabilities(ReadOnlySpan<byte> source, out uint queryMask)
    {
        queryMask = default;
        if (source.Length < 4) return MoonshineErrorCode.BufferTooSmall;

        queryMask = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHostCapabilitiesResponse(in MoonshineHostCapabilitiesResponsePayload payload, Span<byte> destination)
    {
        if (destination.Length < 32) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.SupportedVideoCodecs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.SupportedAudioCodecs);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.MaxEncodeWidth);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.MaxEncodeHeight);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], payload.MaxEncodeFps);
        destination[20] = payload.SupportsHdr10;
        destination[21] = payload.SupportsVirtualAudio;
        destination[22] = payload.SupportsMicBackchannel;
        destination[23] = payload.Reserved;
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..28], payload.MaxBitrateKbps);
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], payload.Reserved2);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadHostCapabilitiesResponse(ReadOnlySpan<byte> source, out MoonshineHostCapabilitiesResponsePayload payload)
    {
        payload = default;
        if (source.Length < 32) return MoonshineErrorCode.BufferTooSmall;

        payload = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = BinaryPrimitives.ReadUInt32BigEndian(source[..4]),
            SupportedAudioCodecs = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]),
            MaxEncodeWidth = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]),
            MaxEncodeHeight = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]),
            MaxEncodeFps = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]),
            SupportsHdr10 = source[20],
            SupportsVirtualAudio = source[21],
            SupportsMicBackchannel = source[22],
            Reserved = source[23],
            MaxBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[24..28]),
            Reserved2 = BinaryPrimitives.ReadUInt32BigEndian(source[28..32])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteGetHostConfiguration(uint queryScope, Span<byte> destination)
    {
        if (destination.Length < 4) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], queryScope);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadGetHostConfiguration(ReadOnlySpan<byte> source, out uint queryScope)
    {
        queryScope = default;
        if (source.Length < 4) return MoonshineErrorCode.BufferTooSmall;

        queryScope = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteHostConfiguration(in MoonshineHostConfigurationPayload payload, Span<byte> destination)
    {
        if (destination.Length < 48) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.ConfigVersion);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.DisplayWidth);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], payload.DisplayHeight);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], payload.RefreshRateHz);
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..20], payload.TargetBitrateKbps);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..24], payload.MaxBitrateKbps);
        destination[24] = (byte)payload.PreferredCodec;
        destination[25] = payload.Hdr10Enabled;
        destination[26] = payload.AudioChannels;
        destination[27] = payload.AudioQualityMode;
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..32], payload.AudioBitrateKbps);
        BinaryPrimitives.WriteUInt16BigEndian(destination[32..34], payload.InputPollingRateHz);
        destination[34] = payload.MicPassthroughEnabled;
        destination[35] = payload.VirtualAudioDriverEnabled;
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..40], payload.Reserved1);
        BinaryPrimitives.WriteUInt32BigEndian(destination[40..44], payload.Reserved2);
        BinaryPrimitives.WriteUInt32BigEndian(destination[44..48], payload.Reserved3);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadHostConfiguration(ReadOnlySpan<byte> source, out MoonshineHostConfigurationPayload payload)
    {
        payload = default;
        if (source.Length < 48) return MoonshineErrorCode.BufferTooSmall;

        uint configVersion = BinaryPrimitives.ReadUInt32BigEndian(source[..4]);
        uint displayWidth = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        uint displayHeight = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);
        uint refreshRateHz = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        uint targetBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[16..20]);
        uint maxBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[20..24]);
        var preferredCodec = (MoonshineVideoCodec)source[24];
        byte hdr10Enabled = source[25];
        byte audioChannels = source[26];
        byte audioQualityMode = source[27];
        uint audioBitrateKbps = BinaryPrimitives.ReadUInt32BigEndian(source[28..32]);
        ushort inputPollingRateHz = BinaryPrimitives.ReadUInt16BigEndian(source[32..34]);
        byte micPassthroughEnabled = source[34];
        byte virtualAudioDriverEnabled = source[35];
        uint res1 = BinaryPrimitives.ReadUInt32BigEndian(source[36..40]);
        uint res2 = BinaryPrimitives.ReadUInt32BigEndian(source[40..44]);
        uint res3 = BinaryPrimitives.ReadUInt32BigEndian(source[44..48]);

        if (displayWidth == 0 || displayWidth > 16384 || displayHeight == 0 || displayHeight > 16384 || refreshRateHz == 0 || refreshRateHz > 1000 ||
            (audioChannels != 1 && audioChannels != 2 && audioChannels != 6 && audioChannels != 8))
        {
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (preferredCodec == MoonshineVideoCodec.Unknown)
        {
            return MoonshineErrorCode.UnsupportedCodec;
        }

        payload = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = configVersion,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight,
            RefreshRateHz = refreshRateHz,
            TargetBitrateKbps = targetBitrateKbps,
            MaxBitrateKbps = maxBitrateKbps,
            PreferredCodec = preferredCodec,
            Hdr10Enabled = hdr10Enabled,
            AudioChannels = audioChannels,
            AudioQualityMode = audioQualityMode,
            AudioBitrateKbps = audioBitrateKbps,
            InputPollingRateHz = inputPollingRateHz,
            MicPassthroughEnabled = micPassthroughEnabled,
            VirtualAudioDriverEnabled = virtualAudioDriverEnabled,
            Reserved1 = res1,
            Reserved2 = res2,
            Reserved3 = res3
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteSetHostConfigurationResponse(in MoonshineSetHostConfigurationResponsePayload payload, Span<byte> destination)
    {
        if (destination.Length < 8) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)payload.StatusCode);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.AppliedConfigVersion);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadSetHostConfigurationResponse(ReadOnlySpan<byte> source, out MoonshineSetHostConfigurationResponsePayload payload)
    {
        payload = default;
        if (source.Length < 8) return MoonshineErrorCode.BufferTooSmall;

        payload = new MoonshineSetHostConfigurationResponsePayload
        {
            StatusCode = (MoonshineErrorCode)BinaryPrimitives.ReadUInt32BigEndian(source[..4]),
            AppliedConfigVersion = BinaryPrimitives.ReadUInt32BigEndian(source[4..8])
        };

        return MoonshineErrorCode.Success;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryWriteConfigurationChanged(in MoonshineConfigurationChangedPayload payload, Span<byte> destination)
    {
        if (destination.Length < 8) return false;

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], payload.NewConfigVersion);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], payload.ChangeReasonFlags);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoonshineErrorCode TryReadConfigurationChanged(ReadOnlySpan<byte> source, out MoonshineConfigurationChangedPayload payload)
    {
        payload = default;
        if (source.Length < 8) return MoonshineErrorCode.BufferTooSmall;

        payload = new MoonshineConfigurationChangedPayload
        {
            NewConfigVersion = BinaryPrimitives.ReadUInt32BigEndian(source[..4]),
            ChangeReasonFlags = BinaryPrimitives.ReadUInt32BigEndian(source[4..8])
        };

        return MoonshineErrorCode.Success;
    }
}
