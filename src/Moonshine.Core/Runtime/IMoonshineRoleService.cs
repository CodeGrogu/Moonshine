namespace Moonshine.Core.Runtime;

/// <summary>
/// Defines the lifecycle and status reporting contract for an individual Moonshine capability service.
/// </summary>
public interface IMoonshineRoleService : IDisposable
{
    /// <summary>
    /// Gets the application role implemented by this service.
    /// </summary>
    ApplicationRole Role { get; }

    /// <summary>
    /// Gets the current operational lifecycle state of the service.
    /// </summary>
    RuntimeState State { get; }

    /// <summary>
    /// Gets a value indicating whether the service is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets a value indicating whether this service holds any active native or managed resources (sockets, workers, buffers).
    /// </summary>
    bool HasActiveResources { get; }

    /// <summary>
    /// Asynchronously initialises and starts the role service.
    /// </summary>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously terminates and releases all active resources of the role service.
    /// </summary>
    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously restarts the role service.
    /// </summary>
    ValueTask RestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Specific service contract for the host streaming capability.
/// </summary>
public interface IMoonshineHostService : IMoonshineRoleService
{
    /// <summary>
    /// Obtains an immutable telemetry snapshot of the host capability.
    /// </summary>
    HostStatus GetStatus();
}

/// <summary>
/// Specific service contract for the client streaming capability.
/// </summary>
public interface IMoonshineClientService : IMoonshineRoleService
{
    /// <summary>
    /// Obtains an immutable telemetry snapshot of the client capability.
    /// </summary>
    ClientStatus GetStatus();
}
