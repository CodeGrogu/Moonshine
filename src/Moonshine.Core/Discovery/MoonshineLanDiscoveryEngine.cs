using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Moonshine.Core.Network;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Discovery;

namespace Moonshine.Core.Discovery;

/// <summary>
/// High-performance client-side LAN discovery engine for detecting Moonshine hosts via UDP multicast,
/// broadcast announcements, and active probing. Operates with zero host listening socket exposure.
/// </summary>
public sealed class MoonshineLanDiscoveryEngine : IAsyncDisposable
{
    public static readonly IPAddress MulticastIPv4 = IPAddress.Parse("239.255.48.10");
    public const int DefaultDiscoveryPort = HostEndpointConfig.DefaultDiscoveryUdpPort;

    private readonly ConcurrentDictionary<MoonshineUuid128, MoonshineDiscoveredHost> _hosts = new();
    private readonly int _discoveryPort;
    private readonly TimeSpan _sweepInterval;
    private readonly TimeSpan _hostTimeout;
    private readonly MoonshineUuid128 _clientUuid;
    private readonly CancellationTokenSource _cts = new();

    private Socket? _rxSocket;
    private Task? _rxTask;
    private Task? _sweepTask;
    private bool _disposed;
    private readonly Lock _lock = new();
    private ulong _probesSent;
    private ulong _announcementsReceived;

    public bool IsMulticastActive { get; private set; }
    public string? LastError { get; private set; }
    public ulong TotalProbesSent => _probesSent;
    public ulong TotalAnnouncementsReceived => _announcementsReceived;

    public event Action<MoonshineDiscoveredHost>? HostDiscovered;
    public event Action<MoonshineDiscoveredHost>? HostUpdated;
    public event Action<MoonshineDiscoveredHost>? HostLost;

    public IReadOnlyCollection<MoonshineDiscoveredHost> ActiveHosts => _hosts.Values.ToList().AsReadOnly();

    public MoonshineLanDiscoveryEngine(
        int discoveryPort = DefaultDiscoveryPort,
        TimeSpan? sweepInterval = null,
        TimeSpan? hostTimeout = null,
        MoonshineUuid128? clientUuid = null)
    {
        _discoveryPort = discoveryPort;
        _sweepInterval = sweepInterval ?? TimeSpan.FromSeconds(2);
        _hostTimeout = hostTimeout ?? TimeSpan.FromSeconds(8);
        _clientUuid = clientUuid ?? new MoonshineUuid128(Guid.NewGuid());

        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }
        // ALLOWED_EXCEPTION: NetworkChange may fail in restricted sandboxes.
        catch (NetworkInformationException)
        {
        }
    }

    /// <summary>
    /// Starts the background discovery receiver and periodic sweep loop.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_rxTask != null) return;

            InitSocketsNoLock();
            _rxTask = Task.Run(ReceiveLoopAsync);
            _sweepTask = Task.Run(SweepLoopAsync);
        }
    }

    private void InitSocketsNoLock()
    {
        try
        {
            _rxSocket?.Dispose();
            _rxSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
                ExclusiveAddressUse = false
            };

            _rxSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _rxSocket.Bind(new IPEndPoint(IPAddress.Any, 0)); // Ephemeral client receiving port

            bool multicastJoined = false;
            try
            {
                _rxSocket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(MulticastIPv4, IPAddress.Any));
                multicastJoined = true;
            }
            // ALLOWED_EXCEPTION: Multicast group joining may fail on loopback-only or restricted interfaces.
            catch (SocketException)
            {
            }

            IsMulticastActive = multicastJoined;
            LastError = multicastJoined ? null : "Multicast group membership unavailable on active interfaces.";
        }
        // ALLOWED_EXCEPTION: Socket setup fallback when port or permissions fail.
        catch (SocketException ex)
        {
            _rxSocket?.Dispose();
            _rxSocket = null;
            IsMulticastActive = false;
            LastError = $"Client discovery receive socket creation failed ({ex.SocketErrorCode}): {ex.Message}";
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _rxTask == null) return;
            InitSocketsNoLock();
        }
    }

    /// <summary>
    /// Sends an active Moonshine discovery probe datagram via multicast, broadcast, or direct unicast.
    /// </summary>
    public async ValueTask SendProbeAsync(IPEndPoint? target = null, CancellationToken cancellationToken = default)
    {
        Socket? socket;
        lock (_lock)
        {
            if (_disposed) return;
            if (_rxSocket == null)
            {
                InitSocketsNoLock();
            }
            socket = _rxSocket;
        }

        if (socket == null) return;

        Span<byte> nonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(nonceBytes);
        ulong nonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(nonceBytes);

        byte[] packetBuffer = new byte[MoonshineDiscoveryCodec.ProbePacketSize];
        var payload = new MoonshineDiscoveryProbePayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 0,
            ClientUuid = _clientUuid,
            DesiredCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264 | MoonshineCapabilities.Hdr10,
            Reserved = 0,
            ProbeNonce = nonce
        };

        if (!MoonshineDiscoveryCodec.TryWriteProbe(payload, packetBuffer, out int bytesWritten))
        {
            return;
        }

        var destinations = new List<IPEndPoint>();
        if (target != null)
        {
            destinations.Add(target);
        }
        else
        {
            destinations.Add(new IPEndPoint(MulticastIPv4, _discoveryPort));
            destinations.Add(new IPEndPoint(IPAddress.Broadcast, _discoveryPort));
            destinations.Add(new IPEndPoint(IPAddress.Loopback, _discoveryPort));

            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != OperationalStatus.Up ||
                        iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var u in iface.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                        {
                            byte[] ipBytes = u.Address.GetAddressBytes();
                            byte[] maskBytes = u.IPv4Mask.GetAddressBytes();
                            byte[] broadcastBytes = new byte[4];
                            for (int i = 0; i < 4; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            destinations.Add(new IPEndPoint(new IPAddress(broadcastBytes), _discoveryPort));
                        }
                    }
                }
            }
            // ALLOWED_EXCEPTION: Defensive network interface enumeration for directed subnet broadcast calculation.
            catch (Exception)
            {
            }
        }

        foreach (var dest in destinations)
        {
            try
            {
                await socket.SendToAsync(packetBuffer.AsMemory(0, bytesWritten), SocketFlags.None, dest, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _probesSent);
            }
            // ALLOWED_EXCEPTION: Ignore transient network send failure on individual multicast or broadcast targets.
            catch (SocketException)
            {
            }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] buffer = new byte[2048];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket;
            lock (_lock)
            {
                if (_disposed) break;
                if (_rxSocket == null)
                {
                    InitSocketsNoLock();
                }
                socket = _rxSocket;
            }

            if (socket == null)
            {
                await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                continue;
            }

            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    buffer.AsMemory(),
                    SocketFlags.None,
                    remoteEp,
                    _cts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes >= MoonshineDiscoveryCodec.AnnouncementPacketSize)
                {
                    ProcessDatagram(buffer.AsSpan(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Continue receiving despite transient network errors or noise.
            catch (SocketException)
            {
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private unsafe void ProcessDatagram(ReadOnlySpan<byte> datagram, IPEndPoint remoteEp)
    {
        MoonshineErrorCode err = MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(
            datagram,
            out MoonshinePacketHeader header,
            out MoonshineDiscoveryAnnouncementPayload payload);

        if (err != MoonshineErrorCode.Success)
        {
            return;
        }

        Interlocked.Increment(ref _announcementsReceived);

        string hostname = MoonshineDiscoveryCodec.GetFixedUtf8String(payload.Hostname, 64);
        string gpuName = MoonshineDiscoveryCodec.GetFixedUtf8String(payload.GpuName, 64);

        if (string.IsNullOrWhiteSpace(hostname))
        {
            hostname = remoteEp.Address.ToString();
        }

        var host = new MoonshineDiscoveredHost(
            HostUuid: payload.HostUuid,
            Hostname: hostname,
            EndpointAddress: remoteEp.Address,
            ControlTcpPort: (int)payload.ControlTcpPort,
            DiscoveryUdpPort: (int)payload.DiscoveryUdpPort,
            VideoUdpPort: (int)payload.VideoUdpPort,
            AudioUdpPort: (int)payload.AudioUdpPort,
            ControlFeedbackUdpPort: (int)payload.ControlFeedbackUdpPort,
            MicUdpPort: (int)payload.MicUdpPort,
            GpuName: gpuName,
            Capabilities: payload.SupportedCapabilities,
            MaxBitrateKbps: payload.MaxBitrateKbps,
            SupportsHdr10: payload.SupportsHdr10 == 1,
            SupportsVirtualAudio: payload.SupportsVirtualAudio == 1,
            SupportsMicBackchannel: payload.SupportsMicBackchannel == 1,
            IsPaired: payload.IsPaired == 1,
            LastSeenUtc: DateTime.UtcNow,
            IsOnline: true
        );

        bool isNew = false;
        _hosts.AddOrUpdate(
            payload.HostUuid,
            _ =>
            {
                isNew = true;
                return host;
            },
            (_, existing) => host
        );

        if (isNew)
        {
            HostDiscovered?.Invoke(host);
        }
        else
        {
            HostUpdated?.Invoke(host);
        }
    }

    private async Task SweepLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_sweepInterval, _cts.Token).ConfigureAwait(false);
                PruneStaleHosts();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue loop
            }
        }
    }

    /// <summary>
    /// Checks for and removes stale hosts that have not sent an advertisement within the timeout window.
    /// </summary>
    public void PruneStaleHosts()
    {
        DateTime cutoff = DateTime.UtcNow - _hostTimeout;
        foreach (var (uuid, host) in _hosts)
        {
            if (host.LastSeenUtc < cutoff)
            {
                if (_hosts.TryRemove(uuid, out var removed))
                {
                    HostLost?.Invoke(removed with { IsOnline = false });
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        }
        // ALLOWED_EXCEPTION: NetworkChange unhook may fail in restricted environments.
        catch (NetworkInformationException)
        {
        }

        _cts.Cancel();
        _rxSocket?.Dispose();

        if (_rxTask != null)
        {
            try
            {
                await _rxTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Task cancellation during graceful shutdown.
            catch (OperationCanceledException)
            {
            }
        }

        if (_sweepTask != null)
        {
            try
            {
                await _sweepTask.ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Task cancellation during graceful shutdown.
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _hosts.Clear();
    }
}
