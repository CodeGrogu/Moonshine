#if MOONSHINE_LEGACY_INTEROP
using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

public class RtpAudioPacketiserTests
{
    [Fact]
    public void RtpAudioPacketiser_Packetise_ProducesValidRtpHeader()
    {
        var packetiser = new RtpAudioPacketiser(payloadType: 97, ssrc: 0xDEADBEEF, initialSeq: 100);

        byte[] pcmPayload = new byte[480];
        Array.Fill(pcmPayload, (byte)0xAB);

        byte[] rtpPacket = new byte[1024];
        bool ok = packetiser.TryPacketise(pcmPayload, timestamp: 96000, marker: true, rtpPacket, out int written);

        ok.Should().BeTrue();
        written.Should().Be(RtpAudioPacketiser.RtpHeaderSize + pcmPayload.Length);

        // Check Version & Marker / Payload Type
        rtpPacket[0].Should().Be(0x80);
        rtpPacket[1].Should().Be((byte)(0x80 | 97)); // Marker bit set + PT 97

        // Sequence number (100)
        ushort seq = BinaryPrimitives.ReadUInt16BigEndian(rtpPacket.AsSpan(2, 2));
        seq.Should().Be(100);

        // Timestamp (96000)
        uint ts = BinaryPrimitives.ReadUInt32BigEndian(rtpPacket.AsSpan(4, 4));
        ts.Should().Be(96000);

        // SSRC (0xDEADBEEF)
        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(rtpPacket.AsSpan(8, 4));
        ssrc.Should().Be(0xDEADBEEF);

        // Payload matches
        rtpPacket[12].Should().Be(0xAB);
        rtpPacket[12 + pcmPayload.Length - 1].Should().Be(0xAB);
    }

    [Fact]
    public void RtpAudioPacketiser_SequentialPackets_IncrementsSequenceNumber()
    {
        var packetiser = new RtpAudioPacketiser(payloadType: 97, ssrc: 0x11223344, initialSeq: 500);

        byte[] payload = [1, 2, 3, 4];
        byte[] packet = new byte[64];

        packetiser.TryPacketise(payload, 1000, false, packet, out _);
        packetiser.CurrentSequenceNumber.Should().Be(501);

        packetiser.TryPacketise(payload, 2000, false, packet, out _);
        packetiser.CurrentSequenceNumber.Should().Be(502);
    }

    [Fact]
    public void RtpAudioPacketiser_InsufficientBuffer_ReturnsFalse()
    {
        var packetiser = new RtpAudioPacketiser();
        byte[] payload = new byte[100];
        byte[] tinyBuffer = new byte[50];

        bool ok = packetiser.TryPacketise(payload, 1000, false, tinyBuffer, out int written);
        ok.Should().BeFalse();
        written.Should().Be(0);
    }
}
#endif
