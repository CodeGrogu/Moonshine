using Moonshine.Host;

namespace Moonshine.App;

[Flags]
public enum ApplicationRole
{
    None = 0,
    Host = 1,
    Client = 2,
    HostAndClient = Host | Client
}

public readonly record struct ApplicationStartResult(bool IsStarted, string Message);

/// <summary>
/// Single Windows application composition root. Role selection is independent, but streaming
/// activation remains unavailable until Moonshine-native control and media transports exist.
/// </summary>
public sealed class MoonshineApplication : IDisposable
{
    private readonly MoonshineHostCoordinator _host = new();

    public static bool TryParseRole(string[] arguments, out ApplicationRole role)
    {
        role = ApplicationRole.None;
        if (arguments.Length != 2 || !string.Equals(arguments[0], "--role", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        role = arguments[1].ToLowerInvariant() switch
        {
            "host" => ApplicationRole.Host,
            "client" => ApplicationRole.Client,
            "host-client" => ApplicationRole.HostAndClient,
            _ => ApplicationRole.None
        };
        return role != ApplicationRole.None;
    }

    public ApplicationStartResult Start(ApplicationRole role)
    {
        if ((role & ApplicationRole.Host) != 0)
        {
            _host.Enable();
        }

        return new ApplicationStartResult(
            IsStarted: false,
            "Streaming is unsupported: Moonshine-native session control, transport, decode, encode, and presentation paths are not implemented. No compatibility protocol or fabricated hardware path was started.");
    }

    public void Dispose() => _host.Dispose();
}
