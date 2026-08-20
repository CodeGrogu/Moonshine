using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Moonshine.Protocol.Discovery.Mdns;
using Moonshine.Protocol.Discovery.Ssdp;
using Moonshine.Protocol.Discovery.Xml;

namespace Moonshine.Core.Discovery;

public sealed record DiscoveredHost(
    string HostId,
    string Hostname,
    string IpAddress,
    int HttpPort,
    int HttpsPort,
    string MacAddress,
    string AppVersion,
    string GpuModel,
    bool IsPaired,
    ServerCodecCapabilities CodecCapabilities,
    string CurrentGame,
    IReadOnlyList<DisplayMode> SupportedDisplayModes,
    DateTime LastSeenUtc,
    bool IsOnline
);

/// <summary>
/// Ultra-low-latency real-time LAN host discovery engine combining Multicast DNS (mDNS),
/// SSDP UPnP broadcasts, and continuous asynchronous HTTP/HTTPS ServerInfo probing.
/// </summary>
public sealed class LiveHostDiscoveryEngine : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, DiscoveredHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _cts = new();
    private Task? _discoveryLoopTask;
    private readonly TimeSpan _sweepInterval;
    private readonly TimeSpan _hostTimeout;

    public event Action<DiscoveredHost>? HostDiscovered;
    public event Action<DiscoveredHost>? HostUpdated;
    public event Action<DiscoveredHost>? HostOffline;

    public IReadOnlyCollection<DiscoveredHost> ActiveHosts => _hosts.Values.ToList().AsReadOnly();

    public LiveHostDiscoveryEngine(
        HttpClient? httpClient = null,
        TimeSpan? sweepInterval = null,
        TimeSpan? hostTimeout = null)
    {
        _sweepInterval = sweepInterval ?? TimeSpan.FromSeconds(3);
        _hostTimeout = hostTimeout ?? TimeSpan.FromSeconds(10);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };

        _httpClient = httpClient ?? new HttpClient(handler);
    }

    /// <summary>
    /// Starts the background continuous mDNS and SSDP discovery listener and periodic emitter.
    /// </summary>
    public void Start()
    {
        if (_discoveryLoopTask != null) return;
        _discoveryLoopTask = Task.Run(DiscoveryLoopAsync);
    }

    private async Task DiscoveryLoopAsync()
    {
        var token = _cts.Token;
        using var mdnsSocket = CreateMdnsSocket();
        using var ssdpSocket = CreateSsdpSocket();

        // Launch concurrent packet receivers
        var mdnsRxTask = ReceiveMdnsPacketsAsync(mdnsSocket, token);
        var ssdpRxTask = ReceiveSsdpPacketsAsync(ssdpSocket, token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await BroadcastDiscoveryQueriesAsync(mdnsSocket, ssdpSocket, token).ConfigureAwait(false);
                PruneStaleHosts();
                await Task.Delay(_sweepInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue discovery loop on transient network errors
            }
        }

        await Task.WhenAll(mdnsRxTask, ssdpRxTask).ConfigureAwait(false);
    }

    private static Socket CreateMdnsSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, 0)); // Ephemeral sender port
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        }
        catch
        {
            // Ignore socket binding issues on restricted environments
        }
        return socket;
    }

    private static Socket CreateSsdpSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
        }
        catch
        {
            // Ignore socket binding issues
        }
        return socket;
    }

    private static async Task BroadcastDiscoveryQueriesAsync(Socket mdnsSocket, Socket ssdpSocket, CancellationToken token)
    {
        // 1. Broadcast mDNS Query for Sunshine and GameStream
        byte[] mdnsQueryBuffer = new byte[512];
        int mdnsLen = MdnsCodec.EncodeQuery(mdnsQueryBuffer, "_nvstream._tcp.local");
        var mdnsEp = new IPEndPoint(MdnsCodec.MdnsMulticastIpv4, MdnsCodec.DefaultMdnsPort);

        try
        {
            await mdnsSocket.SendToAsync(mdnsQueryBuffer.AsMemory(0, mdnsLen), SocketFlags.None, mdnsEp, token).ConfigureAwait(false);
        }
        catch
        {
            // Network interface send errors are handled silently
        }

        // 2. Broadcast SSDP M-SEARCH queries
        byte[] ssdpQueryBuffer = new byte[512];
        int ssdpLen48010 = SsdpCodec.EncodeSearchRequest(ssdpQueryBuffer, targetPort: 48010);
        var ssdpEp48010 = new IPEndPoint(SsdpCodec.SsdpMulticastIpv4, 48010);

        try
        {
            await ssdpSocket.SendToAsync(ssdpQueryBuffer.AsMemory(0, ssdpLen48010), SocketFlags.None, ssdpEp48010, token).ConfigureAwait(false);
        }
        catch
        {
        }

        int ssdpLen1900 = SsdpCodec.EncodeSearchRequest(ssdpQueryBuffer, targetPort: 1900);
        var ssdpEp1900 = new IPEndPoint(SsdpCodec.SsdpMulticastIpv4, 1900);

        try
        {
            await ssdpSocket.SendToAsync(ssdpQueryBuffer.AsMemory(0, ssdpLen1900), SocketFlags.None, ssdpEp1900, token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task ReceiveMdnsPacketsAsync(Socket socket, CancellationToken token)
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, token).ConfigureAwait(false);
                if (result.ReceivedBytes > 12)
                {
                    if (MdnsCodec.TryParseResponse(buffer.AsSpan(0, result.ReceivedBytes), out var serviceRecord) && serviceRecord?.IpAddress != null)
                    {
                        _ = ProbeHostAsync(serviceRecord.IpAddress.ToString(), serviceRecord.Port > 0 ? serviceRecord.Port : 47989, token);
                    }
                    else if (result.RemoteEndPoint is IPEndPoint remoteIp)
                    {
                        _ = ProbeHostAsync(remoteIp.Address.ToString(), 47989, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue receiving
            }
        }
    }

    private async Task ReceiveSsdpPacketsAsync(Socket socket, CancellationToken token)
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, token).ConfigureAwait(false);
                if (result.ReceivedBytes > 16)
                {
                    if (SsdpCodec.TryParseResponse(buffer.AsSpan(0, result.ReceivedBytes), out var ssdpRecord) && ssdpRecord?.HostIp != null)
                    {
                        _ = ProbeHostAsync(ssdpRecord.HostIp.ToString(), ssdpRecord.HostPort, token);
                    }
                    else if (result.RemoteEndPoint is IPEndPoint remoteIp)
                    {
                        _ = ProbeHostAsync(remoteIp.Address.ToString(), 47989, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue receiving
            }
        }
    }

    /// <summary>
    /// Explicitly probes a host by IP address and port to fetch ServerInfo metadata.
    /// </summary>
    public async Task<DiscoveredHost?> ProbeHostAsync(string ipAddress, int port = 47989, CancellationToken ct = default)
    {
        string? xmlResponse = null;

        // Try HTTP first
        try
        {
            string httpUrl = $"http://{ipAddress}:{port}/serverinfo";
            xmlResponse = await _httpClient.GetStringAsync(httpUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            // Fallback to HTTPS
            try
            {
                string httpsUrl = $"https://{ipAddress}:47984/serverinfo";
                xmlResponse = await _httpClient.GetStringAsync(httpsUrl, ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(xmlResponse)) return null;

        var serverInfo = ServerInfoCodec.Parse(xmlResponse, fallbackIp: ipAddress, fallbackPort: port);
        if (serverInfo == null) return null;

        string hostKey = !string.IsNullOrEmpty(serverInfo.UniqueId) ? serverInfo.UniqueId : $"{serverInfo.LocalIp}:{serverInfo.HttpPort}";

        var host = new DiscoveredHost(
            HostId: hostKey,
            Hostname: serverInfo.Hostname,
            IpAddress: serverInfo.LocalIp,
            HttpPort: serverInfo.HttpPort,
            HttpsPort: serverInfo.HttpsPort,
            MacAddress: serverInfo.MacAddress,
            AppVersion: serverInfo.AppVersion,
            GpuModel: serverInfo.GpuModel,
            IsPaired: serverInfo.IsPaired,
            CodecCapabilities: serverInfo.CodecCapabilities,
            CurrentGame: serverInfo.CurrentGame,
            SupportedDisplayModes: serverInfo.SupportedDisplayModes,
            LastSeenUtc: DateTime.UtcNow,
            IsOnline: true
        );

        bool isNew = !_hosts.ContainsKey(hostKey);
        _hosts[hostKey] = host;

        if (isNew)
        {
            HostDiscovered?.Invoke(host);
        }
        else
        {
            HostUpdated?.Invoke(host);
        }

        return host;
    }

    private void PruneStaleHosts()
    {
        var cutoff = DateTime.UtcNow - _hostTimeout;
        foreach (var kvp in _hosts)
        {
            if (kvp.Value.LastSeenUtc < cutoff && kvp.Value.IsOnline)
            {
                var offlineHost = kvp.Value with { IsOnline = false };
                _hosts[kvp.Key] = offlineHost;
                HostOffline?.Invoke(offlineHost);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_discoveryLoopTask != null)
        {
            try
            {
                await _discoveryLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
        _cts.Dispose();
        _httpClient.Dispose();
    }
}
