using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Moonshine.Core.Network;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Discovery;

namespace Moonshine.Core.Discovery;

/// <summary>
/// Operational health status of the host discovery advertiser.
/// </summary>
public enum DiscoveryAdvertiserHealth
{
    /// <summary>Advertiser has not started or socket is unbound.</summary>
    Uninitialised = 0,

    /// <summary>Advertiser bound to port and joined multicast group successfully.</summary>
    Active = 1,

    /// <summary>Advertiser bound to port, but multicast failed; falling back to broadcast/unicast.</summary>
    Degraded = 2,

    /// <summary>Advertiser failed to bind port (e.g. port conflict or permission denied).</summary>
    Faulted = 3
}

/// <summary>
/// Host-side discovery advertiser that emits periodic Moonshine UDP LAN announcements and responds
/// immediately to incoming client DiscoveryProbe requests. Runs only while Host mode is active.
/// </summary>
public sealed class MoonshineHostDiscoveryAdvertiser : IDisposable
{
    private readonly HostEndpointConfig _endpointConfig;
    private readonly MoonshineUuid128 _hostUuid;
    private readonly string _hostname;
    private readonly string _gpuName;
    private readonly MoonshineCapabilities _capabilities;
    private readonly uint _maxBitrateKbps;
    private readonly bool _supportsHdr10;
    private readonly bool _supportsVirtualAudio;
    private readonly bool _supportsMicBackchannel;
    private readonly bool _isPaired;
    private readonly TimeSpan _advertisementInterval;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private Socket? _discoverySocket;
    private Task? _announceTask;
    private Task? _probeListenerTask;
    private bool _disposed;
    private ulong _advertisementSeq;
    private ulong _announcementsEmitted;
    private ulong _probesResponded;

    public DiscoveryAdvertiserHealth Health { get; private set; } = DiscoveryAdvertiserHealth.Uninitialised;
    public string? LastError { get; private set; }
    public bool IsMulticastActive { get; private set; }
    public ulong TotalAnnouncementsEmitted => _announcementsEmitted;
    public ulong TotalProbesResponded => _probesResponded;
    public ulong TotalDiscoveryPacketsEmitted => _announcementsEmitted + _probesResponded;

    public MoonshineHostDiscoveryAdvertiser(
        HostEndpointConfig? endpointConfig = null,
        MoonshineUuid128? hostUuid = null,
        string? hostname = null,
        string? gpuName = null,
        MoonshineCapabilities? capabilities = null,
        uint maxBitrateKbps = 150000,
        bool supportsHdr10 = true,
        bool supportsVirtualAudio = true,
        bool supportsMicBackchannel = true,
        bool isPaired = false,
        TimeSpan? advertisementInterval = null)
    {
        _endpointConfig = endpointConfig ?? HostEndpointConfig.Default;
        _hostUuid = hostUuid ?? new MoonshineUuid128(Guid.NewGuid());
        _hostname = hostname ?? Environment.MachineName;
        _gpuName = gpuName ?? "Direct3D 11/12 GPU";
        _capabilities = capabilities ?? (MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264 | MoonshineCapabilities.Hdr10 | MoonshineCapabilities.ReedSolomonFec);
        _maxBitrateKbps = maxBitrateKbps;
        _supportsHdr10 = supportsHdr10;
        _supportsVirtualAudio = supportsVirtualAudio;
        _supportsMicBackchannel = supportsMicBackchannel;
        _isPaired = isPaired;
        _advertisementInterval = advertisementInterval ?? TimeSpan.FromSeconds(2.5);
    }

    /// <summary>
    /// Starts the background discovery announcement and probe listener tasks.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_announceTask != null) return;

            InitSocketNoLock();
            _announceTask = Task.Run(AnnounceLoopAsync);
            _probeListenerTask = Task.Run(ProbeListenerLoopAsync);
        }
    }

    private void InitSocketNoLock()
    {
        try
        {
            _discoverySocket?.Dispose();
            _discoverySocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
                ExclusiveAddressUse = false
            };

            _discoverySocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _discoverySocket.Bind(new IPEndPoint(_endpointConfig.BindAddress, _endpointConfig.DiscoveryUdpPort));

            bool multicastJoined = false;
            try
            {
                _discoverySocket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(MoonshineLanDiscoveryEngine.MulticastIPv4, IPAddress.Any));
                multicastJoined = true;
            }
            // ALLOWED_EXCEPTION: Multicast group joining may fail on restricted loopback adapters.
            catch (SocketException)
            {
            }

            IsMulticastActive = multicastJoined;
            Health = multicastJoined ? DiscoveryAdvertiserHealth.Active : DiscoveryAdvertiserHealth.Degraded;
            LastError = multicastJoined ? null : "Multicast group membership failed; operating in broadcast/unicast fallback mode.";
        }
        // ALLOWED_EXCEPTION: Fallback if socket creation encounters port conflicts or permissions.
        catch (SocketException ex)
        {
            _discoverySocket?.Dispose();
            _discoverySocket = null;
            IsMulticastActive = false;
            Health = DiscoveryAdvertiserHealth.Faulted;
            LastError = $"Discovery UDP port {_endpointConfig.DiscoveryUdpPort} bind failed ({ex.SocketErrorCode}): {ex.Message}";
        }
    }

    private unsafe MoonshineDiscoveryAnnouncementPayload BuildPayload()
    {
        Span<byte> nonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(nonceBytes);
        ulong nonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(nonceBytes);

        var payload = new MoonshineDiscoveryAnnouncementPayload
        {
            HostVersionMajor = 1,
            HostVersionMinor = 0,
            HostUuid = _hostUuid,
            SupportedCapabilities = _capabilities,
            ControlTcpPort = (uint)_endpointConfig.ControlTcpPort,
            DiscoveryUdpPort = (uint)_endpointConfig.DiscoveryUdpPort,
            VideoUdpPort = (uint)_endpointConfig.VideoUdpPort,
            AudioUdpPort = (uint)_endpointConfig.AudioUdpPort,
            ControlFeedbackUdpPort = (uint)_endpointConfig.ControlFeedbackUdpPort,
            MicUdpPort = (uint)_endpointConfig.MicUdpPort,
            MaxBitrateKbps = _maxBitrateKbps,
            SupportsHdr10 = (byte)(_supportsHdr10 ? 1 : 0),
            SupportsVirtualAudio = (byte)(_supportsVirtualAudio ? 1 : 0),
            SupportsMicBackchannel = (byte)(_supportsMicBackchannel ? 1 : 0),
            IsPaired = (byte)(_isPaired ? 1 : 0),
            AdvertisementNonce = nonce
        };

        MoonshineDiscoveryCodec.SetFixedUtf8String(payload.Hostname, 64, _hostname);
        MoonshineDiscoveryCodec.SetFixedUtf8String(payload.GpuName, 64, _gpuName);

        return payload;
    }

    private async Task AnnounceLoopAsync()
    {
        byte[] buffer = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];
        var multicastTarget = new IPEndPoint(MoonshineLanDiscoveryEngine.MulticastIPv4, _endpointConfig.DiscoveryUdpPort);
        var broadcastTarget = new IPEndPoint(IPAddress.Broadcast, _endpointConfig.DiscoveryUdpPort);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket;
            lock (_lock)
            {
                if (_disposed) break;
                if (_discoverySocket == null)
                {
                    InitSocketNoLock();
                }
                socket = _discoverySocket;
            }

            if (socket != null)
            {
                uint seq = (uint)Interlocked.Increment(ref _advertisementSeq);
                var payload = BuildPayload();

                if (MoonshineDiscoveryCodec.TryWriteAnnouncement(payload, buffer, out int bytesWritten, seq))
                {
                    Interlocked.Increment(ref _announcementsEmitted);
                    try
                    {
                        await socket.SendToAsync(buffer.AsMemory(0, bytesWritten), SocketFlags.None, multicastTarget, _cts.Token).ConfigureAwait(false);
                    }
                    // ALLOWED_EXCEPTION: Transient network send failure on disconnected multicast interface.
                    catch (SocketException)
                    {
                    }

                    try
                    {
                        await socket.SendToAsync(buffer.AsMemory(0, bytesWritten), SocketFlags.None, broadcastTarget, _cts.Token).ConfigureAwait(false);
                    }
                    // ALLOWED_EXCEPTION: Transient broadcast failure on restricted subnet.
                    catch (SocketException)
                    {
                    }
                }
            }

            try
            {
                await Task.Delay(_advertisementInterval, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProbeListenerLoopAsync()
    {
        byte[] rxBuffer = new byte[2048];
        byte[] txBuffer = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!_cts.Token.IsCancellationRequested)
        {
            Socket? socket;
            lock (_lock)
            {
                if (_disposed) break;
                socket = _discoverySocket;
            }

            if (socket == null)
            {
                await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                continue;
            }

            try
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    rxBuffer.AsMemory(),
                    SocketFlags.None,
                    remoteEp,
                    _cts.Token).ConfigureAwait(false);

                if (result.ReceivedBytes >= MoonshineDiscoveryCodec.ProbePacketSize)
                {
                    MoonshineErrorCode err = MoonshineDiscoveryCodec.TryReadProbe(
                        rxBuffer.AsSpan(0, result.ReceivedBytes),
                        out MoonshinePacketHeader header,
                        out MoonshineDiscoveryProbePayload probe);

                    if (err == MoonshineErrorCode.Success)
                    {
                        uint seq = (uint)Interlocked.Increment(ref _advertisementSeq);
                        Interlocked.Increment(ref _probesResponded);
                        var responsePayload = BuildPayload();

                        if (MoonshineDiscoveryCodec.TryWriteResponse(responsePayload, txBuffer, out int written, seq, header.SessionId))
                        {
                            await socket.SendToAsync(txBuffer.AsMemory(0, written), SocketFlags.None, result.RemoteEndPoint, _cts.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // ALLOWED_EXCEPTION: Transient socket error or malformed packet in receive loop.
            catch (SocketException)
            {
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _cts.Cancel();
        _discoverySocket?.Dispose();

        try
        {
            _announceTask?.GetAwaiter().GetResult();
        }
        // ALLOWED_EXCEPTION: Ignore task cancellation during cleanup.
        catch (OperationCanceledException)
        {
        }

        try
        {
            _probeListenerTask?.GetAwaiter().GetResult();
        }
        // ALLOWED_EXCEPTION: Ignore task cancellation during cleanup.
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}
