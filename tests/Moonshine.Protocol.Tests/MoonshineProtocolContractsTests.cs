using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MoonshineProtocolContractsTests
{
    [Fact]
    public void StructSizesAndLayouts_MatchExactExpectedBytes()
    {
        Marshal.SizeOf<MoonshineUuid128>().Should().Be(16);
        Marshal.SizeOf<MoonshinePacketHeader>().Should().Be(32);
        Marshal.SizeOf<MoonshineHelloPayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineHelloResponsePayload>().Should().Be(48);
        Marshal.SizeOf<MoonshineSessionSetupPayload>().Should().Be(40);
        Marshal.SizeOf<MoonshineSessionSetupResponsePayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineVideoPacketHeader>().Should().Be(32);
        Marshal.SizeOf<MoonshineAudioPacketHeader>().Should().Be(24);
        Marshal.SizeOf<MoonshineMicPacketHeader>().Should().Be(20);
        Marshal.SizeOf<MoonshineFeedbackLossStatsPayload>().Should().Be(40);
        Marshal.SizeOf<MoonshineIdrRequestPayload>().Should().Be(16);
        Marshal.SizeOf<MoonshineInputKeyboardPayload>().Should().Be(12);
        Marshal.SizeOf<MoonshineInputMousePayload>().Should().Be(20);
        Marshal.SizeOf<MoonshineInputGamepadPayload>().Should().Be(24);
        Marshal.SizeOf<MoonshineTelemetryReportPayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineHostCapabilitiesResponsePayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineHostConfigurationPayload>().Should().Be(48);
        Marshal.SizeOf<MoonshineSetHostConfigurationResponsePayload>().Should().Be(8);
        Marshal.SizeOf<MoonshineConfigurationChangedPayload>().Should().Be(8);
        Marshal.SizeOf<MoonshineDiscoveryProbePayload>().Should().Be(36);
        Marshal.SizeOf<MoonshineDiscoveryAnnouncementPayload>().Should().Be(192);
    }

    [Fact]
    public void MoonshineUuid128_BigEndianRoundtrip_MatchesExactRawBytes()
    {
        byte[] rawBytes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var uuid = new MoonshineUuid128(rawBytes);

        uuid.AsSpan().ToArray().Should().Equal(rawBytes);

        Guid guid = uuid.ToGuid();
        var reconstructed = new MoonshineUuid128(guid);
        reconstructed.Should().Be(uuid);
        reconstructed.AsSpan().ToArray().Should().Equal(rawBytes);
    }

    [Fact]
    public void HeaderCodec_BigEndianSerialization_MatchesExactBytePatternAndCPlusPlusFixture()
    {
        var original = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 64,
            SequenceNumber: 1024,
            SessionId: 0x0123456789ABCDEFUL,
            TimestampUs: 1700000000123456UL);

        byte[] buffer = new byte[32 + 64];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteHeader(original, buffer);
        writeSuccess.Should().BeTrue();

        // Validate byte-for-byte exact big-endian wire pattern
        buffer[0].Should().Be(0x4D); // 'M'
        buffer[1].Should().Be(0x53); // 'S'
        buffer[2].Should().Be(0x48); // 'H'
        buffer[3].Should().Be(0x4E); // 'N'

        buffer[4].Should().Be(0x00);
        buffer[5].Should().Be(0x01); // Version 1.0

        buffer[6].Should().Be(0x02);
        buffer[7].Should().Be(0x01); // VideoPacket (0x0201)

        buffer[8].Should().Be(0x00);
        buffer[9].Should().Be(0x00);
        buffer[10].Should().Be(0x00);
        buffer[11].Should().Be(0x40); // PayloadSize = 64

        buffer[12].Should().Be(0x00);
        buffer[13].Should().Be(0x00);
        buffer[14].Should().Be(0x04);
        buffer[15].Should().Be(0x00); // SequenceNumber = 1024

        // Session ID 0x0123456789ABCDEF
        buffer[16].Should().Be(0x01);
        buffer[17].Should().Be(0x23);
        buffer[18].Should().Be(0x45);
        buffer[19].Should().Be(0x67);
        buffer[20].Should().Be(0x89);
        buffer[21].Should().Be(0xAB);
        buffer[22].Should().Be(0xCD);
        buffer[23].Should().Be(0xEF);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHeader(buffer, out MoonshinePacketHeader decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.Magic.Should().Be(original.Magic);
        decoded.Version.Should().Be(original.Version);
        decoded.MessageType.Should().Be(original.MessageType);
        decoded.PayloadSize.Should().Be(original.PayloadSize);
        decoded.SequenceNumber.Should().Be(original.SequenceNumber);
        decoded.SessionId.Should().Be(original.SessionId);
        decoded.TimestampUs.Should().Be(original.TimestampUs);
    }

    [Fact]
    public void HeaderCodec_BufferTooSmall_ReturnsError()
    {
        byte[] buffer = new byte[16];
        MoonshineErrorCode result = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        result.Should().Be(MoonshineErrorCode.BufferTooSmall);

        var header = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, MoonshineProtocolConstants.Version10, MoonshineMessageType.Hello, 0, 1, 42, 100);
        bool writeResult = MoonshineProtocolCodec.TryWriteHeader(header, buffer);
        writeResult.Should().BeFalse();
    }

    [Fact]
    public void HeaderCodec_InvalidMagic_ReturnsError()
    {
        byte[] buffer = new byte[64];
        var header = new MoonshinePacketHeader(0xDEADBEEF, MoonshineProtocolConstants.Version10, MoonshineMessageType.Hello, 0, 1, 42, 100);
        MoonshineProtocolCodec.TryWriteHeader(header, buffer);

        MoonshineErrorCode result = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        result.Should().Be(MoonshineErrorCode.InvalidMagic);
    }

    [Fact]
    public void HeaderCodec_UnsupportedVersion_ReturnsError()
    {
        byte[] buffer = new byte[64];
        var header = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, 0x0099, MoonshineMessageType.Hello, 0, 1, 42, 100);
        MoonshineProtocolCodec.TryWriteHeader(header, buffer);

        MoonshineErrorCode result = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        result.Should().Be(MoonshineErrorCode.UnsupportedVersion);
    }

    [Fact]
    public void HeaderCodec_PayloadTruncated_ReturnsError()
    {
        byte[] buffer = new byte[32]; // 32 bytes header, but declares 100 bytes payload
        var header = new MoonshinePacketHeader(MoonshineProtocolConstants.Magic, MoonshineProtocolConstants.Version10, MoonshineMessageType.Hello, 100, 1, 42, 100);
        MoonshineProtocolCodec.TryWriteHeader(header, buffer);

        MoonshineErrorCode result = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        result.Should().Be(MoonshineErrorCode.PayloadTruncated);
    }

    [Fact]
    public void VideoHeaderCodec_RoundtripsSuccessfully()
    {
        var original = new MoonshineVideoPacketHeader
        {
            StreamId = 1,
            FrameIndex = 500,
            PacketIndex = 3,
            TotalPackets = 10,
            FecBlockIndex = 1,
            PayloadSize = 1380,
            PacketType = 0,
            Flags = MoonshineVideoAttributes.Keyframe | MoonshineVideoAttributes.FrameStart,
            TotalFrameBytes = 13800
        };

        byte[] buffer = new byte[32];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteVideoHeader(original, buffer);
        writeSuccess.Should().BeTrue();

        bool readSuccess = MoonshineProtocolCodec.TryReadVideoHeader(buffer, out MoonshineVideoPacketHeader decoded);
        readSuccess.Should().BeTrue();

        decoded.StreamId.Should().Be(original.StreamId);
        decoded.FrameIndex.Should().Be(original.FrameIndex);
        decoded.PacketIndex.Should().Be(original.PacketIndex);
        decoded.TotalPackets.Should().Be(original.TotalPackets);
        decoded.FecBlockIndex.Should().Be(original.FecBlockIndex);
        decoded.PayloadSize.Should().Be(original.PayloadSize);
        decoded.PacketType.Should().Be(original.PacketType);
        decoded.Flags.Should().Be(original.Flags);
    }

    [Fact]
    public void HelloCodec_ExplicitSerialization_RoundtripsIdentically()
    {
        byte[] rawUuid = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160];
        var original = new MoonshineHelloPayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 0,
            CapabilitiesMask = MoonshineCapabilities.Av1 | MoonshineCapabilities.Hdr10 | MoonshineCapabilities.Surround71,
            ClientNonce = 0xABCDEF0123456789UL,
            ClientUuid = new MoonshineUuid128(rawUuid)
        };

        byte[] buffer = new byte[32];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteHello(original, buffer);
        writeSuccess.Should().BeTrue();

        // Validate big-endian wire encoding
        buffer[0].Should().Be(0x00);
        buffer[1].Should().Be(0x01); // Major = 1
        buffer[2].Should().Be(0x00);
        buffer[3].Should().Be(0x00); // Minor = 0

        bool readSuccess = MoonshineProtocolCodec.TryReadHello(buffer, out MoonshineHelloPayload decoded);
        readSuccess.Should().BeTrue();

        decoded.ClientVersionMajor.Should().Be(original.ClientVersionMajor);
        decoded.ClientVersionMinor.Should().Be(original.ClientVersionMinor);
        decoded.CapabilitiesMask.Should().Be(original.CapabilitiesMask);
        decoded.ClientNonce.Should().Be(original.ClientNonce);
        decoded.ClientUuid.Should().Be(original.ClientUuid);
    }

    [Fact]
    public void HelloResponsePayload_Serialization_RoundtripMatchesExactFields()
    {
        var original = new MoonshineHelloResponsePayload
        {
            ServerVersionMajor = 1,
            ServerVersionMinor = 0,
            NegotiatedCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.ReedSolomonFec,
            AssignedSessionId = 0x1122334455667788UL,
            ServerNonce = 0x99AABBCCDDEEFF00UL,
            ChallengeSalt = new MoonshineUuid128(Guid.NewGuid()),
            SessionLeaseSeconds = 3600,
            Reserved = 0
        };

        byte[] buffer = new byte[48];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteHelloResponse(original, buffer);
        writeSuccess.Should().BeTrue();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHelloResponse(buffer, out MoonshineHelloResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.ServerVersionMajor.Should().Be(original.ServerVersionMajor);
        decoded.ServerVersionMinor.Should().Be(original.ServerVersionMinor);
        decoded.NegotiatedCapabilities.Should().Be(original.NegotiatedCapabilities);
        decoded.AssignedSessionId.Should().Be(original.AssignedSessionId);
        decoded.ServerNonce.Should().Be(original.ServerNonce);
        decoded.ChallengeSalt.Should().Be(original.ChallengeSalt);
        decoded.SessionLeaseSeconds.Should().Be(original.SessionLeaseSeconds);
    }

    [Fact]
    public void SessionSetupPayload_Serialization_RoundtripMatchesExactFields()
    {
        var original = new MoonshineSessionSetupPayload
        {
            VideoWidth = 2560,
            VideoHeight = 1440,
            VideoFps = 120,
            VideoBitrateKbps = 50000,
            VideoCodec = MoonshineVideoCodec.Av1,
            VideoColorFormat = MoonshineColorFormat.P010Hdr10,
            AudioChannels = 6,
            AudioCodec = MoonshineAudioCodec.Opus,
            AudioSampleRate = 48000,
            AudioBitrateKbps = 256,
            ClientUdpVideoPort = 48011,
            ClientUdpAudioPort = 48012,
            ClientUdpFeedbackPort = 48013,
            Reserved = 0,
            MtuPayloadSize = 1188
        };

        byte[] buffer = new byte[40];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteSessionSetup(original, buffer);
        writeSuccess.Should().BeTrue();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadSessionSetup(buffer, out MoonshineSessionSetupPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.VideoWidth.Should().Be(2560);
        decoded.VideoHeight.Should().Be(1440);
        decoded.VideoFps.Should().Be(120);
        decoded.VideoBitrateKbps.Should().Be(50000);
        decoded.VideoCodec.Should().Be(MoonshineVideoCodec.Av1);
        decoded.VideoColorFormat.Should().Be(MoonshineColorFormat.P010Hdr10);
        decoded.AudioChannels.Should().Be(6);
        decoded.AudioCodec.Should().Be(MoonshineAudioCodec.Opus);
        decoded.AudioSampleRate.Should().Be(48000);
        decoded.AudioBitrateKbps.Should().Be(256);
        decoded.ClientUdpVideoPort.Should().Be(48011);
        decoded.ClientUdpAudioPort.Should().Be(48012);
        decoded.ClientUdpFeedbackPort.Should().Be(48013);
        decoded.MtuPayloadSize.Should().Be(1188);
    }

    [Fact]
    public void SessionSetupResponsePayload_Serialization_RoundtripMatchesExactFields()
    {
        var original = new MoonshineSessionSetupResponsePayload
        {
            StatusCode = MoonshineErrorCode.Success,
            VideoStreamId = 101,
            AudioStreamId = 102,
            FeedbackStreamId = 103,
            HostUdpVideoPort = 48011,
            HostUdpAudioPort = 48012,
            HostUdpFeedbackPort = 48013,
            HostUdpInputPort = 48014,
            NegotiatedMtu = 1188,
            Reserved = 0
        };

        byte[] buffer = new byte[32];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteSessionSetupResponse(original, buffer);
        writeSuccess.Should().BeTrue();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadSessionSetupResponse(buffer, out MoonshineSessionSetupResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.StatusCode.Should().Be(MoonshineErrorCode.Success);
        decoded.VideoStreamId.Should().Be(101);
        decoded.AudioStreamId.Should().Be(102);
        decoded.FeedbackStreamId.Should().Be(103);
        decoded.HostUdpVideoPort.Should().Be(48011);
        decoded.HostUdpAudioPort.Should().Be(48012);
        decoded.HostUdpFeedbackPort.Should().Be(48013);
        decoded.HostUdpInputPort.Should().Be(48014);
        decoded.NegotiatedMtu.Should().Be(1188);
    }

    [Fact]
    public void TelemetryReportPayload_Serialization_RoundtripMatchesExactFields()
    {
        var original = new MoonshineTelemetryReportPayload
        {
            EncodeLatencyUs = 250,
            DecodeLatencyUs = 800,
            RenderLatencyUs = 400,
            NetworkLatencyUs = 1200,
            FramesRendered = 6000,
            FramesDropped = 2,
            FecRecoveredFrames = 5,
            Reserved = 0
        };

        byte[] buffer = new byte[32];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteTelemetryReport(original, buffer);
        writeSuccess.Should().BeTrue();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadTelemetryReport(buffer, out MoonshineTelemetryReportPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.EncodeLatencyUs.Should().Be(250);
        decoded.DecodeLatencyUs.Should().Be(800);
        decoded.RenderLatencyUs.Should().Be(400);
        decoded.NetworkLatencyUs.Should().Be(1200);
        decoded.FramesRendered.Should().Be(6000);
        decoded.FramesDropped.Should().Be(2);
        decoded.FecRecoveredFrames.Should().Be(5);
    }

    [Fact]
    public void IdrRequestPayload_Serialization_RoundtripMatchesExactFields()
    {
        var original = new MoonshineIdrRequestPayload
        {
            StreamId = 1,
            LastValidFrameIndex = 500,
            ReasonCode = 2
        };

        byte[] buffer = new byte[16];
        bool writeSuccess = MoonshineProtocolCodec.TryWriteIdrRequest(original, buffer);
        writeSuccess.Should().BeTrue();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadIdrRequest(buffer, out MoonshineIdrRequestPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.StreamId.Should().Be(1);
        decoded.LastValidFrameIndex.Should().Be(500);
        decoded.ReasonCode.Should().Be(2);
    }

    [Fact]
    public void AllMessageFamilyEnums_HaveExactExpectedWireCodes()
    {
        ((ushort)MoonshineMessageType.Hello).Should().Be(0x0101);
        ((ushort)MoonshineMessageType.HelloResponse).Should().Be(0x0102);
        ((ushort)MoonshineMessageType.SessionSetup).Should().Be(0x0103);
        ((ushort)MoonshineMessageType.SessionSetupResponse).Should().Be(0x0104);
        ((ushort)MoonshineMessageType.KeepAlive).Should().Be(0x0105);
        ((ushort)MoonshineMessageType.KeepAliveAck).Should().Be(0x0106);
        ((ushort)MoonshineMessageType.Teardown).Should().Be(0x0107);
        ((ushort)MoonshineMessageType.VideoPacket).Should().Be(0x0201);
        ((ushort)MoonshineMessageType.AudioPacket).Should().Be(0x0301);
        ((ushort)MoonshineMessageType.MicPacket).Should().Be(0x0401);
        ((ushort)MoonshineMessageType.FeedbackLossStats).Should().Be(0x0501);
        ((ushort)MoonshineMessageType.IdrRequest).Should().Be(0x0502);
        ((ushort)MoonshineMessageType.InputKeyboard).Should().Be(0x0601);
        ((ushort)MoonshineMessageType.InputMouse).Should().Be(0x0602);
        ((ushort)MoonshineMessageType.InputGamepad).Should().Be(0x0603);
        ((ushort)MoonshineMessageType.TelemetryReport).Should().Be(0x0701);
        ((ushort)MoonshineMessageType.GetHostCapabilities).Should().Be(0x0801);
        ((ushort)MoonshineMessageType.HostCapabilitiesResponse).Should().Be(0x0802);
        ((ushort)MoonshineMessageType.GetHostConfiguration).Should().Be(0x0803);
        ((ushort)MoonshineMessageType.HostConfigurationResponse).Should().Be(0x0804);
        ((ushort)MoonshineMessageType.SetHostConfiguration).Should().Be(0x0805);
        ((ushort)MoonshineMessageType.SetHostConfigurationResponse).Should().Be(0x0806);
        ((ushort)MoonshineMessageType.ConfigurationChanged).Should().Be(0x0807);
        ((ushort)MoonshineMessageType.DiscoveryProbe).Should().Be(0x0901);
        ((ushort)MoonshineMessageType.DiscoveryAnnouncement).Should().Be(0x0902);
        ((ushort)MoonshineMessageType.DiscoveryResponse).Should().Be(0x0903);
    }

    [Fact]
    public void GetHostCapabilities_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        uint queryMask = 0x01020304;
        byte[] buffer = new byte[4];

        bool writeOk = MoonshineProtocolCodec.TryWriteGetHostCapabilities(queryMask, buffer);
        writeOk.Should().BeTrue();

        buffer[0].Should().Be(0x01);
        buffer[1].Should().Be(0x02);
        buffer[2].Should().Be(0x03);
        buffer[3].Should().Be(0x04);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadGetHostCapabilities(buffer, out uint decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.Should().Be(0x01020304);
    }

    [Fact]
    public void GetHostCapabilities_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[3];

        bool writeOk = MoonshineProtocolCodec.TryWriteGetHostCapabilities(0x12345678, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadGetHostCapabilities(truncatedBuffer, out uint decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.Should().Be(0);
    }

    [Fact]
    public void HostCapabilitiesResponse_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        var original = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = 0x01020304,
            SupportedAudioCodecs = 0x05060708,
            MaxEncodeWidth = 3840,
            MaxEncodeHeight = 2160,
            MaxEncodeFps = 144,
            SupportsHdr10 = 1,
            SupportsVirtualAudio = 1,
            SupportsMicBackchannel = 1,
            Reserved = 0xAA,
            MaxBitrateKbps = 100000,
            Reserved2 = 0xDEADBEEF
        };

        byte[] buffer = new byte[32];
        bool writeOk = MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(original, buffer);
        writeOk.Should().BeTrue();

        // Exact big-endian byte offsets validation
        buffer[0].Should().Be(0x01);
        buffer[1].Should().Be(0x02);
        buffer[2].Should().Be(0x03);
        buffer[3].Should().Be(0x04);

        buffer[4].Should().Be(0x05);
        buffer[5].Should().Be(0x06);
        buffer[6].Should().Be(0x07);
        buffer[7].Should().Be(0x08);

        buffer[8].Should().Be(0x00);
        buffer[9].Should().Be(0x00);
        buffer[10].Should().Be(0x0F);
        buffer[11].Should().Be(0x00);

        buffer[12].Should().Be(0x00);
        buffer[13].Should().Be(0x00);
        buffer[14].Should().Be(0x08);
        buffer[15].Should().Be(0x70);

        buffer[16].Should().Be(0x00);
        buffer[17].Should().Be(0x00);
        buffer[18].Should().Be(0x00);
        buffer[19].Should().Be(0x90);

        buffer[20].Should().Be(1);
        buffer[21].Should().Be(1);
        buffer[22].Should().Be(1);
        buffer[23].Should().Be(0xAA);

        buffer[24].Should().Be(0x00);
        buffer[25].Should().Be(0x01);
        buffer[26].Should().Be(0x86);
        buffer[27].Should().Be(0xA0);

        buffer[28].Should().Be(0xDE);
        buffer[29].Should().Be(0xAD);
        buffer[30].Should().Be(0xBE);
        buffer[31].Should().Be(0xEF);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHostCapabilitiesResponse(buffer, out MoonshineHostCapabilitiesResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.SupportedVideoCodecs.Should().Be(original.SupportedVideoCodecs);
        decoded.SupportedAudioCodecs.Should().Be(original.SupportedAudioCodecs);
        decoded.MaxEncodeWidth.Should().Be(original.MaxEncodeWidth);
        decoded.MaxEncodeHeight.Should().Be(original.MaxEncodeHeight);
        decoded.MaxEncodeFps.Should().Be(original.MaxEncodeFps);
        decoded.SupportsHdr10.Should().Be(original.SupportsHdr10);
        decoded.SupportsVirtualAudio.Should().Be(original.SupportsVirtualAudio);
        decoded.SupportsMicBackchannel.Should().Be(original.SupportsMicBackchannel);
        decoded.Reserved.Should().Be(original.Reserved);
        decoded.MaxBitrateKbps.Should().Be(original.MaxBitrateKbps);
        decoded.Reserved2.Should().Be(original.Reserved2);
    }

    [Fact]
    public void HostCapabilitiesResponse_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[31];
        var original = new MoonshineHostCapabilitiesResponsePayload { MaxEncodeWidth = 1920 };

        bool writeOk = MoonshineProtocolCodec.TryWriteHostCapabilitiesResponse(original, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHostCapabilitiesResponse(truncatedBuffer, out MoonshineHostCapabilitiesResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.MaxEncodeWidth.Should().Be(0);
    }

    [Fact]
    public void GetHostConfiguration_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        uint queryScope = 0xAABBCCDD;
        byte[] buffer = new byte[4];

        bool writeOk = MoonshineProtocolCodec.TryWriteGetHostConfiguration(queryScope, buffer);
        writeOk.Should().BeTrue();

        buffer[0].Should().Be(0xAA);
        buffer[1].Should().Be(0xBB);
        buffer[2].Should().Be(0xCC);
        buffer[3].Should().Be(0xDD);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadGetHostConfiguration(buffer, out uint decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.Should().Be(0xAABBCCDD);
    }

    [Fact]
    public void GetHostConfiguration_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[3];

        bool writeOk = MoonshineProtocolCodec.TryWriteGetHostConfiguration(0x12345678, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadGetHostConfiguration(truncatedBuffer, out uint decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.Should().Be(0);
    }

    [Fact]
    public void HostConfiguration_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        var original = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 42,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 165,
            TargetBitrateKbps = 50000,
            MaxBitrateKbps = 80000,
            PreferredCodec = MoonshineVideoCodec.Av1,
            Hdr10Enabled = 1,
            AudioChannels = 6,
            AudioQualityMode = 2,
            AudioBitrateKbps = 512,
            InputPollingRateHz = 1000,
            MicPassthroughEnabled = 1,
            VirtualAudioDriverEnabled = 1,
            Reserved1 = 0x11223344,
            Reserved2 = 0x55667788,
            Reserved3 = 0x99AABBCC
        };

        byte[] buffer = new byte[48];
        bool writeOk = MoonshineProtocolCodec.TryWriteHostConfiguration(original, buffer);
        writeOk.Should().BeTrue();

        // Exact big-endian byte offsets validation
        buffer[0].Should().Be(0x00);
        buffer[1].Should().Be(0x00);
        buffer[2].Should().Be(0x00);
        buffer[3].Should().Be(0x2A);

        buffer[4].Should().Be(0x00);
        buffer[5].Should().Be(0x00);
        buffer[6].Should().Be(0x0A);
        buffer[7].Should().Be(0x00);

        buffer[8].Should().Be(0x00);
        buffer[9].Should().Be(0x00);
        buffer[10].Should().Be(0x05);
        buffer[11].Should().Be(0xA0);

        buffer[12].Should().Be(0x00);
        buffer[13].Should().Be(0x00);
        buffer[14].Should().Be(0x00);
        buffer[15].Should().Be(0xA5);

        buffer[16].Should().Be(0x00);
        buffer[17].Should().Be(0x00);
        buffer[18].Should().Be(0xC3);
        buffer[19].Should().Be(0x50);

        buffer[20].Should().Be(0x00);
        buffer[21].Should().Be(0x01);
        buffer[22].Should().Be(0x38);
        buffer[23].Should().Be(0x80);

        buffer[24].Should().Be((byte)MoonshineVideoCodec.Av1);
        buffer[25].Should().Be(1);
        buffer[26].Should().Be(6);
        buffer[27].Should().Be(2);

        buffer[28].Should().Be(0x00);
        buffer[29].Should().Be(0x00);
        buffer[30].Should().Be(0x02);
        buffer[31].Should().Be(0x00);

        buffer[32].Should().Be(0x03);
        buffer[33].Should().Be(0xE8);

        buffer[34].Should().Be(1);
        buffer[35].Should().Be(1);

        buffer[36].Should().Be(0x11);
        buffer[37].Should().Be(0x22);
        buffer[38].Should().Be(0x33);
        buffer[39].Should().Be(0x44);

        buffer[40].Should().Be(0x55);
        buffer[41].Should().Be(0x66);
        buffer[42].Should().Be(0x77);
        buffer[43].Should().Be(0x88);

        buffer[44].Should().Be(0x99);
        buffer[45].Should().Be(0xAA);
        buffer[46].Should().Be(0xBB);
        buffer[47].Should().Be(0xCC);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHostConfiguration(buffer, out MoonshineHostConfigurationPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.ConfigVersion.Should().Be(original.ConfigVersion);
        decoded.DisplayWidth.Should().Be(original.DisplayWidth);
        decoded.DisplayHeight.Should().Be(original.DisplayHeight);
        decoded.RefreshRateHz.Should().Be(original.RefreshRateHz);
        decoded.TargetBitrateKbps.Should().Be(original.TargetBitrateKbps);
        decoded.MaxBitrateKbps.Should().Be(original.MaxBitrateKbps);
        decoded.PreferredCodec.Should().Be(original.PreferredCodec);
        decoded.Hdr10Enabled.Should().Be(original.Hdr10Enabled);
        decoded.AudioChannels.Should().Be(original.AudioChannels);
        decoded.AudioQualityMode.Should().Be(original.AudioQualityMode);
        decoded.AudioBitrateKbps.Should().Be(original.AudioBitrateKbps);
        decoded.InputPollingRateHz.Should().Be(original.InputPollingRateHz);
        decoded.MicPassthroughEnabled.Should().Be(original.MicPassthroughEnabled);
        decoded.VirtualAudioDriverEnabled.Should().Be(original.VirtualAudioDriverEnabled);
        decoded.Reserved1.Should().Be(original.Reserved1);
        decoded.Reserved2.Should().Be(original.Reserved2);
        decoded.Reserved3.Should().Be(original.Reserved3);
    }

    [Fact]
    public void HostConfiguration_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[47];
        var original = new MoonshineHostConfigurationPayload { ConfigVersion = 1 };

        bool writeOk = MoonshineProtocolCodec.TryWriteHostConfiguration(original, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadHostConfiguration(truncatedBuffer, out MoonshineHostConfigurationPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.ConfigVersion.Should().Be(0);
    }

    [Fact]
    public void SetHostConfigurationResponse_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        var original = new MoonshineSetHostConfigurationResponsePayload
        {
            StatusCode = MoonshineErrorCode.UnauthorizedConfiguration,
            AppliedConfigVersion = 42
        };

        byte[] buffer = new byte[8];
        bool writeOk = MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(original, buffer);
        writeOk.Should().BeTrue();

        buffer[0].Should().Be(0x00);
        buffer[1].Should().Be(0x00);
        buffer[2].Should().Be(0x00);
        buffer[3].Should().Be(0x0C); // UnauthorizedConfiguration = 12 (0x0C)

        buffer[4].Should().Be(0x00);
        buffer[5].Should().Be(0x00);
        buffer[6].Should().Be(0x00);
        buffer[7].Should().Be(0x2A); // 42 = 0x2A

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(buffer, out MoonshineSetHostConfigurationResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.StatusCode.Should().Be(MoonshineErrorCode.UnauthorizedConfiguration);
        decoded.AppliedConfigVersion.Should().Be(42);
    }

    [Fact]
    public void SetHostConfigurationResponse_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[7];
        var original = new MoonshineSetHostConfigurationResponsePayload { AppliedConfigVersion = 1 };

        bool writeOk = MoonshineProtocolCodec.TryWriteSetHostConfigurationResponse(original, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(truncatedBuffer, out MoonshineSetHostConfigurationResponsePayload decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.AppliedConfigVersion.Should().Be(0);
    }

    [Fact]
    public void ConfigurationChanged_Serialisation_RoundtripAndOffsets_MatchExactWirePattern()
    {
        var original = new MoonshineConfigurationChangedPayload
        {
            NewConfigVersion = 43,
            ChangeReasonFlags = 0x00000005
        };

        byte[] buffer = new byte[8];
        bool writeOk = MoonshineProtocolCodec.TryWriteConfigurationChanged(original, buffer);
        writeOk.Should().BeTrue();

        buffer[0].Should().Be(0x00);
        buffer[1].Should().Be(0x00);
        buffer[2].Should().Be(0x00);
        buffer[3].Should().Be(0x2B); // 43 = 0x2B

        buffer[4].Should().Be(0x00);
        buffer[5].Should().Be(0x00);
        buffer[6].Should().Be(0x00);
        buffer[7].Should().Be(0x05);

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadConfigurationChanged(buffer, out MoonshineConfigurationChangedPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.Success);
        decoded.NewConfigVersion.Should().Be(43);
        decoded.ChangeReasonFlags.Should().Be(0x00000005);
    }

    [Fact]
    public void ConfigurationChanged_TruncatedBuffer_FailsGracefully()
    {
        byte[] truncatedBuffer = new byte[7];
        var original = new MoonshineConfigurationChangedPayload { NewConfigVersion = 1 };

        bool writeOk = MoonshineProtocolCodec.TryWriteConfigurationChanged(original, truncatedBuffer);
        writeOk.Should().BeFalse();

        MoonshineErrorCode readResult = MoonshineProtocolCodec.TryReadConfigurationChanged(truncatedBuffer, out MoonshineConfigurationChangedPayload decoded);
        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
        decoded.NewConfigVersion.Should().Be(0);
    }
}
