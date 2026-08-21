using System.Net;

namespace Moonshine.Core.Network;

/// <summary>
/// Declares the explicit network profile mode for Moonshine Host network endpoints.
/// </summary>
public enum HostNetworkProfile
{
    /// <summary>
    /// Ephemeral dynamic ports (port 0) for automated testing, in-memory instances, and isolation from live system services.
    /// </summary>
    EphemeralTesting = 0,

    /// <summary>
    /// Standard production host ports (TCP 48010, UDP 48010, UDP 47998, UDP 47999, UDP 48000, UDP 48002).
    /// </summary>
    Production = 1,

    /// <summary>
    /// Explicit user-configured custom ports and bind address.
    /// </summary>
    Custom = 2
}

/// <summary>
/// Defines explicit endpoint and port configurations for Moonshine Host services.
/// All ports are strictly bound only when the Host role is activated.
/// Guarantees clear architectural separation between ephemeral testing profiles and production network exposure.
/// </summary>
public record HostEndpointConfig
{
    public const int DefaultControlTcpPort = 48010;
    public const int DefaultDiscoveryUdpPort = 48010;
    public const int DefaultVideoUdpPort = 47998;
    public const int DefaultControlFeedbackUdpPort = 47999;
    public const int DefaultAudioUdpPort = 48000;
    public const int DefaultMicUdpPort = 48002;

    public HostNetworkProfile Profile { get; init; } = HostNetworkProfile.EphemeralTesting;
    public IPAddress BindAddress { get; init; } = IPAddress.Any;
    public int ControlTcpPort { get; init; }
    public int DiscoveryUdpPort { get; init; }
    public int VideoUdpPort { get; init; }
    public int ControlFeedbackUdpPort { get; init; }
    public int AudioUdpPort { get; init; }
    public int MicUdpPort { get; init; }

    /// <summary>
    /// Gets a value indicating whether this endpoint configuration uses ephemeral ports.
    /// </summary>
    public bool IsEphemeral => Profile == HostNetworkProfile.EphemeralTesting ||
                               (ControlTcpPort == 0 && DiscoveryUdpPort == 0 && VideoUdpPort == 0 &&
                                ControlFeedbackUdpPort == 0 && AudioUdpPort == 0 && MicUdpPort == 0);

    /// <summary>
    /// Creates an ephemeral configuration with dynamically assigned free ports for isolated testing.
    /// </summary>
    public static HostEndpointConfig Ephemeral => new()
    {
        Profile = HostNetworkProfile.EphemeralTesting,
        BindAddress = IPAddress.Any
    };

    /// <summary>
    /// Alias for <see cref="Ephemeral"/> for test isolation.
    /// </summary>
    public static HostEndpointConfig Default => Ephemeral;

    /// <summary>
    /// Creates the standard production configuration with explicit fixed service port numbers.
    /// </summary>
    public static HostEndpointConfig ProductionDefault => new()
    {
        Profile = HostNetworkProfile.Production,
        BindAddress = IPAddress.Any,
        ControlTcpPort = DefaultControlTcpPort,
        DiscoveryUdpPort = DefaultDiscoveryUdpPort,
        VideoUdpPort = DefaultVideoUdpPort,
        ControlFeedbackUdpPort = DefaultControlFeedbackUdpPort,
        AudioUdpPort = DefaultAudioUdpPort,
        MicUdpPort = DefaultMicUdpPort
    };

    /// <summary>
    /// Creates an explicit custom endpoint configuration.
    /// </summary>
    public static HostEndpointConfig Custom(
        IPAddress bindAddress,
        int controlTcpPort,
        int discoveryUdpPort,
        int videoUdpPort,
        int controlFeedbackUdpPort,
        int audioUdpPort,
        int micUdpPort) => new()
    {
        Profile = HostNetworkProfile.Custom,
        BindAddress = bindAddress ?? IPAddress.Any,
        ControlTcpPort = controlTcpPort,
        DiscoveryUdpPort = discoveryUdpPort,
        VideoUdpPort = videoUdpPort,
        ControlFeedbackUdpPort = controlFeedbackUdpPort,
        AudioUdpPort = audioUdpPort,
        MicUdpPort = micUdpPort
    };
}
