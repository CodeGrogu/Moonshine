using System.Net;

namespace Moonshine.Core.Transport;

public delegate void DatagramReceivedHandler(ReadOnlyMemory<byte> datagram, EndPoint remoteEndPoint);
public delegate void TransportFaultHandler(Exception exception, TransportState newState);

/// <summary>
/// High-performance asynchronous UDP datagram transport contract for Moonshine media and feedback channels.
/// </summary>
public interface IMoonshineDatagramTransport : IAsyncDisposable, IDisposable
{
    int LocalPort { get; }
    TransportState State { get; }
    TransportMetrics Metrics { get; }

    event DatagramReceivedHandler OnDatagramReceived;
    event TransportFaultHandler OnTransportFault;

    void StartReceiving();

    ValueTask<bool> SendDatagramAsync(
        ReadOnlyMemory<byte> datagram,
        EndPoint destination,
        CancellationToken ct = default);

    ValueTask<bool> SendDatagramGatherAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        EndPoint destination,
        CancellationToken ct = default);
}

/// <summary>
/// High-performance asynchronous reliable TCP stream transport contract for Moonshine control and management channels.
/// </summary>
public interface IMoonshineReliableTransport : IAsyncDisposable, IDisposable
{
    TransportState State { get; }
    TransportMetrics Metrics { get; }

    event TransportFaultHandler OnTransportFault;

    ValueTask ConnectAsync(EndPoint remoteEndPoint, CancellationToken ct = default);

    ValueTask<bool> SendFramedMessageAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken ct = default);

    ValueTask<int> ReceiveFramedMessageAsync(
        Memory<byte> destination,
        CancellationToken ct = default);
}
