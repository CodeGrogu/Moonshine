using System.Net;

namespace Moonshine.Core.Network;

/// <summary>
/// High-reliability network exposure coordinator for Moonshine Host services.
/// Manages the atomic binding, isolation, and teardown of all host listening endpoints.
/// </summary>
public sealed class MoonshineHostNetworkManager : IMoonshineHostNetworkManager
{
    private readonly List<IMoonshineHostNetworkListener> _listeners = new();
    private readonly Lock _lock = new();
    private bool _isExposed;
    private bool _disposed;

    public int ActiveListenerCount
    {
        get
        {
            lock (_lock) return _listeners.Count;
        }
    }

    public bool IsExposed
    {
        get
        {
            lock (_lock) return _isExposed;
        }
    }

    public IReadOnlyList<IMoonshineHostNetworkListener> ActiveListeners
    {
        get
        {
            lock (_lock) return _listeners.ToArray();
        }
    }

    public ValueTask StartListenersAsync(HostEndpointConfig? config = null, CancellationToken cancellationToken = default)
    {
        config ??= HostEndpointConfig.Default;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isExposed)
            {
                return ValueTask.CompletedTask;
            }
        }

        var opened = new List<IMoonshineHostNetworkListener>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Control / Session TCP Listener
            opened.Add(MoonshineHostNetworkListener.CreateTcp(
                "HostControlTcp",
                new IPEndPoint(config.BindAddress, config.ControlTcpPort)));

            // 2. Host Discovery / SSDP UDP Responder
            opened.Add(MoonshineHostNetworkListener.CreateUdp(
                "HostDiscoveryUdp",
                new IPEndPoint(config.BindAddress, config.DiscoveryUdpPort)));

            // 3. Video RTP / MNBP Media Stream UDP Listener
            opened.Add(MoonshineHostNetworkListener.CreateUdp(
                "HostVideoUdp",
                new IPEndPoint(config.BindAddress, config.VideoUdpPort)));

            // 4. Control Feedback & Loss Stats UDP Listener
            opened.Add(MoonshineHostNetworkListener.CreateUdp(
                "HostFeedbackUdp",
                new IPEndPoint(config.BindAddress, config.ControlFeedbackUdpPort)));

            // 5. Audio RTP Stream UDP Listener
            opened.Add(MoonshineHostNetworkListener.CreateUdp(
                "HostAudioUdp",
                new IPEndPoint(config.BindAddress, config.AudioUdpPort)));

            // 6. Microphone Sink UDP Listener
            opened.Add(MoonshineHostNetworkListener.CreateUdp(
                "HostMicUdp",
                new IPEndPoint(config.BindAddress, config.MicUdpPort)));

            lock (_lock)
            {
                _listeners.AddRange(opened);
                _isExposed = true;
            }

            return ValueTask.CompletedTask;
        }
        // ALLOWED_EXCEPTION: Rolls back opened listeners immediately on port conflict and rethrows.
        catch (Exception)
        {
            foreach (var listener in opened)
            {
                listener.Dispose();
            }

            lock (_lock)
            {
                _listeners.Clear();
                _isExposed = false;
            }

            throw;
        }
    }

    public ValueTask StopListenersAsync(CancellationToken cancellationToken = default)
    {
        List<IMoonshineHostNetworkListener> toStop;

        lock (_lock)
        {
            if (!_isExposed && _listeners.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            toStop = new List<IMoonshineHostNetworkListener>(_listeners);
            _listeners.Clear();
            _isExposed = false;
        }

        foreach (var listener in toStop)
        {
            listener.StopListening();
            listener.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopListenersAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }
}
