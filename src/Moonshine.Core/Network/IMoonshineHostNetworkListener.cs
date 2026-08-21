using System.Net;
using System.Net.Sockets;

namespace Moonshine.Core.Network;

/// <summary>
/// Represents an individual bound network listener socket for a host service.
/// </summary>
public interface IMoonshineHostNetworkListener : IDisposable
{
    /// <summary>
    /// Gets the name or service identifier of this listener.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Gets the bound local endpoint.
    /// </summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>
    /// Gets the protocol type (TCP or UDP).
    /// </summary>
    ProtocolType Protocol { get; }

    /// <summary>
    /// Gets a value indicating whether this listener is actively accepting/processing traffic.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Stops listening and immediately closes the underlying socket.
    /// </summary>
    void StopListening();
}
