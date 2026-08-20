using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class RtpHeaderTests
{
    [Fact]
    public void TryParse_ValidRtpPacket_CorrectlyParsesHeaderFields()
    {
        // Arrange: 12-byte RTP header + 4-byte payload
        byte[] raw = new byte[16];
        raw[0] = 0x80; // V=2, P=0, X=0, CC=0
        raw[1] = 0xE0; // M=1, PT=96 (0x60 | 0x80)
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(2), 12345);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(4), 987654321);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(8), 0xDEADBEEF);
        raw[12] = 0xAA;
        raw[13] = 0xBB;
        raw[14] = 0xCC;
        raw[15] = 0xDD;

        // Act
        bool success = RtpHeader.TryParse(raw, out var header, out var payload);

        // Assert
        success.Should().BeTrue();
        header.Version.Should().Be(2);
        header.HasPadding.Should().BeFalse();
        header.HasExtension.Should().BeFalse();
        header.CsrcCount.Should().Be(0);
        header.Marker.Should().BeTrue();
        header.PayloadId.Should().Be(96);
        header.SequenceNumber.Should().Be(12345);
        header.Timestamp.Should().Be(987654321);
        header.Ssrc.Should().Be(0xDEADBEEF);
        payload.Length.Should().Be(4);
        payload[0].Should().Be(0xAA);
    }

    [Fact]
    public void SequenceUnwrapper_HandlesWraparoundCorrectly()
    {
        var unwrapper = new RtpSequenceUnwrapper();
        unwrapper.Unwrap(65530).Should().Be(65530);
        unwrapper.Unwrap(65535).Should().Be(65535);
        unwrapper.Unwrap(0).Should().Be(65536);
        unwrapper.Unwrap(5).Should().Be(65541);
    }
}
