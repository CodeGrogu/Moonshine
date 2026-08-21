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
        // Arrange: 12-byte NvVideoHeader + 4-byte payload
        byte[] raw = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 1024);   // FrameIndex = 1024
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 3);      // PacketIndex = 3
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(8), 8);      // TotalPackets = 8
        raw[10] = 0x05;                                                 // Flags = StartOfFrame (0x01) | Keyframe (0x04)
        raw[11] = 0x00;                                                 // Reserved
        raw[12] = 0xDE;
        raw[13] = 0xAD;
        raw[14] = 0xBE;
        raw[15] = 0xEF;

        // Act
        bool success = NvVideoHeader.TryParse(raw, out var header, out var payload);

        // Assert
        success.Should().BeTrue();
        header.FrameIndex.Should().Be(1024);
        header.PacketIndex.Should().Be(3);
        header.TotalPackets.Should().Be(8);
        header.Flags.Should().Be(0x05);
        header.IsStartOfFrame.Should().BeTrue();
        header.IsEndOfFrame.Should().BeFalse();
        header.IsKeyframe.Should().BeTrue();
        payload.Length.Should().Be(4);
        payload[0].Should().Be(0xDE);
    }

    [Fact]
    public void TryParse_InvalidTotalPacketsOrIndex_ReturnsFalse()
    {
        byte[] rawZeroTotal = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(rawZeroTotal.AsSpan(0), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(rawZeroTotal.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(rawZeroTotal.AsSpan(8), 0); // TotalPackets = 0 (invalid)

        NvVideoHeader.TryParse(rawZeroTotal, out _, out _).Should().BeFalse();

        byte[] rawIndexOutOfBounds = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(rawIndexOutOfBounds.AsSpan(0), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(rawIndexOutOfBounds.AsSpan(4), 5); // PacketIndex = 5
        BinaryPrimitives.WriteUInt16LittleEndian(rawIndexOutOfBounds.AsSpan(8), 5); // TotalPackets = 5 (index must be < total)

        NvVideoHeader.TryParse(rawIndexOutOfBounds, out _, out _).Should().BeFalse();
    }
}
