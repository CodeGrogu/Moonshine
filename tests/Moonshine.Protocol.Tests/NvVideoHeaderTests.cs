using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class NvVideoHeaderTests
{
    [Fact]
    public void TryParse_ValidNvVideoHeader_CorrectlyParsesFields()
    {
        // Arrange: 16-byte GameStream NV_VIDEO_PACKET + 4-byte payload
        byte[] raw = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 0x12345600); // Stream packet index = 0x123456
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 1024);       // Frame index = 1024
        raw[8] = 0x07;                                                       // Picture data | End | Start
        raw[9] = 0x01;                                                       // LTR frame extension
        raw[10] = 0xA5;                                                      // Multi-FEC flags
        raw[11] = 0x90;                                                      // FEC block index = 1, last block = 2
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 0xA1B2C3D4);
        raw[16] = 0xDE;
        raw[17] = 0xAD;
        raw[18] = 0xBE;
        raw[19] = 0xEF;

        // Act
        bool success = NvVideoHeader.TryParse(raw, out var header, out var payload);

        // Assert
        success.Should().BeTrue();
        header.FrameIndex.Should().Be(1024);
        header.RawStreamPacketIndex.Should().Be(0x12345600);
        header.StreamPacketIndex.Should().Be(0x123456);
        header.Flags.Should().Be(0x07);
        header.ExtraFlags.Should().Be(0x01);
        header.MultiFecFlags.Should().Be(0xA5);
        header.FecInfo.Should().Be(0xA1B2C3D4);
        header.ContainsPictureData.Should().BeTrue();
        header.IsStartOfFrame.Should().BeTrue();
        header.IsEndOfFrame.Should().BeTrue();
        header.FecBlockIndex.Should().Be(1);
        header.LastFecBlockIndex.Should().Be(2);
        payload.Length.Should().Be(4);
        payload[0].Should().Be(0xDE);
    }

    [Fact]
    public void TryParse_TruncatedHeader_ReturnsFalse()
    {
        byte[] truncated = new byte[NvVideoHeader.Size - 1];

        NvVideoHeader.TryParse(truncated, out _, out _).Should().BeFalse();
    }
}
