namespace Moonshine.Core.Runtime;

/// <summary>
/// Orchestrates the single Moonshine executable's runtime lifecycle, role transitions, fault recovery,
/// and resource isolation between Host, Client, and HostAndClient capabilities.
/// </summary>
public interface IRuntimeCoordinator : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the currently configured active role.
    /// </summary>
    ApplicationRole ActiveRole { get; }

    /// <summary>
    /// Gets the overall lifecycle state of the runtime.
    /// </summary>
    RuntimeState State { get; }

    /// <summary>
    /// Gets a value indicating whether any role is actively running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Asynchronously starts the specified application role or combination of roles.
    /// If starting any enabled role fails, all partially initialised roles are rolled back to zero resources.
    /// </summary>
    ValueTask<RoleTransitionResult> StartAsync(ApplicationRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously stops all active roles, terminating sessions, closing listeners, and releasing all resources.
    /// </summary>
    ValueTask<RoleTransitionResult> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously restarts the currently active role services.
    /// </summary>
    ValueTask<RoleTransitionResult> RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously transitions the runtime from its current role to a new target role.
    /// Disabled roles are safely stopped and freed before new roles are activated.
    /// If the transition fails, the runtime safely rolls back without leaking resources.
    /// </summary>
    ValueTask<RoleTransitionResult> TransitionToRoleAsync(ApplicationRole targetRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtains an immutable telemetry snapshot of the overall runtime, including Host and Client sub-states.
    /// </summary>
    RuntimeStatus GetStatus();

    /// <summary>
    /// Dispatched when the overall runtime state or role transitions.
    /// </summary>
    event EventHandler<RuntimeStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Dispatched when a runtime fault occurs in an active subsystem.
    /// </summary>
    event EventHandler<RuntimeFaultEventArgs>? Faulted;
}
