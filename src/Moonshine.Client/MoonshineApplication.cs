using Moonshine.Core.Runtime;
using Moonshine.Host;

namespace Moonshine.App;

public readonly record struct ApplicationStartResult(bool IsStarted, string Message);

/// <summary>
/// Single Windows application composition root. Coordinates the unified executable's runtime lifecycle,
/// delegating role activations to the <see cref="IRuntimeCoordinator"/> while preserving fail-closed baseline.
/// </summary>
public sealed class MoonshineApplication : IDisposable
{
    private readonly IRuntimeCoordinator _coordinator;

    public MoonshineApplication()
        : this(new MoonshineRuntimeCoordinator(
            hostServiceFactory: () => new MoonshineHostCoordinator(),
            clientServiceFactory: () => new MoonshineClientCoordinator()))
    {
    }

    public MoonshineApplication(IRuntimeCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public IRuntimeCoordinator Coordinator => _coordinator;

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
        RoleTransitionResult result = _coordinator.StartAsync(role).AsTask().GetAwaiter().GetResult();
        return new ApplicationStartResult(
            IsStarted: result.State == RuntimeState.Running,
            result.Message ?? "Streaming is unsupported: Moonshine-native session control, transport, decode, encode, and presentation paths are not implemented. No compatibility protocol or fabricated hardware path was started.");
    }

    public void Dispose() => _coordinator.Dispose();
}
