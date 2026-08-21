namespace Moonshine.Core.Runtime;

/// <summary>
/// Selectable capabilities and operational roles for the single Moonshine Windows executable.
/// </summary>
[Flags]
public enum ApplicationRole
{
    /// <summary>
    /// No role is active; all streaming services remain stopped and consume zero resources.
    /// </summary>
    None = 0,

    /// <summary>
    /// The application acts as a streaming host: desktop capture, audio capture, hardware encoding, and streaming transmission.
    /// </summary>
    Host = 1,

    /// <summary>
    /// The application acts as a streaming client: packet reception, jitter buffering, hardware decoding, presentation, audio playback, and input polling.
    /// </summary>
    Client = 2,

    /// <summary>
    /// The application runs both host and client capabilities concurrently within a single process.
    /// </summary>
    HostAndClient = Host | Client
}

/// <summary>
/// Extension methods for querying and formatting <see cref="ApplicationRole"/>.
/// </summary>
public static class ApplicationRoleExtensions
{
    /// <summary>
    /// Determines whether the host role is enabled.
    /// </summary>
    public static bool HasHost(this ApplicationRole role) => (role & ApplicationRole.Host) != 0;

    /// <summary>
    /// Determines whether the client role is enabled.
    /// </summary>
    public static bool HasClient(this ApplicationRole role) => (role & ApplicationRole.Client) != 0;

    /// <summary>
    /// Formats the application role into a canonical string descriptor.
    /// </summary>
    public static string FormatRole(this ApplicationRole role) => role switch
    {
        ApplicationRole.None => "none",
        ApplicationRole.Host => "host",
        ApplicationRole.Client => "client",
        ApplicationRole.HostAndClient => "host-client",
        _ => "unknown"
    };
}
