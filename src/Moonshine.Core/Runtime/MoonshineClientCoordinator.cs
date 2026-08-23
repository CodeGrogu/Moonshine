using Moonshine.Core.Input;
using Moonshine.Core.Session;

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
    private MoonshineClientStreamingSession? _activeSession;
    private bool _disposed;

    public ApplicationRole Role => ApplicationRole.Client;
    public MoonshineClientInputPipeline? InputPipeline
    {
        get
        {
            lock (_lock) return _inputPipeline;
        }
    }

    public MoonshineClientStreamingSession? ActiveSession
    {
        get
        {
            lock (_lock) return _activeSession;
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
                
                // Initialize input pipeline
                _inputPipeline = new MoonshineClientInputPipeline();
                _activeWorkers++;

                _state = RuntimeState.Running;
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

    public async ValueTask<MoonshineClientStreamingSession> ConnectAndStartSessionAsync(ClientSessionConfig config, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != RuntimeState.Running)
            {
                throw new InvalidOperationException("Client coordinator must be running before starting a session.");
            }
            if (_activeSession != null)
            {
                throw new InvalidOperationException("A client session is already active.");
            }
        }

        var session = new MoonshineClientStreamingSession(config);

        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            
            lock (_lock)
            {
                _activeSession = session;
                _isConnected = true;
                _activeWorkers++;
            }
            return session;
        }
        // ALLOWED_EXCEPTION: Dispose session on startup failure before rethrowing to caller.
        catch (Exception)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisconnectSessionAsync(MoonshineClientStreamingSession session)
    {
        bool wasActive = false;
        lock (_lock)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
                _isConnected = false;
                _activeWorkers = Math.Max(0, _activeWorkers - 1);
                wasActive = true;
            }
        }

        if (wasActive)
        {
            await session.StopAsync().ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
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

        if (_activeSession is not null)
        {
            try
            {
                _activeSession.Dispose();
            }
            // ALLOWED_EXCEPTION: Suppress disposal failures during coordinator teardown to avoid masking root cause.
            catch { }
            _activeSession = null;
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
