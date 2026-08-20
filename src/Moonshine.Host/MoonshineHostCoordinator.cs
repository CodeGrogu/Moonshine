namespace Moonshine.Host;

public enum HostState
{
    Disabled,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// Modular Moonshine Host Coordinator.
/// Provides zero-overhead state management for enabling and disabling the host streaming server.
/// </summary>
public sealed class MoonshineHostCoordinator : IDisposable
{
    private HostState _state = HostState.Disabled;
    private readonly Lock _lock = new();

    public HostState State
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    public bool IsRunning => State == HostState.Running;

    public void Enable()
    {
        lock (_lock)
        {
            if (_state == HostState.Running || _state == HostState.Starting) return;
            _state = HostState.Running;
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (_state == HostState.Disabled || _state == HostState.Stopping) return;
            _state = HostState.Disabled;
        }
    }

    public void Dispose()
    {
        Disable();
    }
}
