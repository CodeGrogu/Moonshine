namespace Moonshine.Core.Network;

/// <summary>
/// Coordinates the complete lifecycle of Host network listeners, ensuring all listening endpoints
/// are strictly bound only when the Host role is active and completely closed when disabled.
/// </summary>
public interface IMoonshineHostNetworkManager : IDisposable
{
    /// <summary>
    /// Gets the number of currently active listening endpoints.
    /// </summary>
    int ActiveListenerCount { get; }

    /// <summary>
    /// Gets a value indicating whether host network listeners are actively running.
    /// </summary>
    bool IsExposed { get; }

    /// <summary>
    /// Gets the current active listener instances.
    /// </summary>
    IReadOnlyList<IMoonshineHostNetworkListener> ActiveListeners { get; }

    /// <summary>
    /// Asynchronously starts and binds all host listening endpoints.
    /// If any port fails to bind, all partially opened listeners are immediately rolled back.
    /// </summary>
    ValueTask StartListenersAsync(HostEndpointConfig? config = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously stops and disposes all host listening endpoints.
    /// </summary>
    ValueTask StopListenersAsync(CancellationToken cancellationToken = default);
}
