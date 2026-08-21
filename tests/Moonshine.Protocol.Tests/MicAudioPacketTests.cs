using Moonshine.Protocol.Audio;
using Xunit;

namespace Moonshine.Protocol.Tests;

public sealed class MicAudioPacketTests
{
    [Fact]
    public void TryWrite_And_TryParse_Succeeds()
    {
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60];
        Span<byte> datagram = stackalloc byte[128];

        bool written = MicAudioPacket.TryWrite(
            payload,
            sequenceNumber: 1042,
            timestamp: 480000,
            ssrc: 0xDEADBEEF,
            marker: true,
            payloadType: 98,
            datagram,
            out int bytesWritten
        );

        Assert.True(written);
        Assert.Equal(12 + payload.Length, bytesWritten);

        ReadOnlySpan<byte> slice = datagram.Slice(0, bytesWritten);
        bool parsed = MicAudioPacket.TryParse(slice, out MicAudioPacket packet);

        Assert.True(parsed);
        Assert.Equal(98, packet.PayloadType);
        Assert.True(packet.Marker);
        Assert.Equal((ushort)1042, packet.SequenceNumber);
        Assert.Equal(480000u, packet.Timestamp);
        Assert.Equal(0xDEADBEEFu, packet.Ssrc);
        Assert.Equal(payload.Length, packet.Payload.Length);
        Assert.True(payload.AsSpan().SequenceEqual(packet.Payload));
    }

    [Fact]
    public void TryParse_InvalidLength_ReturnsFalse()
    {
        Span<byte> small = stackalloc byte[11];
        Assert.False(MicAudioPacket.TryParse(small, out _));
    }

    [Fact]
    public void TryWrite_DestinationTooSmall_ReturnsFalse()
    {
        byte[] payload = new byte[100];
        Span<byte> smallDest = stackalloc byte[50];

        Assert.False(MicAudioPacket.TryWrite(
            payload,
            1,
            0,
            0,
            false,
            98,
            smallDest,
            out int written
        ));
        Assert.Equal(0, written);
    }
}
