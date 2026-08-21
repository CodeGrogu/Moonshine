using System.Net;
using FluentAssertions;
using Moonshine.Core.Transport;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineDatagramTransportTests : IAsyncLifetime, IDisposable
{
    private MoonshineDatagramTransport? _sender;
    private MoonshineDatagramTransport? _receiver;

    public async Task InitializeAsync()
    {
        _receiver = new MoonshineDatagramTransport(bindPort: 0);
        _sender = new MoonshineDatagramTransport(bindPort: 0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SendAndReceiveDatagrams_Loopback_TransfersSuccessfully()
    {
        _receiver!.StartReceiving();

        var receivedData = new List<byte[]>();
        var tcs = new TaskCompletionSource<bool>();
        int targetCount = 100;

        _receiver.OnDatagramReceived += (datagram, _) =>
        {
            lock (receivedData)
            {
                receivedData.Add(datagram.ToArray());
                if (receivedData.Count == targetCount)
                {
                    tcs.TrySetResult(true);
                }
            }
        };

        var destination = new IPEndPoint(IPAddress.Loopback, _receiver.LocalPort);

        for (int i = 0; i < targetCount; i++)
        {
            byte[] packet = new byte[64];
            packet[0] = (byte)i;
            packet[63] = 0xAA;

            bool sent = await _sender!.SendDatagramAsync(packet, destination);
            sent.Should().BeTrue();
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        await tcs.Task;

        receivedData.Should().HaveCount(targetCount);
        receivedData[0][0].Should().Be(0);
        receivedData[99][0].Should().Be(99);

        // Verify metrics
        _sender!.Metrics.PacketsSent.Should().Be((ulong)targetCount);
        _sender.Metrics.BytesSent.Should().Be((ulong)(targetCount * 64));
        _receiver.Metrics.PacketsReceived.Should().Be((ulong)targetCount);
        _receiver.Metrics.BytesReceived.Should().Be((ulong)(targetCount * 64));
    }

    [Fact]
    public async Task SendDatagramGather_TransfersHeaderAndPayloadContiguously()
    {
        _receiver!.StartReceiving();

        var tcs = new TaskCompletionSource<byte[]>();

        _receiver.OnDatagramReceived += (datagram, _) =>
        {
            tcs.TrySetResult(datagram.ToArray());
        };

        var destination = new IPEndPoint(IPAddress.Loopback, _receiver.LocalPort);

        byte[] header = [0x4D, 0x53, 0x48, 0x4E, 0x00, 0x01]; // 'MSHN' v1.0
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];

        bool sent = await _sender!.SendDatagramGatherAsync(header, payload, destination);
        sent.Should().BeTrue();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        byte[] received = await tcs.Task;
        received.Should().HaveCount(11);
        received[..6].Should().Equal(header);
        received[6..].Should().Equal(payload);
    }

    [Fact]
    public async Task TransportDisposal_TransitionsStateToDisconnected()
    {
        _sender!.State.Should().Be(TransportState.Connected);
        await _sender.DisposeAsync();
        _sender.State.Should().Be(TransportState.Disconnected);
    }

    public async Task DisposeAsync()
    {
        if (_sender is not null) await _sender.DisposeAsync();
        if (_receiver is not null) await _receiver.DisposeAsync();
    }

    public void Dispose()
    {
        _sender?.Dispose();
        _receiver?.Dispose();
        GC.SuppressFinalize(this);
    }
}
