using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Protocol.Control;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class ControlPacketTests
{
    [Fact]
    public void ControlHeader_TryParse_ValidBuffer_ExtractsHeaderAndPayload()
    {
        byte[] raw = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(0, 2), (ushort)ControlPacketType.Ping);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(2, 2), 4);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(4, 4), 100);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(8, 4), 0xCAFEBABE);

        bool success = ControlHeader.TryParse(raw, out var header, out var payload);

        success.Should().BeTrue();
        header.PacketType.Should().Be(ControlPacketType.Ping);
        header.PayloadLength.Should().Be(4);
        header.SequenceNumber.Should().Be(100);
        payload.Length.Should().Be(4);
    }

    [Fact]
    public void ControlHeader_TryParse_TruncatedBuffer_ReturnsFalse()
    {
        byte[] raw = [0x14, 0x01, 0x00];
        bool success = ControlHeader.TryParse(raw, out _, out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void LossStatsPayload_StructSize_MatchesPackedLayout()
    {
        Marshal.SizeOf<LossStatsPayload>().Should().Be(16);
    }
}
