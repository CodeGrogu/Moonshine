using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Moonshine.Protocol.Discovery.Mdns;

public enum DnsRecordType : ushort
{
    A = 1,
    PTR = 12,
    TXT = 16,
    SRV = 33,
    ANY = 255
}

public enum DnsClass : ushort
{
    IN = 1,
    ANY = 255
}

/// <summary>
/// Parsed service record discovered via Multicast DNS (RFC 6762).
/// </summary>
public sealed record MdnsServiceRecord(
    string ServiceName,
    string HostTarget,
    IPAddress? IpAddress,
    ushort Port,
    Dictionary<string, string> Attributes,
    uint Ttl
);

/// <summary>
/// High-performance, zero-allocation Multicast DNS (mDNS) packet builder and parser over ReadOnlySpan.
/// </summary>
public static class MdnsCodec
{
    public const int DefaultMdnsPort = 5353;
    public static readonly IPAddress MdnsMulticastIpv4 = IPAddress.Parse("224.0.0.251");

    /// <summary>
    /// Encodes an mDNS PTR query for a service name (e.g. "_nvstream._tcp.local").
    /// </summary>
    public static int EncodeQuery(Span<byte> destination, string serviceName, bool unicastResponse = false)
    {
        if (destination.Length < 12 + serviceName.Length + 6)
        {
            throw new ArgumentException("Destination buffer is too small for mDNS query.", nameof(destination));
        }

        // Header: Transaction ID (0), Flags (0 for query), Questions (1), Answers (0), Auth (0), Add (0)
        BinaryPrimitives.WriteUInt16BigEndian(destination[0..2], 0x0000); // ID = 0
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], 0x0000); // Standard Query
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..6], 1);      // Questions = 1
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], 0);      // Answers = 0
        BinaryPrimitives.WriteUInt16BigEndian(destination[8..10], 0);     // Authority = 0
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..12], 0);    // Additional = 0

        int offset = 12;

        // Encode QNAME: split by '.' into length-prefixed labels
        string[] labels = serviceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (string label in labels)
        {
            int labelLength = Encoding.UTF8.GetByteCount(label);
            destination[offset++] = (byte)labelLength;
            Encoding.UTF8.GetBytes(label, destination.Slice(offset, labelLength));
            offset += labelLength;
        }
        destination[offset++] = 0x00; // Terminating null label

        // QTYPE = PTR (12)
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset, 2), (ushort)DnsRecordType.PTR);
        offset += 2;

        // QCLASS = IN (1) | Unicast-response bit (0x8000 if requested)
        ushort qclass = (ushort)((ushort)DnsClass.IN | (unicastResponse ? 0x8000 : 0x0000));
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset, 2), qclass);
        offset += 2;

        return offset;
    }

    /// <summary>
    /// Parses an incoming mDNS response packet into an MdnsServiceRecord.
    /// </summary>
    public static bool TryParseResponse(ReadOnlySpan<byte> packet, out MdnsServiceRecord? serviceRecord)
    {
        serviceRecord = null;
        if (packet.Length < 12)
        {
            return false;
        }

        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        bool isResponse = (flags & 0x8000) != 0;
        if (!isResponse)
        {
            return false;
        }

        ushort questionsCount = BinaryPrimitives.ReadUInt16BigEndian(packet[4..6]);
        ushort answersCount = BinaryPrimitives.ReadUInt16BigEndian(packet[6..8]);
        ushort authorityCount = BinaryPrimitives.ReadUInt16BigEndian(packet[8..10]);
        ushort additionalCount = BinaryPrimitives.ReadUInt16BigEndian(packet[10..12]);

        int totalRecords = answersCount + authorityCount + additionalCount;
        if (totalRecords == 0)
        {
            return false;
        }

        int offset = 12;

        // Skip Questions
        for (int q = 0; q < questionsCount; q++)
        {
            if (!TrySkipName(packet, ref offset)) return false;
            if (offset + 4 > packet.Length) return false;
            offset += 4; // Skip QTYPE and QCLASS
        }

        string discoveredService = string.Empty;
        string hostTarget = string.Empty;
        IPAddress? ipAddress = null;
        ushort port = 0;
        uint recordTtl = 120;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Parse Resource Records
        for (int r = 0; r < totalRecords && offset < packet.Length; r++)
        {
            if (!TryReadName(packet, ref offset, out string recordName))
            {
                break;
            }

            if (offset + 10 > packet.Length) break;

            ushort rtype = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));
            ushort rclass = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2));
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(offset + 4, 4));
            ushort rdlength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 8, 2));
            offset += 10;

            if (offset + rdlength > packet.Length) break;
            recordTtl = ttl;

            ReadOnlySpan<byte> rdata = packet.Slice(offset, rdlength);

            switch ((DnsRecordType)rtype)
            {
                case DnsRecordType.PTR:
                    int rdataOffset = offset;
                    if (TryReadName(packet, ref rdataOffset, out string ptrTarget))
                    {
                        discoveredService = ptrTarget;
                    }
                    break;

                case DnsRecordType.SRV:
                    if (rdlength >= 6)
                    {
                        port = BinaryPrimitives.ReadUInt16BigEndian(rdata[4..6]);
                        int srvOffset = offset + 6;
                        if (TryReadName(packet, ref srvOffset, out string srvTarget))
                        {
                            hostTarget = srvTarget;
                        }
                    }
                    break;

                case DnsRecordType.A:
                    if (rdlength == 4)
                    {
                        ipAddress = new IPAddress(rdata.ToArray());
                    }
                    break;

                case DnsRecordType.TXT:
                    ParseTxtAttributes(rdata, attributes);
                    break;
            }

            offset += rdlength;
        }

        if (string.IsNullOrEmpty(discoveredService) && string.IsNullOrEmpty(hostTarget) && ipAddress == null)
        {
            return false;
        }

        serviceRecord = new MdnsServiceRecord(
            ServiceName: discoveredService,
            HostTarget: hostTarget,
            IpAddress: ipAddress,
            Port: port,
            Attributes: attributes,
            Ttl: recordTtl
        );

        return true;
    }

    private static bool TrySkipName(ReadOnlySpan<byte> packet, ref int offset)
    {
        while (offset < packet.Length)
        {
            byte len = packet[offset++];
            if (len == 0) return true;
            if ((len & 0xC0) == 0xC0) // Compression pointer (2 bytes)
            {
                offset++; // Skip 2nd pointer byte
                return true;
            }
            offset += len;
        }
        return false;
    }

    public static bool TryReadName(ReadOnlySpan<byte> packet, ref int offset, out string name)
    {
        name = string.Empty;
        var sb = new StringBuilder();
        int currentOffset = offset;
        bool jumped = false;
        int maxJumps = 5;

        while (currentOffset < packet.Length)
        {
            byte len = packet[currentOffset++];
            if (len == 0)
            {
                break;
            }

            if ((len & 0xC0) == 0xC0)
            {
                if (currentOffset >= packet.Length) return false;
                byte b2 = packet[currentOffset++];
                int pointer = ((len & 0x3F) << 8) | b2;

                if (!jumped)
                {
                    offset = currentOffset;
                    jumped = true;
                }

                if (--maxJumps <= 0) return false;
                currentOffset = pointer;
                continue;
            }

            if (currentOffset + len > packet.Length) return false;

            if (sb.Length > 0)
            {
                sb.Append('.');
            }
            sb.Append(Encoding.UTF8.GetString(packet.Slice(currentOffset, len)));
            currentOffset += len;
        }

        if (!jumped)
        {
            offset = currentOffset;
        }

        name = sb.ToString();
        return true;
    }

    private static void ParseTxtAttributes(ReadOnlySpan<byte> rdata, Dictionary<string, string> attributes)
    {
        int offset = 0;
        while (offset < rdata.Length)
        {
            int entryLen = rdata[offset++];
            if (offset + entryLen > rdata.Length) break;

            string entry = Encoding.UTF8.GetString(rdata.Slice(offset, entryLen));
            offset += entryLen;

            int eqIdx = entry.IndexOf('=');
            if (eqIdx > 0)
            {
                string key = entry[..eqIdx];
                string val = entry[(eqIdx + 1)..];
                attributes[key] = val;
            }
            else
            {
                attributes[entry] = string.Empty;
            }
        }
    }
}
