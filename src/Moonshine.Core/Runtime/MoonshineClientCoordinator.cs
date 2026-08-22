using Moonshine.Core.Input;

namespace Moonshine.Core.Runtime;

/// <summary>
/// Coordinates the Client capability lifecycle, managing packet reception, decode pipelines,
/// presentation swapchains, audio rendering, and input polling while guaranteeing deterministic resource disposal.
/// </summary>
public sealed class MoonshineClientCoordinator : IMoonshineClientService
{
    private readonly Lock _lock = new();
    private RuntimeState _state = RuntimeState.Stopped;
    private bool _isConnected;
    private int _activeWorkers;
    private int _activeBuffers;
    private string? _lastError;
    private CancellationTokenSource? _workerCts;
    private MoonshineClientInputPipeline? _inputPipeline;
    private bool _disposed;

    public ApplicationRole Role => ApplicationRole.Client;
    public MoonshineClientInputPipeline? InputPipeline
    {
        get
        {
            lock (_lock) return _inputPipeline;
        }
    }

    public RuntimeState State
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    public bool IsRunning => State == RuntimeState.Running;

    public bool HasActiveResources
    {
        get
        {
            lock (_lock)
            {
                return _activeWorkers > 0 || _activeBuffers > 0 || _isConnected;
            }
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state is RuntimeState.Running or RuntimeState.Starting)
            {
                return ValueTask.CompletedTask;
            }

            _state = RuntimeState.Starting;
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _workerCts = new CancellationTokenSource();
                // Fail-closed baseline: reports Unsupported until native session control is implemented
                _state = RuntimeState.Unsupported;
                _lastError = "Client media transport and session control are not yet connected.";
            }

            return ValueTask.CompletedTask;
        }
        // ALLOWED_EXCEPTION: Re-throws after capturing error state and cleaning up resources.
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = RuntimeState.Faulted;
                _lastError = ex.Message;
                CleanupResourcesNoLock();
            }
            throw;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_state == RuntimeState.Stopped)
            {
                return ValueTask.CompletedTask;
            }

            _state = RuntimeState.Stopping;
            CleanupResourcesNoLock();
            _state = RuntimeState.Stopped;
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public ClientStatus GetStatus()
    {
        lock (_lock)
        {
            return new ClientStatus(
                State: _state,
                IsRunning: _state == RuntimeState.Running,
                IsConnected: _isConnected,
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

        if (_inputPipeline is not null)
        {
            _inputPipeline.Dispose();
            _inputPipeline = null;
        }

        _activeWorkers = 0;
        _activeBuffers = 0;
        _isConnected = false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _state = RuntimeState.Stopping;
            CleanupResourcesNoLock();
            _state = RuntimeState.Stopped;
        }
    }
}
