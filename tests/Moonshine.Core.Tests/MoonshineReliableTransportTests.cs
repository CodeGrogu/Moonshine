using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Transport;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineReliableTransportTests : IAsyncLifetime, IDisposable
{
    private TcpListener? _listener;
    private MoonshineReliableTransport? _clientTransport;
    private MoonshineReliableTransport? _serverTransport;
    private int _port;

    public async Task InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ConnectAndExchangeFramedMessages_TransfersSuccessfully()
    {
        var acceptTask = Task.Run(async () =>
        {
            Socket serverSocket = await _listener!.AcceptSocketAsync();
            return new MoonshineReliableTransport(serverSocket);
        });

        _clientTransport = new MoonshineReliableTransport();
        await _clientTransport.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));

        _serverTransport = await acceptTask;

        _clientTransport.State.Should().Be(TransportState.Connected);
        _serverTransport.State.Should().Be(TransportState.Connected);

        // Client -> Server framed message
        byte[] clientMessage = [1, 2, 3, 4, 5, 6, 7, 8];
        bool sendOk = await _clientTransport.SendFramedMessageAsync(clientMessage);
        sendOk.Should().BeTrue();

        byte[] serverBuffer = new byte[64];
        int bytesRead = await _serverTransport.ReceiveFramedMessageAsync(serverBuffer);
        bytesRead.Should().Be(8);
        serverBuffer[..8].Should().Equal(clientMessage);

        // Server -> Client response
        byte[] serverResponse = [10, 20, 30, 40];
        bool serverSendOk = await _serverTransport.SendFramedMessageAsync(serverResponse);
        serverSendOk.Should().BeTrue();

        byte[] clientBuffer = new byte[64];
        int clientBytesRead = await _clientTransport.ReceiveFramedMessageAsync(clientBuffer);
        clientBytesRead.Should().Be(4);
        clientBuffer[..4].Should().Equal(serverResponse);

        // Verify metrics
        _clientTransport.Metrics.PacketsSent.Should().Be(1);
        _clientTransport.Metrics.PacketsReceived.Should().Be(1);
        _serverTransport.Metrics.PacketsSent.Should().Be(1);
        _serverTransport.Metrics.PacketsReceived.Should().Be(1);
    }

    [Fact]
    public async Task RemoteDisconnection_ReturnsZeroAndUpdatesState()
    {
        var acceptTask = Task.Run(async () =>
        {
            Socket serverSocket = await _listener!.AcceptSocketAsync();
            return new MoonshineReliableTransport(serverSocket);
        });

        _clientTransport = new MoonshineReliableTransport();
        await _clientTransport.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));

        _serverTransport = await acceptTask;

        // Dispose server socket abruptly
        await _serverTransport.DisposeAsync();

        byte[] buffer = new byte[64];
        int read = await _clientTransport.ReceiveFramedMessageAsync(buffer);
        read.Should().Be(0); // 0 indicates peer disconnected
        _clientTransport.State.Should().Be(TransportState.Disconnected);
    }

    public async Task DisposeAsync()
    {
        if (_clientTransport is not null) await _clientTransport.DisposeAsync();
        if (_serverTransport is not null) await _serverTransport.DisposeAsync();
        _listener?.Stop();
    }

    public void Dispose()
    {
        _clientTransport?.Dispose();
        _serverTransport?.Dispose();
        _listener?.Stop();
        GC.SuppressFinalize(this);
    }
}
