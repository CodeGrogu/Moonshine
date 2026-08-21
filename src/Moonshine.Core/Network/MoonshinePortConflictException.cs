using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Runtime;

namespace Moonshine.Core.Network;

/// <summary>
/// Exception thrown when a network listener cannot bind to its designated endpoint due to a port conflict or permission error.
/// Prevents silent fallback to arbitrary or unsafe ports.
/// </summary>
public sealed class MoonshinePortConflictException : Exception
{
    public IPEndPoint Endpoint { get; }
    public ProtocolType Protocol { get; }
    public ApplicationRole Role { get; }

    public MoonshinePortConflictException(
        IPEndPoint endpoint,
        ProtocolType protocol,
        ApplicationRole role,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        Protocol = protocol;
        Role = role;
    }

    public static MoonshinePortConflictException Create(
        IPEndPoint endpoint,
        ProtocolType protocol,
        ApplicationRole role,
        Exception innerException)
    {
        string msg = $"Port conflict detected for {role} on {protocol} endpoint {endpoint}: {innerException.Message}";
        return new MoonshinePortConflictException(endpoint, protocol, role, msg, innerException);
    }
}
