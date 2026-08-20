using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

namespace Moonshine.Core.Discovery;

public record HostServerInfo(
    string Hostname,
    string IpAddress,
    int Port,
    string MacAddress,
    string AppVersion,
    string GpuModel,
    bool IsPaired,
    int ServerCodecModeSupport
);

/// <summary>
/// Service for discovering Sunshine and GameStream hosts on local networks.
/// </summary>
public sealed class MoonshineDiscoveryService : IDisposable
{
    private readonly HttpClient _httpClient;

    public MoonshineDiscoveryService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(3)
        });
    }

    public async Task<HostServerInfo?> QueryServerInfoAsync(string hostIp, int port = 47989, CancellationToken ct = default)
    {
        try
        {
            string url = string.Create(CultureInfo.InvariantCulture, $"http://{hostIp}:{port}/serverinfo");
            string responseXml = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            return ParseServerInfoXml(hostIp, port, responseXml);
        }
        catch
        {
            // Fallback to HTTPS port (47984) if HTTP is disabled
            try
            {
                string httpsUrl = string.Create(CultureInfo.InvariantCulture, $"https://{hostIp}:47984/serverinfo");
                string responseXml = await _httpClient.GetStringAsync(httpsUrl, ct).ConfigureAwait(false);
                return ParseServerInfoXml(hostIp, 47984, responseXml);
            }
            catch
            {
                return null;
            }
        }
    }

    public static HostServerInfo? ParseServerInfoXml(string hostIp, int port, string xmlContent)
    {
        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root == null) return null;

            string hostname = root.Element("hostname")?.Value ?? hostIp;
            string mac = root.Element("mac")?.Value ?? "00:00:00:00:00:00";
            string appversion = root.Element("appversion")?.Value ?? "1.0";
            string gpu = root.Element("gputype")?.Value ?? "NVIDIA / AMD / Intel";
            bool isPaired = root.Element("PairStatus")?.Value == "1";
            int codecSupport = int.TryParse(root.Element("ServerCodecModeSupport")?.Value, CultureInfo.InvariantCulture, out int c) ? c : 0;

            return new HostServerInfo(hostname, hostIp, port, mac, appversion, gpu, isPaired, codecSupport);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
