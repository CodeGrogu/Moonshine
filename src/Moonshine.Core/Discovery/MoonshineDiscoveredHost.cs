using System.Net;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Core.Discovery;

/// <summary>
/// Immutable representation of an active Moonshine host discovered on the local network.
/// </summary>
public sealed record MoonshineDiscoveredHost(
    MoonshineUuid128 HostUuid,
    string Hostname,
    IPAddress EndpointAddress,
    int ControlTcpPort,
    int DiscoveryUdpPort,
    int VideoUdpPort,
    int AudioUdpPort,
    int ControlFeedbackUdpPort,
    int MicUdpPort,
    string GpuName,
    MoonshineCapabilities Capabilities,
    uint MaxBitrateKbps,
    bool SupportsHdr10,
    bool SupportsVirtualAudio,
    bool SupportsMicBackchannel,
    bool IsPaired,
    DateTime LastSeenUtc,
    bool IsOnline = true
);
