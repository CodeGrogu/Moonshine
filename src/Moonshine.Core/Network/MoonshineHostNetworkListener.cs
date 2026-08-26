using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Runtime;

namespace Moonshine.Core.Network;

/// <summary>
/// Managed wrapper for a concrete Host listening socket (TCP or UDP).
/// Enforces explicit endpoint binding, port conflict reporting, and deterministic cleanup.
/// </summary>
public sealed class MoonshineHostNetworkListener : IMoonshineHostNetworkListener
{
    private readonly Socket _socket;
    private readonly string _serviceName;
    private readonly ProtocolType _protocol;
    private readonly IPEndPoint _localEndPoint;
    private bool _isListening;
    private bool _disposed;
    private readonly Lock _lock = new();

    private MoonshineHostNetworkListener(string serviceName, Socket socket, ProtocolType protocol, IPEndPoint localEndPoint)
    {
        _serviceName = serviceName;
        _socket = socket;
        _protocol = protocol;
        _localEndPoint = localEndPoint;
        _isListening = true;
    }

    public string ServiceName => _serviceName;
    public IPEndPoint LocalEndPoint => _localEndPoint;
    public ProtocolType Protocol => _protocol;
    public Socket Socket => _socket;

    public bool IsListening
    {
        get
        {
            lock (_lock) return _isListening && !_disposed;
        }
    }

    /// <summary>
    /// Creates and binds a new TCP listener on the specified endpoint.
    /// Throws <see cref="MoonshinePortConflictException"/> if binding fails.
    /// </summary>
    public static MoonshineHostNetworkListener CreateTcp(string serviceName, IPEndPoint endpoint, int backlog = 128)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            socket.Bind(endpoint);
            socket.Listen(backlog);
            var boundEp = (IPEndPoint)socket.LocalEndPoint!;
            return new MoonshineHostNetworkListener(serviceName, socket, ProtocolType.Tcp, boundEp);
        }
        // ALLOWED_EXCEPTION: Translates native socket bind errors into deterministic port conflict exception.
        catch (SocketException ex)
        {
            socket.Dispose();
            throw MoonshinePortConflictException.Create(endpoint, ProtocolType.Tcp, ApplicationRole.Host, ex);
        }
    }

    /// <summary>
    /// Creates and binds a new UDP listener on the specified endpoint.
    /// Throws <see cref="MoonshinePortConflictException"/> if binding fails.
    /// </summary>
    public static MoonshineHostNetworkListener CreateUdp(string serviceName, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            socket.Bind(endpoint);
            var boundEp = (IPEndPoint)socket.LocalEndPoint!;
            return new MoonshineHostNetworkListener(serviceName, socket, ProtocolType.Udp, boundEp);
        }
        // ALLOWED_EXCEPTION: Translates native socket bind errors into deterministic port conflict exception.
        catch (SocketException ex)
        {
            socket.Dispose();
            throw MoonshinePortConflictException.Create(endpoint, ProtocolType.Udp, ApplicationRole.Host, ex);
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening) return;
            _isListening = false;

            try
            {
                _socket.Close();
            }
            // ALLOWED_EXCEPTION: Sockets closing during graceful teardown.
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isListening = false;

            try
            {
                _socket.Dispose();
            }
            // ALLOWED_EXCEPTION: Sockets disposing during teardown.
            catch (Exception)
            {
            }
        }
    }
}
