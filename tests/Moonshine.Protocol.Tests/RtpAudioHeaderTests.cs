using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class RtpAudioHeaderTests
{
    [Fact]
    public void TryParse_ValidAudioHeader_ExtractsOpusMetadata()
    {
        byte[] raw =
        [
            0x80, 0x61, 0x00, 0x0A, // V=2, P=0, X=0, CC=0, M=0, PT=97, Seq=10
            0x00, 0x00, 0x10, 0x00, // Timestamp = 4096
            0x12, 0x34, 0x56, 0x78, // SSRC
            0x00, 0x05,             // AudioSequenceNumber = 5
            0x00, 0x01,             // StreamId = 1
            0xFC, 0xFF, 0xFE        // Opus payload
        ];

        bool success = RtpAudioHeader.TryParse(raw, out var header, out var payload);

        success.Should().BeTrue();
        header.BaseHeader.SequenceNumber.Should().Be(10);
        header.BaseHeader.Timestamp.Should().Be(4096);
        header.BaseHeader.Ssrc.Should().Be(0x12345678);
        header.AudioSequenceNumber.Should().Be(5);
        header.StreamId.Should().Be(1);
        payload.Length.Should().Be(3);
        payload[0].Should().Be(0xFC);
    }

    [Fact]
    public void TryParse_TruncatedAudioHeader_ReturnsFalse()
    {
        byte[] shortData = [0x80, 0x61, 0x00];
        bool success = RtpAudioHeader.TryParse(shortData, out _, out var payload);

        success.Should().BeFalse();
        payload.IsEmpty.Should().BeTrue();
    }
}
