using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Moonshine.Core.RTSP;
using Moonshine.Protocol.RTSP;
using Xunit;

namespace Moonshine.Core.Tests;

public class RtspClientTests
{
    private sealed class MockRtspServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private Task? _listenTask;

        public int Port { get; }
        public string LastSessionId { get; set; } = "session-nv-123456";

        public MockRtspServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _listenTask = Task.Run(ListenLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on dispose
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                byte[] buffer = new byte[8192];
                while (!ct.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (bytesRead <= 0) break;

                    if (RtspMessage.TryParse(buffer.AsSpan(0, bytesRead), out var request))
                    {
                        var response = CreateResponseFor(request);
                        var writer = new System.Buffers.ArrayBufferWriter<byte>();
                        response.Serialize(writer);
                        byte[] respBytes = writer.WrittenSpan.ToArray();
                        await stream.WriteAsync(respBytes, ct).ConfigureAwait(false);
                        await stream.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
            }
        }

        private RtspMessage CreateResponseFor(RtspMessage req)
        {
            var resp = new RtspMessage
            {
                IsResponse = true,
                StatusCode = 200,
                StatusMessage = "OK",
                CSeq = req.CSeq,
                SessionId = LastSessionId
            };

            if (req.Method == RtspMethod.Options)
            {
                resp.Headers["Public"] = "OPTIONS, DESCRIBE, SETUP, PLAY, ANNOUNCE, TEARDOWN";
            }
            else if (req.Method == RtspMethod.Describe)
            {
                resp.Headers["Content-Type"] = "application/sdp";
                resp.Body = $"""
                    v=0
                    o=Sunshine 0 0 IN IP4 127.0.0.1
                    s=Stream
                    a=x-nv-session-id: {LastSessionId}
                    m=video 47998 RTP/AVP 98
                    m=audio 48000 RTP/AVP 97
                    """;
            }
            else if (req.Method == RtspMethod.Setup)
            {
                resp.Headers["Transport"] = req.Headers.GetValueOrDefault("Transport", "unicast;client_port=47998-47999;server_port=47998-47999");
            }

            return resp;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task MoonshineRtspClient_CompleteStreamLifecycle_ExecutesSuccessfully()
    {
        using var server = new MockRtspServer();
        using var client = new MoonshineRtspClient();

        var statesObserved = new List<RtspClientState>();
        client.StateChanged += s => statesObserved.Add(s);

        // 1. Connect
        await client.ConnectAsync("127.0.0.1", server.Port);
        client.State.Should().Be(RtspClientState.Connected);

        // 2. Options
        var optionsResp = await client.SendOptionsAsync();
        optionsResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.OptionsReceived);

        // 3. Describe
        var config = new MoonshineStreamConfiguration(Width: 1920, Height: 1080, FrameRate: 60, BitrateKbps: 25000);
        var describeResp = await client.SendDescribeAsync(config);
        describeResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.Described);
        client.NegotiatedSdp.Should().NotBeNull();
        client.NegotiatedSdp!.VideoPayloadType.Should().Be(98);

        // 4. Setup Video & Audio
        var setupVideoResp = await client.SendSetupVideoAsync(47998, 47999);
        setupVideoResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.VideoSetup);
        client.SessionId.Should().Be(server.LastSessionId);

        var setupAudioResp = await client.SendSetupAudioAsync(48000, 48001);
        setupAudioResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.AudioSetup);

        // 5. Play
        var playResp = await client.SendPlayAsync();
        playResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.Playing);

        // 6. Dynamic Announce (Bitrate & Loss)
        int notifiedBitrate = 0;
        client.BitrateUpdated += b => notifiedBitrate = b;

        var announceBitrateResp = await client.SendAnnounceBitrateUpdateAsync(35000);
        announceBitrateResp.StatusCode.Should().Be(200);
        notifiedBitrate.Should().Be(35000);

        var announceLossResp = await client.SendAnnounceLossStatsAsync(packetsLost: 5, totalPackets: 1000);
        announceLossResp.StatusCode.Should().Be(200);

        // 7. Teardown
        var teardownResp = await client.SendTeardownAsync();
        teardownResp.StatusCode.Should().Be(200);
        client.State.Should().Be(RtspClientState.Teardown);

        statesObserved.Should().Contain([
            RtspClientState.Connecting,
            RtspClientState.Connected,
            RtspClientState.OptionsReceived,
            RtspClientState.Described,
            RtspClientState.VideoSetup,
            RtspClientState.AudioSetup,
            RtspClientState.Playing,
            RtspClientState.Teardown
        ]);
    }
}
