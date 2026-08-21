namespace Moonshine.Core.Runtime;

/// <summary>
/// Lifecycle and health states for the unified Moonshine runtime coordinator and role services.
/// </summary>
public enum RuntimeState
{
    /// <summary>
    /// The runtime or service is inactive and consumes zero streaming resources.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// The runtime or service is currently initialising enabled subsystems.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// The runtime or service is active and operating.
    /// </summary>
    Running = 2,

    /// <summary>
    /// The runtime or service is actively terminating workers and releasing resources.
    /// </summary>
    Stopping = 3,

    /// <summary>
    /// The runtime or service encountered a fault and terminated or rolled back.
    /// </summary>
    Faulted = 4,

    /// <summary>
    /// The runtime or service requested a backend capability that is unsupported or unavailable.
    /// </summary>
    Unsupported = 5
}

/// <summary>
/// Immutable snapshot metrics for the host capability.
/// </summary>
public readonly record struct HostStatus(
    RuntimeState State,
    bool IsRunning,
    int ActiveSessionCount,
    int ActiveListenerCount,
    int ActiveWorkerCount,
    int ActiveBufferCount,
    string? LastError = null);

/// <summary>
/// Immutable snapshot metrics for the client capability.
/// </summary>
public readonly record struct ClientStatus(
    RuntimeState State,
    bool IsRunning,
    bool IsConnected,
    int ActiveWorkerCount,
    int ActiveBufferCount,
    string? LastError = null);

/// <summary>
/// Comprehensive snapshot telemetry representing the current state of the unified Moonshine runtime.
/// </summary>
public readonly record struct RuntimeStatus(
    ApplicationRole ActiveRole,
    RuntimeState State,
    HostStatus Host,
    ClientStatus Client,
    DateTimeOffset Timestamp);

/// <summary>
/// Result metadata returned from a role transition or execution command.
/// </summary>
public readonly record struct RoleTransitionResult(
    bool Success,
    ApplicationRole PreviousRole,
    ApplicationRole TargetRole,
    RuntimeState State,
    string? Message = null,
    Exception? Error = null);

/// <summary>
/// Event arguments dispatched when the overall runtime state transitions.
/// </summary>
public sealed class RuntimeStateChangedEventArgs(
    ApplicationRole activeRole,
    RuntimeState previousState,
    RuntimeState newState,
    string? reason = null) : EventArgs
{
    public ApplicationRole ActiveRole { get; } = activeRole;
    public RuntimeState PreviousState { get; } = previousState;
    public RuntimeState NewState { get; } = newState;
    public string? Reason { get; } = reason;
}

/// <summary>
/// Event arguments dispatched when a runtime fault occurs.
/// </summary>
public sealed class RuntimeFaultEventArgs(
    ApplicationRole role,
    string component,
    Exception error) : EventArgs
{
    public ApplicationRole Role { get; } = role;
    public string Component { get; } = component;
    public Exception Error { get; } = error;
}
