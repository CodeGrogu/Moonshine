using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class RtcpPacketTests
{
    [Fact]
    public void RtcpLossStatsPacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var stats = new RtcpLossStatsPacket(
            Ssrc: 0x12345678,
            PacketsReceived: 10000,
            PacketsLost: 150,
            PacketsRecovered: 130,
            LastSequenceNumber: 54321,
            JitterMicros: 450
        );

        byte[] buffer = new byte[64];
        int written = stats.WriteTo(buffer);
        written.Should().Be(RtcpLossStatsPacket.PacketSize);

        bool parsed = RtcpLossStatsPacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.Ssrc.Should().Be(0x12345678);
        result.PacketsReceived.Should().Be(10000);
        result.PacketsLost.Should().Be(150);
        result.PacketsRecovered.Should().Be(130);
        result.LastSequenceNumber.Should().Be(54321);
        result.JitterMicros.Should().Be(450);
    }

    [Fact]
    public void RtcpLossStatsPacket_UnrecoverableLossRate_CalculatesExpectedRatio()
    {
        var clean = new RtcpLossStatsPacket(1, 1000, 10, 10, 1000, 100);
        clean.UnrecoverableLossRate.Should().Be(0.0);

        var lossy = new RtcpLossStatsPacket(1, 900, 100, 50, 1000, 100);
        // unrecoverable: 50 / 1000 = 0.05 (5%)
        lossy.UnrecoverableLossRate.Should().BeApproximately(0.05, 0.001);
    }

    [Fact]
    public void RtcpIdrRequestPacket_WriteAndParse_RoundtripsSuccessfully()
    {
        var idr = new RtcpIdrRequestPacket(SenderSsrc: 0xCAFEBABE, MediaSsrc: 0xDEADBEEF);
        byte[] buffer = new byte[32];

        int written = idr.WriteTo(buffer);
        written.Should().Be(RtcpIdrRequestPacket.PacketSize);

        bool parsed = RtcpIdrRequestPacket.TryParse(buffer.AsSpan(0, written), out var result);
        parsed.Should().BeTrue();
        result.SenderSsrc.Should().Be(0xCAFEBABE);
        result.MediaSsrc.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void RtcpPacket_BufferTooSmall_ReturnsFailure()
    {
        byte[] smallBuffer = new byte[4];
        var stats = new RtcpLossStatsPacket(1, 10, 0, 0, 10, 0);
        stats.WriteTo(smallBuffer).Should().Be(-1);

        RtcpLossStatsPacket.TryParse(smallBuffer, out _).Should().BeFalse();
        RtcpIdrRequestPacket.TryParse(smallBuffer, out _).Should().BeFalse();
    }
}
