using System.Net;

namespace Moonshine.Core.Network;

/// <summary>
/// Defines the explicit endpoint and port configuration for Moonshine Host services.
/// All ports are strictly bound only when the Host role is activated.
/// Defaults to ephemeral ports for test isolation and safe in-memory execution.
/// </summary>
public record HostEndpointConfig
{
    public const int DefaultControlTcpPort = 48010;
    public const int DefaultDiscoveryUdpPort = 48010;
    public const int DefaultVideoUdpPort = 47998;
    public const int DefaultControlFeedbackUdpPort = 47999;
    public const int DefaultAudioUdpPort = 48000;
    public const int DefaultMicUdpPort = 48002;

    public IPAddress BindAddress { get; init; } = IPAddress.Any;
    public int ControlTcpPort { get; init; }
    public int DiscoveryUdpPort { get; init; }
    public int VideoUdpPort { get; init; }
    public int ControlFeedbackUdpPort { get; init; }
    public int AudioUdpPort { get; init; }
    public int MicUdpPort { get; init; }

    /// <summary>
    /// Gets the default configuration (ephemeral dynamic ports).
    /// </summary>
    public static HostEndpointConfig Default => Ephemeral;

    /// <summary>
    /// Gets an ephemeral configuration with dynamically assigned free ports.
    /// </summary>
    public static HostEndpointConfig Ephemeral => new();

    /// <summary>
    /// Gets the standard production configuration with default fixed port numbers.
    /// </summary>
    public static HostEndpointConfig ProductionDefault => new()
    {
        ControlTcpPort = DefaultControlTcpPort,
        DiscoveryUdpPort = DefaultDiscoveryUdpPort,
        VideoUdpPort = DefaultVideoUdpPort,
        ControlFeedbackUdpPort = DefaultControlFeedbackUdpPort,
        AudioUdpPort = DefaultAudioUdpPort,
        MicUdpPort = DefaultMicUdpPort
    };
}
