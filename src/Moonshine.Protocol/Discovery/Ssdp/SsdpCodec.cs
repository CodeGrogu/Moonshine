using System.Net;
using System.Text;

namespace Moonshine.Protocol.Discovery.Ssdp;

/// <summary>
/// Parsed SSDP advertisement or discovery response.
/// </summary>
public sealed record SsdpDeviceRecord(
    string Location,
    string ServiceType,
    string UniqueServiceName,
    string ServerHeader,
    int MaxAgeSeconds,
    IPAddress? HostIp,
    int HostPort
);

/// <summary>
/// High-performance, zero-allocation Simple Service Discovery Protocol (SSDP / UPnP) builder and parser.
/// </summary>
public static class SsdpCodec
{
    public const int DefaultSsdpPort = 1900;
    public const int SunshineSsdpPort = 48010;
    public static readonly IPAddress SsdpMulticastIpv4 = IPAddress.Parse("239.255.255.250");

    public const string SunshineServiceType = "urn:schemas-upnp-org:device:MediaServer:1";
    public const string RootDeviceServiceType = "upnp:rootdevice";

    /// <summary>
    /// Formats an SSDP M-SEARCH multicast discovery request packet.
    /// </summary>
    public static int EncodeSearchRequest(
        Span<byte> destination,
        int targetPort = SunshineSsdpPort,
        string serviceType = SunshineServiceType,
        int mxSeconds = 2)
    {
        string request = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:{targetPort}\r\nMAN: \"ssdp:discover\"\r\nST: {serviceType}\r\nMX: {mxSeconds}\r\n\r\n"
        );

        int byteCount = Encoding.ASCII.GetByteCount(request);
        if (destination.Length < byteCount)
        {
            throw new ArgumentException("Destination buffer too small for SSDP search request.", nameof(destination));
        }

        return Encoding.ASCII.GetBytes(request, destination);
    }

    /// <summary>
    /// Parses an incoming SSDP HTTP/1.1 200 OK or NOTIFY packet over ReadOnlySpan.
    /// </summary>
    public static bool TryParseResponse(ReadOnlySpan<byte> packet, out SsdpDeviceRecord? record)
    {
        record = null;
        if (packet.Length < 16)
        {
            return false;
        }

        string raw = Encoding.ASCII.GetString(packet);
        if (!raw.StartsWith("HTTP/1.1 200 OK", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("NOTIFY * HTTP/1.1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string location = string.Empty;
        string st = string.Empty;
        string usn = string.Empty;
        string server = string.Empty;
        int maxAge = 1800;

        string[] lines = raw.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            string headerName = line[..colonIdx].Trim();
            string headerVal = line[(colonIdx + 1)..].Trim();

            if (headerName.Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                location = headerVal;
            }
            else if (headerName.Equals("ST", StringComparison.OrdinalIgnoreCase) || headerName.Equals("NT", StringComparison.OrdinalIgnoreCase))
            {
                st = headerVal;
            }
            else if (headerName.Equals("USN", StringComparison.OrdinalIgnoreCase))
            {
                usn = headerVal;
            }
            else if (headerName.Equals("SERVER", StringComparison.OrdinalIgnoreCase))
            {
                server = headerVal;
            }
            else if (headerName.Equals("CACHE-CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                int maxAgeIdx = headerVal.IndexOf("max-age=", StringComparison.OrdinalIgnoreCase);
                if (maxAgeIdx >= 0)
                {
                    string sub = headerVal[(maxAgeIdx + 8)..].Trim();
                    int end = sub.IndexOfAny([',', ';', ' ']);
                    string numStr = end > 0 ? sub[..end] : sub;
                    if (int.TryParse(numStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int ma))
                    {
                        maxAge = ma;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(location) && string.IsNullOrEmpty(st))
        {
            return false;
        }

        IPAddress? hostIp = null;
        int hostPort = 47989;

        if (!string.IsNullOrEmpty(location) && Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            if (IPAddress.TryParse(uri.Host, out var parsedIp))
            {
                hostIp = parsedIp;
            }
            hostPort = uri.Port > 0 ? uri.Port : 47989;
        }

        record = new SsdpDeviceRecord(
            Location: location,
            ServiceType: st,
            UniqueServiceName: usn,
            ServerHeader: server,
            MaxAgeSeconds: maxAge,
            HostIp: hostIp,
            HostPort: hostPort
        );

        return true;
    }
}
