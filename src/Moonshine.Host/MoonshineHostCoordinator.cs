namespace Moonshine.Host;

public enum HostState
{
    Disabled,
    Starting,
    Running,
    Stopping,
    Unsupported
}

/// <summary>
/// Coordinates the Host role state. It does not claim an active host until a Moonshine-native
/// control and media transport is implemented and bound to a listener.
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
            if (_state == HostState.Running || _state == HostState.Starting || _state == HostState.Unsupported) return;
            _state = HostState.Unsupported;
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
