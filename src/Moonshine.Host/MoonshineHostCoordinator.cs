using System.Net.Sockets;
using Moonshine.Core.Discovery;
using Moonshine.Core.Network;
using Moonshine.Core.Runtime;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;

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
/// encoder engines, and streaming sessions) while guaranteeing deterministic resource disposal and zero leaks when disabled.
/// </summary>
public sealed class MoonshineHostCoordinator : IMoonshineHostService
{
    private readonly IMoonshineHostNetworkManager _networkManager;
    private readonly HostEndpointConfig _endpointConfig;
    private readonly List<MoonshineHostStreamingSession> _sessions = new();
    private MoonshineHostInputPipeline? _inputPipeline;
    private MoonshineHostDiscoveryAdvertiser? _discoveryAdvertiser;
    private HostState _state = HostState.Disabled;
    private int _activeWorkers;
    private int _activeBuffers;
    private string? _lastError;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _workerCts;
    private bool _disposed;

    public MoonshineHostCoordinator(
        IMoonshineHostNetworkManager? networkManager = null,
        HostEndpointConfig? endpointConfig = null)
    {
        _networkManager = networkManager ?? new MoonshineHostNetworkManager();
        _endpointConfig = endpointConfig ?? HostEndpointConfig.Default;
    }

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

    public MoonshineHostInputPipeline? InputPipeline
    {
        get
        {
            lock (_lock) return _inputPipeline;
        }
    }

    public MoonshineHostDiscoveryAdvertiser? DiscoveryAdvertiser
    {
        get
        {
            lock (_lock) return _discoveryAdvertiser;
        }
    }

    public IReadOnlyList<MoonshineHostStreamingSession> ActiveSessions
    {
        get
        {
            lock (_lock) return _sessions.ToList().AsReadOnly();
        }
    }

    public bool HasActiveResources
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count > 0 || _networkManager.ActiveListenerCount > 0 || _activeWorkers > 0 || _activeBuffers > 0 || _inputPipeline is not null || _discoveryAdvertiser is not null;
            }
        }
    }

    public void Enable()
    {
        lock (_lock)
        {
            if (_state is HostState.Running or HostState.Starting) return;
            _state = HostState.Running;
            _lastError = null;
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (_state is HostState.Disabled or HostState.Stopping) return;
            _state = HostState.Stopping;
        }

        CleanupResourcesAsync().AsTask().GetAwaiter().GetResult();

        lock (_lock)
        {
            _state = HostState.Disabled;
        }
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state is HostState.Running or HostState.Starting)
            {
                return;
            }

            _state = HostState.Starting;
            _lastError = null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _networkManager.StartListenersAsync(_endpointConfig, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                _discoveryAdvertiser = new MoonshineHostDiscoveryAdvertiser(_endpointConfig);
                _discoveryAdvertiser.Start();
                _inputPipeline = new MoonshineHostInputPipeline();
                _workerCts = new CancellationTokenSource();
                _state = HostState.Running;
                _lastError = null;
            }
        }
        // ALLOWED_EXCEPTION: Re-throws after capturing error state and cleaning up resources.
        catch (Exception ex)
        {
            lock (_lock)
            {
                _state = HostState.Faulted;
                _lastError = ex.Message;
            }
            await CleanupResourcesAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates and starts a unified host streaming session.
    /// </summary>
    public async ValueTask<MoonshineHostStreamingSession> CreateAndStartSessionAsync(
        HostSessionConfig? sessionConfig = null,
        IDesktopCapturePipeline? capturePipeline = null,
        UnifiedHardwareEncoderEngine? encoderEngine = null,
        MoonshineHostAudioPipeline? audioPipeline = null,
        MoonshineHostInputPipeline? inputPipeline = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != HostState.Running)
            {
                throw new InvalidOperationException($"Host coordinator is in {_state} state and cannot create streaming sessions.");
            }
        }

        var session = new MoonshineHostStreamingSession(
            config: sessionConfig,
            capturePipeline: capturePipeline,
            encoderEngine: encoderEngine,
            audioPipeline: audioPipeline,
            inputPipeline: inputPipeline ?? _inputPipeline);

        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _sessions.Add(session);
            }
            return session;
        }
        // ALLOWED_EXCEPTION: Cleans up failed session and propagates initialization fault.
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Stops and removes an active streaming session.
    /// </summary>
    public async ValueTask StopSessionAsync(MoonshineHostStreamingSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            _sessions.Remove(session);
        }
        await session.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_state == HostState.Disabled)
            {
                return;
            }

            _state = HostState.Stopping;
        }

        await CleanupResourcesAsync().ConfigureAwait(false);

        lock (_lock)
        {
            _state = HostState.Disabled;
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
            RuntimeState runtimeState = ((IMoonshineRoleService)this).State;
            return new HostStatus(
                State: runtimeState,
                IsRunning: _state == HostState.Running,
                ActiveSessionCount: _sessions.Count,
                ActiveListenerCount: _networkManager.ActiveListenerCount,
                ActiveWorkerCount: (_discoveryAdvertiser != null ? 1 : 0) + _sessions.Count * 2,
                ActiveBufferCount: _activeBuffers,
                LastError: _lastError);
        }
    }

    private async ValueTask CleanupResourcesAsync()
    {
        List<MoonshineHostStreamingSession> sessionsToDispose;
        lock (_lock)
        {
            sessionsToDispose = _sessions.ToList();
            _sessions.Clear();
        }

        foreach (var session in sessionsToDispose)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            // ALLOWED_EXCEPTION: Ignore secondary socket/io errors during session cleanup.
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException or ObjectDisposedException or System.Runtime.InteropServices.ExternalException)
            {
            }
        }

        if (_discoveryAdvertiser is not null)
        {
            _discoveryAdvertiser.Dispose();
            _discoveryAdvertiser = null;
        }

        if (_workerCts is not null)
        {
            await _workerCts.CancelAsync().ConfigureAwait(false);
            _workerCts.Dispose();
            _workerCts = null;
        }

        if (_inputPipeline is not null)
        {
            _inputPipeline.Dispose();
            _inputPipeline = null;
        }

        await _networkManager.StopListenersAsync().ConfigureAwait(false);

        _activeWorkers = 0;
        _activeBuffers = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _state = HostState.Stopping;
        }

        CleanupResourcesAsync().AsTask().GetAwaiter().GetResult();
        _networkManager.Dispose();

        lock (_lock)
        {
            _state = HostState.Disabled;
        }
    }
}
