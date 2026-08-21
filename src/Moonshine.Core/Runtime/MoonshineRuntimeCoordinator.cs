using Moonshine.Core.Network;

namespace Moonshine.Core.Runtime;

/// <summary>
/// Thread-safe, rollback-safe runtime coordinator for the single Moonshine Windows executable.
/// Coordinates the lifecycles of Host, Client, and HostAndClient capabilities, strictly isolating
/// disabled roles so they consume zero tasks, threads, buffers, sockets, or native contexts.
/// </summary>
public sealed class MoonshineRuntimeCoordinator : IRuntimeCoordinator
{
    private readonly Func<IMoonshineHostService>? _hostServiceFactory;
    private readonly Func<IMoonshineClientService>? _clientServiceFactory;
    private readonly HostEndpointConfig _hostEndpointConfig;
    private IMoonshineHostService? _hostService;
    private IMoonshineClientService? _clientService;

    private readonly Lock _lock = new();
    private ApplicationRole _activeRole = ApplicationRole.None;
    private RuntimeState _state = RuntimeState.Stopped;
    private bool _disposed;

    public event EventHandler<RuntimeStateChangedEventArgs>? StateChanged;
    public event EventHandler<RuntimeFaultEventArgs>? Faulted;

    /// <summary>
    /// Initialises a new instance of <see cref="MoonshineRuntimeCoordinator"/> with optional custom service factories.
    /// </summary>
    /// <param name="hostServiceFactory">Optional factory for constructing the host service.</param>
    /// <param name="clientServiceFactory">Optional factory for constructing the client service.</param>
    /// <param name="hostEndpointConfig">Optional endpoint configuration for host network listeners.</param>
    public MoonshineRuntimeCoordinator(
        Func<IMoonshineHostService>? hostServiceFactory = null,
        Func<IMoonshineClientService>? clientServiceFactory = null,
        HostEndpointConfig? hostEndpointConfig = null)
    {
        _hostServiceFactory = hostServiceFactory;
        _clientServiceFactory = clientServiceFactory;
        _hostEndpointConfig = hostEndpointConfig ?? HostEndpointConfig.Default;
    }

    public ApplicationRole ActiveRole
    {
        get
        {
            lock (_lock) return _activeRole;
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

    public async ValueTask<RoleTransitionResult> StartAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        if (role == ApplicationRole.None)
        {
            return await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        ApplicationRole previousRole;
        RuntimeState previousState;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (role == _activeRole && _state == RuntimeState.Running)
            {
                return new RoleTransitionResult(true, _activeRole, role, _state, "Role is already active and running.");
            }

            previousRole = _activeRole;
            previousState = _state;
            SetStateNoLock(RuntimeState.Starting, "Starting requested runtime capabilities.");
        }

        bool hostStarted = false;
        bool clientStarted = false;
        IMoonshineHostService? hostToStart = null;
        IMoonshineClientService? clientToStart = null;
        IMoonshineHostService? hostToStop = null;
        IMoonshineClientService? clientToStop = null;

        lock (_lock)
        {
            // Prepare host capability
            if (role.HasHost())
            {
                _hostService ??= CreateHostServiceNoLock();
                hostToStart = _hostService;
            }
            else if (_hostService is not null)
            {
                hostToStop = _hostService;
                _hostService = null;
            }

            // Prepare client capability
            if (role.HasClient())
            {
                _clientService ??= CreateClientServiceNoLock();
                clientToStart = _clientService;
            }
            else if (_clientService is not null)
            {
                clientToStop = _clientService;
                _clientService = null;
            }
        }

        // Stop disabled capabilities first
        if (hostToStop is not null)
        {
            try
            {
                await hostToStop.StopAsync(cancellationToken).ConfigureAwait(false);
                hostToStop.Dispose();
            }
            // ALLOWED_EXCEPTION: Dispatches fault event and continues cleanup during role transition.
            catch (Exception ex)
            {
                DispatchFault(ApplicationRole.Host, nameof(IMoonshineHostService), ex);
            }
        }

        if (clientToStop is not null)
        {
            try
            {
                await clientToStop.StopAsync(cancellationToken).ConfigureAwait(false);
                clientToStop.Dispose();
            }
            // ALLOWED_EXCEPTION: Dispatches fault event and continues cleanup during role transition.
            catch (Exception ex)
            {
                DispatchFault(ApplicationRole.Client, nameof(IMoonshineClientService), ex);
            }
        }

        // Start enabled capabilities with atomic rollback protection
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (hostToStart is not null)
            {
                await hostToStart.StartAsync(cancellationToken).ConfigureAwait(false);
                hostStarted = true;
            }

            if (clientToStart is not null)
            {
                await clientToStart.StartAsync(cancellationToken).ConfigureAwait(false);
                clientStarted = true;
            }

            lock (_lock)
            {
                _activeRole = role;
                RuntimeState targetState = DeriveOverallStateNoLock();
                SetStateNoLock(targetState, $"Activated role: {role.FormatRole()}");
                return new RoleTransitionResult(true, previousRole, role, _state, $"Role {role.FormatRole()} activated successfully.");
            }
        }
        // ALLOWED_EXCEPTION: Executes rollback of partially started services and captures fault state.
        catch (Exception ex)
        {
            // Rollback all partially started services to preserve zero resource guarantees
            if (hostStarted && hostToStart is not null)
            {
                try
                {
                    await hostToStart.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                // ALLOWED_EXCEPTION: Dispatches secondary fault during rollback without breaking rollback loop.
                catch (Exception rollbackEx)
                {
                    DispatchFault(ApplicationRole.Host, nameof(IMoonshineHostService), rollbackEx);
                }
            }

            if (clientStarted && clientToStart is not null)
            {
                try
                {
                    await clientToStart.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                // ALLOWED_EXCEPTION: Dispatches secondary fault during rollback without breaking rollback loop.
                catch (Exception rollbackEx)
                {
                    DispatchFault(ApplicationRole.Client, nameof(IMoonshineClientService), rollbackEx);
                }
            }

            lock (_lock)
            {
                _activeRole = ApplicationRole.None;
                _hostService?.Dispose();
                _hostService = null;
                _clientService?.Dispose();
                _clientService = null;
                SetStateNoLock(RuntimeState.Faulted, $"Startup faulted and rolled back: {ex.Message}");
                DispatchFault(role, nameof(MoonshineRuntimeCoordinator), ex);
                return new RoleTransitionResult(false, previousRole, role, _state, $"Startup failed and rolled back: {ex.Message}", ex);
            }
        }
    }

    public async ValueTask<RoleTransitionResult> StopAsync(CancellationToken cancellationToken = default)
    {
        ApplicationRole previousRole;
        RuntimeState previousState;
        IMoonshineHostService? host;
        IMoonshineClientService? client;

        lock (_lock)
        {
            if (_state == RuntimeState.Stopped && _activeRole == ApplicationRole.None)
            {
                return new RoleTransitionResult(true, _activeRole, ApplicationRole.None, RuntimeState.Stopped, "Runtime is already stopped.");
            }

            previousRole = _activeRole;
            previousState = _state;
            SetStateNoLock(RuntimeState.Stopping, "Stopping active runtime capabilities.");

            host = _hostService;
            _hostService = null;

            client = _clientService;
            _clientService = null;

            _activeRole = ApplicationRole.None;
        }

        if (host is not null)
        {
            try
            {
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
                host.Dispose();
            }
            // ALLOWED_EXCEPTION: Dispatches fault event and continues stopping remaining capabilities.
            catch (Exception ex)
            {
                DispatchFault(ApplicationRole.Host, nameof(IMoonshineHostService), ex);
            }
        }

        if (client is not null)
        {
            try
            {
                await client.StopAsync(cancellationToken).ConfigureAwait(false);
                client.Dispose();
            }
            // ALLOWED_EXCEPTION: Dispatches fault event and continues stopping remaining capabilities.
            catch (Exception ex)
            {
                DispatchFault(ApplicationRole.Client, nameof(IMoonshineClientService), ex);
            }
        }

        lock (_lock)
        {
            SetStateNoLock(RuntimeState.Stopped, "All runtime capabilities stopped successfully.");
            return new RoleTransitionResult(true, previousRole, ApplicationRole.None, _state, "Runtime stopped successfully.");
        }
    }

    public async ValueTask<RoleTransitionResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        ApplicationRole currentRole;
        lock (_lock)
        {
            currentRole = _activeRole;
        }

        if (currentRole == ApplicationRole.None)
        {
            return new RoleTransitionResult(true, ApplicationRole.None, ApplicationRole.None, RuntimeState.Stopped, "Runtime is not running.");
        }

        var stopResult = await StopAsync(cancellationToken).ConfigureAwait(false);
        if (!stopResult.Success)
        {
            return stopResult;
        }

        return await StartAsync(currentRole, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RoleTransitionResult> TransitionToRoleAsync(ApplicationRole targetRole, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (targetRole == _activeRole)
            {
                return new RoleTransitionResult(true, _activeRole, targetRole, _state, $"Already in target role: {targetRole.FormatRole()}");
            }
        }

        return await StartAsync(targetRole, cancellationToken).ConfigureAwait(false);
    }

    public RuntimeStatus GetStatus()
    {
        lock (_lock)
        {
            HostStatus hostStatus = _hostService?.GetStatus() ?? new HostStatus(
                State: RuntimeState.Stopped,
                IsRunning: false,
                ActiveSessionCount: 0,
                ActiveListenerCount: 0,
                ActiveWorkerCount: 0,
                ActiveBufferCount: 0,
                LastError: null);

            ClientStatus clientStatus = _clientService?.GetStatus() ?? new ClientStatus(
                State: RuntimeState.Stopped,
                IsRunning: false,
                IsConnected: false,
                ActiveWorkerCount: 0,
                ActiveBufferCount: 0,
                LastError: null);

            return new RuntimeStatus(
                ActiveRole: _activeRole,
                State: _state,
                Host: hostStatus,
                Client: clientStatus,
                Timestamp: DateTimeOffset.UtcNow);
        }
    }

    private IMoonshineHostService CreateHostServiceNoLock()
    {
        if (_hostServiceFactory is not null)
        {
            return _hostServiceFactory();
        }

        return new MoonshineDefaultHostService(endpointConfig: _hostEndpointConfig);
    }

    private IMoonshineClientService CreateClientServiceNoLock()
    {
        if (_clientServiceFactory is not null)
        {
            return _clientServiceFactory();
        }

        return new MoonshineClientCoordinator();
    }

    private RuntimeState DeriveOverallStateNoLock()
    {
        RuntimeState hostState = _hostService?.State ?? RuntimeState.Stopped;
        RuntimeState clientState = _clientService?.State ?? RuntimeState.Stopped;

        if (hostState == RuntimeState.Faulted || clientState == RuntimeState.Faulted)
        {
            return RuntimeState.Faulted;
        }

        if (hostState == RuntimeState.Unsupported || clientState == RuntimeState.Unsupported)
        {
            return RuntimeState.Unsupported;
        }

        if (hostState == RuntimeState.Running || clientState == RuntimeState.Running)
        {
            return RuntimeState.Running;
        }

        return RuntimeState.Stopped;
    }

    private void SetStateNoLock(RuntimeState newState, string? reason)
    {
        RuntimeState previous = _state;
        _state = newState;
        if (previous != newState)
        {
            StateChanged?.Invoke(this, new RuntimeStateChangedEventArgs(_activeRole, previous, newState, reason));
        }
    }

    private void DispatchFault(ApplicationRole role, string component, Exception ex)
    {
        Faulted?.Invoke(this, new RuntimeFaultEventArgs(role, component, ex));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// Default host role service used when no external host factory is supplied to the coordinator.
/// Coordinates host network listeners and guarantees complete teardown when disabled.
/// </summary>
internal sealed class MoonshineDefaultHostService : IMoonshineHostService
{
    private readonly IMoonshineHostNetworkManager _networkManager;
    private readonly HostEndpointConfig _endpointConfig;
    private readonly Lock _lock = new();
    private RuntimeState _state = RuntimeState.Stopped;
    private int _activeSessions;
    private int _activeWorkers;
    private int _activeBuffers;
    private string? _lastError;
    private CancellationTokenSource? _workerCts;
    private bool _disposed;

    public MoonshineDefaultHostService(
        IMoonshineHostNetworkManager? networkManager = null,
        HostEndpointConfig? endpointConfig = null)
    {
        _networkManager = networkManager ?? new MoonshineHostNetworkManager();
        _endpointConfig = endpointConfig ?? HostEndpointConfig.Default;
    }

    public ApplicationRole Role => ApplicationRole.Host;

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
                return _activeSessions > 0 || _networkManager.ActiveListenerCount > 0 || _activeWorkers > 0 || _activeBuffers > 0;
            }
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state is RuntimeState.Running or RuntimeState.Starting)
            {
                return;
            }

            _state = RuntimeState.Starting;
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _networkManager.StartListenersAsync(_endpointConfig, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                _workerCts = new CancellationTokenSource();
                // Fail-closed baseline: reports Unsupported until native session control is implemented
                _state = RuntimeState.Unsupported;
                _lastError = "Host media transport and session control are not yet implemented.";
            }
        }
        // ALLOWED_EXCEPTION: Re-throws after capturing error state and cleaning up resources.
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = RuntimeState.Faulted;
                _lastError = ex.Message;
            }
            await CleanupResourcesAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_state == RuntimeState.Stopped)
            {
                return;
            }

            _state = RuntimeState.Stopping;
        }

        await CleanupResourcesAsync().ConfigureAwait(false);

        lock (_lock)
        {
            _state = RuntimeState.Stopped;
        }
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
            return new HostStatus(
                State: _state,
                IsRunning: _state == RuntimeState.Running,
                ActiveSessionCount: _activeSessions,
                ActiveListenerCount: _networkManager.ActiveListenerCount,
                ActiveWorkerCount: _activeWorkers,
                ActiveBufferCount: _activeBuffers,
                LastError: _lastError);
        }
    }

    private async ValueTask CleanupResourcesAsync()
    {
        if (_workerCts is not null)
        {
            await _workerCts.CancelAsync().ConfigureAwait(false);
            _workerCts.Dispose();
            _workerCts = null;
        }

        await _networkManager.StopListenersAsync().ConfigureAwait(false);

        _activeSessions = 0;
        _activeWorkers = 0;
        _activeBuffers = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _state = RuntimeState.Stopping;
        }

        CleanupResourcesAsync().AsTask().GetAwaiter().GetResult();
        _networkManager.Dispose();

        lock (_lock)
        {
            _state = RuntimeState.Stopped;
        }
    }
}
