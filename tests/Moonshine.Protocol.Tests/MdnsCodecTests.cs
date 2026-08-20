using System.Buffers.Binary;
using System.Net;
using System.Text;
using FluentAssertions;
using Moonshine.Protocol.Discovery.Mdns;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MdnsCodecTests
{
    [Fact]
    public void EncodeQuery_ValidServiceName_ConstructsStandardDnsQuery()
    {
        byte[] buffer = new byte[256];
        int length = MdnsCodec.EncodeQuery(buffer, "_nvstream._tcp.local");

        length.Should().BeGreaterThan(12);

        // Check DNS Header
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, 2)).Should().Be(0); // ID = 0
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2)).Should().Be(0); // Flags = 0 (Query)
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4, 2)).Should().Be(1); // Questions = 1
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6, 2)).Should().Be(0); // Answers = 0

        // Check QNAME labels: 9 '_nvstream', 4 '_tcp', 5 'local', 0
        buffer[12].Should().Be(9);
        Encoding.UTF8.GetString(buffer, 13, 9).Should().Be("_nvstream");
        buffer[22].Should().Be(4);
        Encoding.UTF8.GetString(buffer, 23, 4).Should().Be("_tcp");
        buffer[27].Should().Be(5);
        Encoding.UTF8.GetString(buffer, 28, 5).Should().Be("local");
        buffer[33].Should().Be(0);

        // QTYPE = PTR (12), QCLASS = IN (1)
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(34, 2)).Should().Be(12);
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(36, 2)).Should().Be(1);
    }

    [Fact]
    public void TryParseResponse_ValidMdnsPacketWithAAndTxt_ExtractsServiceRecord()
    {
        // Construct simulated mDNS response packet
        var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Header: ID=0, Flags=0x8400 (Response, Authoritative), Questions=0, Answers=2, Auth=0, Add=0
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)0x8400));
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)2));
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)0));

        // Record 1: A Record
        // Name: "host.local" (4 'host', 5 'local', 0)
        bw.Write((byte)4);
        bw.Write(Encoding.UTF8.GetBytes("host"));
        bw.Write((byte)5);
        bw.Write(Encoding.UTF8.GetBytes("local"));
        bw.Write((byte)0);

        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)1)); // Type = A
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)1)); // Class = IN
        bw.Write(BinaryPrimitives.ReverseEndianness((uint)120)); // TTL = 120
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)4));   // RDLength = 4
        bw.Write(new byte[] { 192, 168, 1, 100 });              // 192.168.1.100

        // Record 2: TXT Record
        // Compression pointer to "host.local" at offset 12 -> 0xC00C
        bw.Write((byte)0xC0);
        bw.Write((byte)0x0C);

        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)16)); // Type = TXT
        bw.Write(BinaryPrimitives.ReverseEndianness((ushort)1));  // Class = IN
        bw.Write(BinaryPrimitives.ReverseEndianness((uint)120));  // TTL = 120

        string txt1 = "model=Sunshine";
        string txt2 = "version=0.23.1";
        ushort txtLen = (ushort)(1 + txt1.Length + 1 + txt2.Length);
        bw.Write(BinaryPrimitives.ReverseEndianness(txtLen)); // RDLength

        bw.Write((byte)txt1.Length);
        bw.Write(Encoding.UTF8.GetBytes(txt1));
        bw.Write((byte)txt2.Length);
        bw.Write(Encoding.UTF8.GetBytes(txt2));

        byte[] rawPacket = ms.ToArray();

        bool success = MdnsCodec.TryParseResponse(rawPacket, out var record);

        success.Should().BeTrue();
        record.Should().NotBeNull();
        record!.IpAddress.Should().Be(IPAddress.Parse("192.168.1.100"));
        record.Attributes["model"].Should().Be("Sunshine");
        record.Attributes["version"].Should().Be("0.23.1");
        record.Ttl.Should().Be(120);
    }

    [Fact]
    public void TryParseResponse_QueryPacketNotResponse_ReturnsFalse()
    {
        byte[] queryPacket = new byte[32];
        BinaryPrimitives.WriteUInt16BigEndian(queryPacket.AsSpan(2, 2), 0x0000); // Standard Query flag

        bool success = MdnsCodec.TryParseResponse(queryPacket, out var record);
        success.Should().BeFalse();
        record.Should().BeNull();
    }
}
