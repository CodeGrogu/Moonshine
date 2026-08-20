using System.Globalization;
using System.Xml.Linq;

namespace Moonshine.Protocol.Discovery.Xml;

public sealed record DisplayMode(
    int Width,
    int Height,
    int RefreshRate
);

[Flags]
public enum ServerCodecCapabilities : uint
{
    None = 0,
    H264 = 1 << 0,
    Hevc = 1 << 1,
    Av1 = 1 << 2,
    HevcMain10 = 1 << 3,
    Av1Main10 = 1 << 4
}

/// <summary>
/// Rich metadata descriptor for a discovered GameStream/Sunshine host.
/// </summary>
public sealed record ServerInfoDetails(
    string Hostname,
    string ExternalIp,
    string LocalIp,
    int HttpPort,
    int HttpsPort,
    string MacAddress,
    string AppVersion,
    string GpuModel,
    bool IsPaired,
    ServerCodecCapabilities CodecCapabilities,
    int MaxLumaPixelsHevc,
    int MaxLumaPixelsH264,
    string CurrentGame,
    IReadOnlyList<DisplayMode> SupportedDisplayModes,
    string UniqueId
);

/// <summary>
/// High-performance XML parser for Sunshine/GameStream /serverinfo endpoints.
/// </summary>
public static class ServerInfoCodec
{
    public static ServerInfoDetails? Parse(string xmlContent, string fallbackIp, int fallbackPort = 47989)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return null;

            string hostname = root.Element("hostname")?.Value?.Trim() ?? fallbackIp;
            string externalIp = root.Element("ExternalIP")?.Value?.Trim() ?? fallbackIp;
            string localIp = root.Element("LocalIP")?.Value?.Trim() ?? fallbackIp;
            string mac = root.Element("mac")?.Value?.Trim() ?? "00:00:00:00:00:00";
            string appVersion = root.Element("appversion")?.Value?.Trim() ?? "1.0.0";
            string gpu = root.Element("gputype")?.Value?.Trim() ?? "Unknown GPU";
            bool isPaired = root.Element("PairStatus")?.Value?.Trim() == "1";
            string currentGame = root.Element("currentgame")?.Value?.Trim() ?? string.Empty;
            string uniqueId = root.Element("uniqueid")?.Value?.Trim() ?? string.Empty;

            int httpPort = fallbackPort;
            if (int.TryParse(root.Element("HttpPort")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hp) && hp > 0)
            {
                httpPort = hp;
            }

            int httpsPort = 47984;
            if (int.TryParse(root.Element("HttpsPort")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sp) && sp > 0)
            {
                httpsPort = sp;
            }

            ServerCodecCapabilities codecCaps = ServerCodecCapabilities.None;
            if (int.TryParse(root.Element("ServerCodecModeSupport")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cMode))
            {
                codecCaps = (ServerCodecCapabilities)cMode;
            }

            int maxLumaHevc = 0;
            if (int.TryParse(root.Element("MaxLumaPixelsHEVC")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mlh))
            {
                maxLumaHevc = mlh;
            }

            int maxLumaH264 = 0;
            if (int.TryParse(root.Element("MaxLumaPixelsH264")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ml4))
            {
                maxLumaH264 = ml4;
            }

            var displayModes = new List<DisplayMode>();
            var displayNodes = root.Element("SupportedDisplayModes")?.Elements("DisplayMode");
            if (displayNodes != null)
            {
                foreach (var node in displayNodes)
                {
                    if (int.TryParse(node.Element("Width")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
                        int.TryParse(node.Element("Height")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
                        int.TryParse(node.Element("RefreshRate")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                    {
                        displayModes.Add(new DisplayMode(w, h, r));
                    }
                }
            }

            return new ServerInfoDetails(
                Hostname: hostname,
                ExternalIp: externalIp,
                LocalIp: localIp,
                HttpPort: httpPort,
                HttpsPort: httpsPort,
                MacAddress: mac,
                AppVersion: appVersion,
                GpuModel: gpu,
                IsPaired: isPaired,
                CodecCapabilities: codecCaps,
                MaxLumaPixelsHevc: maxLumaHevc,
                MaxLumaPixelsH264: maxLumaH264,
                CurrentGame: currentGame,
                SupportedDisplayModes: displayModes.AsReadOnly(),
                UniqueId: uniqueId
            );
        }
        catch
        {
            return null;
        }
    }
}
