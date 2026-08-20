using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Protocol.FEC;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class FecHeaderTests
{
    [Fact]
    public void TryParse_ValidFecHeader_ExtractsShardMetadata()
    {
        byte[] raw = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(0), 42);   // BlockIndex
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(4), 8);    // ShardIndex
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(6), 8);    // DataShards
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(8), 2);    // ParityShards
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(10), 1400); // ShardSize
        raw[12] = 0x01;
        raw[13] = 0x02;

        bool success = FecHeader.TryParse(raw, out var header, out var payload);

        success.Should().BeTrue();
        header.BlockIndex.Should().Be(42);
        header.ShardIndex.Should().Be(8);
        header.DataShards.Should().Be(8);
        header.ParityShards.Should().Be(2);
        header.TotalShards.Should().Be(10);
        header.IsParityShard.Should().BeTrue();
        header.ShardSize.Should().Be(1400);
        payload.Length.Should().Be(4);
    }
}
