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
        Marshal.SizeOf<MoonshineFeedbackLossStatsPayload>().Should().Be(36);
        Marshal.SizeOf<MoonshineIdrRequestPayload>().Should().Be(16);
        Marshal.SizeOf<MoonshineInputKeyboardPayload>().Should().Be(12);
        Marshal.SizeOf<MoonshineInputMousePayload>().Should().Be(20);
        Marshal.SizeOf<MoonshineInputGamepadPayload>().Should().Be(24);
        Marshal.SizeOf<MoonshineTelemetryReportPayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineHostCapabilitiesResponsePayload>().Should().Be(32);
        Marshal.SizeOf<MoonshineHostConfigurationPayload>().Should().Be(48);
        Marshal.SizeOf<MoonshineSetHostConfigurationResponsePayload>().Should().Be(8);
        Marshal.SizeOf<MoonshineConfigurationChangedPayload>().Should().Be(8);
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
            Reserved = 0
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
    }
}
