using Moonshine.Core.Runtime;

namespace Moonshine.Host;

public enum HostState
{
    Disabled = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Faulted = 4,
    Unsupported = 5
}

/// <summary>
/// Coordinates the Host role state and manages host resources (listeners, workers, capture pipelines,
/// encoder engines, and sessions) while guaranteeing deterministic resource disposal and zero leaks when disabled.
/// </summary>
public sealed class MoonshineHostCoordinator : IMoonshineHostService
{
    private HostState _state = HostState.Disabled;
    private int _activeSessions;
    private int _activeListeners;
    private int _activeWorkers;
    private int _activeBuffers;
    private string? _lastError;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _workerCts;
    private bool _disposed;

    public ApplicationRole Role => ApplicationRole.Host;

    public HostState State
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    RuntimeState IMoonshineRoleService.State => State switch
    {
        HostState.Disabled => RuntimeState.Stopped,
        HostState.Starting => RuntimeState.Starting,
        HostState.Running => RuntimeState.Running,
        HostState.Stopping => RuntimeState.Stopping,
        HostState.Faulted => RuntimeState.Faulted,
        HostState.Unsupported => RuntimeState.Unsupported,
        _ => RuntimeState.Stopped
    };

    public bool IsRunning => State == HostState.Running;

    public bool HasActiveResources
    {
        get
        {
            lock (_lock)
            {
                return _activeSessions > 0 || _activeListeners > 0 || _activeWorkers > 0 || _activeBuffers > 0;
            }
        }
    }

    public void Enable()
    {
        lock (_lock)
        {
            if (_state is HostState.Running or HostState.Starting or HostState.Unsupported) return;
            _state = HostState.Unsupported;
            _lastError = "Host media transport and session control are not yet implemented.";
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (_state is HostState.Disabled or HostState.Stopping) return;
            _state = HostState.Stopping;
            CleanupResourcesNoLock();
            _state = HostState.Disabled;
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state is HostState.Running or HostState.Starting)
            {
                return ValueTask.CompletedTask;
            }

            _state = HostState.Starting;
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _workerCts = new CancellationTokenSource();
                // Fail-closed baseline: reports Unsupported until native session control is implemented
                _state = HostState.Unsupported;
                _lastError = "Host media transport and session control are not yet implemented.";
            }

            return ValueTask.CompletedTask;
        }
        // ALLOWED_EXCEPTION: Re-throws after capturing error state and cleaning up resources.
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = HostState.Faulted;
                _lastError = ex.Message;
                CleanupResourcesNoLock();
            }
            throw;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        Disable();
        return ValueTask.CompletedTask;
    }

    public async ValueTask RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public HostStatus GetStatus()
    {
        lock (_lock)
        {
            RuntimeState runtimeState = ((IMoonshineRoleService)this).State;
            return new HostStatus(
                State: runtimeState,
                IsRunning: _state == HostState.Running,
                ActiveSessionCount: _activeSessions,
                ActiveListenerCount: _activeListeners,
                ActiveWorkerCount: _activeWorkers,
                ActiveBufferCount: _activeBuffers,
                LastError: _lastError);
        }
    }

    private void CleanupResourcesNoLock()
    {
        if (_workerCts is not null)
        {
            _workerCts.Cancel();
            _workerCts.Dispose();
            _workerCts = null;
        }

        _activeSessions = 0;
        _activeListeners = 0;
        _activeWorkers = 0;
        _activeBuffers = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            Disable();
        }
    }
}
