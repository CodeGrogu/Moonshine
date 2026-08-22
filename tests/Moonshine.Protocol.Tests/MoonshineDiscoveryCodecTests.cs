using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Discovery;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MoonshineDiscoveryCodecTests
{
    [Fact]
    public void DiscoveryProbe_RoundtripsSuccessfully()
    {
        var original = new MoonshineDiscoveryProbePayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 2,
            ClientUuid = new MoonshineUuid128(Guid.NewGuid()),
            DesiredCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.Hdr10,
            Reserved = 0,
            ProbeNonce = 0x1122334455667788UL
        };

        byte[] buffer = new byte[MoonshineDiscoveryCodec.ProbePacketSize];
        bool writeSuccess = MoonshineDiscoveryCodec.TryWriteProbe(original, buffer, out int bytesWritten, sequenceNumber: 42);

        writeSuccess.Should().BeTrue();
        bytesWritten.Should().Be(MoonshineDiscoveryCodec.ProbePacketSize);

        // Verify wire header
        buffer[0].Should().Be(0x4D); // 'M'
        buffer[1].Should().Be(0x53); // 'S'
        buffer[2].Should().Be(0x48); // 'H'
        buffer[3].Should().Be(0x4E); // 'N'

        MoonshineErrorCode readResult = MoonshineDiscoveryCodec.TryReadProbe(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineDiscoveryProbePayload decoded);

        readResult.Should().Be(MoonshineErrorCode.Success);
        header.Magic.Should().Be(MoonshineProtocolConstants.Magic);
        header.Version.Should().Be(MoonshineProtocolConstants.Version10);
        header.MessageType.Should().Be(MoonshineMessageType.DiscoveryProbe);
        header.PayloadSize.Should().Be(MoonshineDiscoveryCodec.ProbePayloadSize);
        header.SequenceNumber.Should().Be(42);

        decoded.ClientVersionMajor.Should().Be(original.ClientVersionMajor);
        decoded.ClientVersionMinor.Should().Be(original.ClientVersionMinor);
        decoded.ClientUuid.Should().Be(original.ClientUuid);
        decoded.DesiredCapabilities.Should().Be(original.DesiredCapabilities);
        decoded.ProbeNonce.Should().Be(original.ProbeNonce);
    }

    [Fact]
    public unsafe void DiscoveryAnnouncement_RoundtripsSuccessfully()
    {
        var hostUuid = new MoonshineUuid128(Guid.NewGuid());
        var original = new MoonshineDiscoveryAnnouncementPayload
        {
            HostVersionMajor = 1,
            HostVersionMinor = 0,
            HostUuid = hostUuid,
            SupportedCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.H264 | MoonshineCapabilities.ReedSolomonFec,
            ControlTcpPort = 48010,
            DiscoveryUdpPort = 48010,
            VideoUdpPort = 47998,
            AudioUdpPort = 48000,
            ControlFeedbackUdpPort = 47999,
            MicUdpPort = 48002,
            MaxBitrateKbps = 150000,
            SupportsHdr10 = 1,
            SupportsVirtualAudio = 1,
            SupportsMicBackchannel = 0,
            IsPaired = 1,
            AdvertisementNonce = 0x9988776655443322UL
        };

        MoonshineDiscoveryCodec.SetFixedUtf8String(original.Hostname, 64, "RIG-DESKTOP");
        MoonshineDiscoveryCodec.SetFixedUtf8String(original.GpuName, 64, "NVIDIA GeForce RTX 4090");

        byte[] buffer = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];
        bool writeSuccess = MoonshineDiscoveryCodec.TryWriteAnnouncement(original, buffer, out int bytesWritten, sequenceNumber: 100);

        writeSuccess.Should().BeTrue();
        bytesWritten.Should().Be(MoonshineDiscoveryCodec.AnnouncementPacketSize);

        MoonshineErrorCode readResult = MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineDiscoveryAnnouncementPayload decoded);

        readResult.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.DiscoveryAnnouncement);
        header.PayloadSize.Should().Be(MoonshineDiscoveryCodec.AnnouncementPayloadSize);
        header.SequenceNumber.Should().Be(100);

        decoded.HostVersionMajor.Should().Be(1);
        decoded.HostVersionMinor.Should().Be(0);
        decoded.HostUuid.Should().Be(hostUuid);
        decoded.SupportedCapabilities.Should().Be(original.SupportedCapabilities);
        decoded.ControlTcpPort.Should().Be(48010);
        decoded.DiscoveryUdpPort.Should().Be(48010);
        decoded.VideoUdpPort.Should().Be(47998);
        decoded.AudioUdpPort.Should().Be(48000);
        decoded.ControlFeedbackUdpPort.Should().Be(47999);
        decoded.MicUdpPort.Should().Be(48002);
        decoded.MaxBitrateKbps.Should().Be(150000);
        decoded.SupportsHdr10.Should().Be(1);
        decoded.SupportsVirtualAudio.Should().Be(1);
        decoded.SupportsMicBackchannel.Should().Be(0);
        decoded.IsPaired.Should().Be(1);
        decoded.AdvertisementNonce.Should().Be(0x9988776655443322UL);

        string hostname = MoonshineDiscoveryCodec.GetFixedUtf8String(decoded.Hostname, 64);
        string gpuName = MoonshineDiscoveryCodec.GetFixedUtf8String(decoded.GpuName, 64);

        hostname.Should().Be("RIG-DESKTOP");
        gpuName.Should().Be("NVIDIA GeForce RTX 4090");
    }

    [Fact]
    public unsafe void DiscoveryResponse_RoundtripsSuccessfully()
    {
        var original = new MoonshineDiscoveryAnnouncementPayload
        {
            HostVersionMajor = 1,
            HostVersionMinor = 1,
            HostUuid = new MoonshineUuid128(Guid.NewGuid()),
            SupportedCapabilities = MoonshineCapabilities.Av1,
            ControlTcpPort = 48010,
            DiscoveryUdpPort = 48010,
            VideoUdpPort = 47998,
            AudioUdpPort = 48000,
            ControlFeedbackUdpPort = 47999,
            MicUdpPort = 48002,
            MaxBitrateKbps = 100000,
            SupportsHdr10 = 0,
            SupportsVirtualAudio = 0,
            SupportsMicBackchannel = 1,
            IsPaired = 0,
            AdvertisementNonce = 0xAABBCCDDEEFF0011UL
        };

        byte[] buffer = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];
        bool writeSuccess = MoonshineDiscoveryCodec.TryWriteResponse(original, buffer, out int written, sequenceNumber: 5, sessionId: 0x1234567890ABCDEF);
        writeSuccess.Should().BeTrue();
        written.Should().Be(MoonshineDiscoveryCodec.AnnouncementPacketSize);

        MoonshineErrorCode readResult = MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineDiscoveryAnnouncementPayload decoded);

        readResult.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.DiscoveryResponse);
        header.SessionId.Should().Be(0x1234567890ABCDEF);
        decoded.AdvertisementNonce.Should().Be(0xAABBCCDDEEFF0011UL);
    }

    [Fact]
    public void DiscoveryCodec_BufferTooSmall_ReturnsFalseOrError()
    {
        byte[] tinyBuffer = new byte[16];
        var probePayload = new MoonshineDiscoveryProbePayload();
        var announcePayload = new MoonshineDiscoveryAnnouncementPayload();

        MoonshineDiscoveryCodec.TryWriteProbe(probePayload, tinyBuffer, out _).Should().BeFalse();
        MoonshineDiscoveryCodec.TryWriteAnnouncement(announcePayload, tinyBuffer, out _).Should().BeFalse();
        MoonshineDiscoveryCodec.TryWriteResponse(announcePayload, tinyBuffer, out _).Should().BeFalse();

        MoonshineDiscoveryCodec.TryReadProbe(tinyBuffer, out _, out _).Should().Be(MoonshineErrorCode.BufferTooSmall);
        MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(tinyBuffer, out _, out _).Should().Be(MoonshineErrorCode.BufferTooSmall);
    }

    [Fact]
    public void DiscoveryCodec_WrongMessageType_ReturnsMalformedHeader()
    {
        // Write a Hello packet
        byte[] buffer = new byte[128];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.Hello,
            PayloadSize: MoonshineDiscoveryCodec.ProbePayloadSize,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 1000);

        MoonshineProtocolCodec.TryWriteHeader(header, buffer);

        MoonshineErrorCode probeResult = MoonshineDiscoveryCodec.TryReadProbe(buffer, out _, out _);
        probeResult.Should().Be(MoonshineErrorCode.MalformedHeader);

        MoonshineErrorCode announceResult = MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(buffer, out _, out _);
        announceResult.Should().Be(MoonshineErrorCode.MalformedHeader);
    }

    [Fact]
    public unsafe void FixedUtf8String_HandlesEdgeCases_Correctly()
    {
        byte[] buffer = new byte[64];
        fixed (byte* p = buffer)
        {
            // Empty string
            MoonshineDiscoveryCodec.SetFixedUtf8String(p, 64, string.Empty);
            MoonshineDiscoveryCodec.GetFixedUtf8String(p, 64).Should().Be(string.Empty);

            // Standard string
            MoonshineDiscoveryCodec.SetFixedUtf8String(p, 64, "My-Host-PC");
            MoonshineDiscoveryCodec.GetFixedUtf8String(p, 64).Should().Be("My-Host-PC");

            // String longer than buffer capacity
            string veryLong = new string('A', 100);
            MoonshineDiscoveryCodec.SetFixedUtf8String(p, 64, veryLong);
            string readBack = MoonshineDiscoveryCodec.GetFixedUtf8String(p, 64);
            readBack.Length.Should().Be(63); // 63 chars + 1 null terminator
            readBack.Should().Be(new string('A', 63));
        }
    }
}
