using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Moonshine.Core.Session;
using Moonshine.Protocol.Contracts;

namespace Moonshine.App;

public static class ClientStreamRunner
{
    private sealed record ClientHandshakeRequest(
        int ClientVideoPort,
        int ClientAudioPort,
        int ClientControlPort,
        uint Width,
        uint Height,
        uint Fps,
        uint BitrateKbps,
        string Codec
    );

    private sealed record HostHandshakeResponse(
        string Status,
        ulong SessionId,
        int HostVideoPort,
        int HostAudioPort,
        int HostControlPort
    );

    public static async Task RunClientAsync(CliOptions options, CancellationToken ct)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine Client Streaming Session");
        Console.WriteLine("==========================================================");

        if (!IPAddress.TryParse(options.HostAddress, out var hostIp))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(options.HostAddress, ct).ConfigureAwait(false);
                hostIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? IPAddress.Loopback;
            }
            // ALLOWED_EXCEPTION: Hostname resolution fallback to loopback for isolated CLI execution.
            catch (Exception)
            {
                hostIp = IPAddress.Loopback;
            }
        }

        Console.WriteLine($"[*] Initiating streaming handshake with Host at {hostIp}:{options.Port}...");
        Console.WriteLine($"    Desired Mode:    {options.Width}x{options.Height} @ {options.Fps} FPS");
        Console.WriteLine($"    Target Bitrate:  {options.BitrateKbps} Kbps ({options.Codec.ToUpperInvariant()})");
        Console.WriteLine($"    HDR10 Enabled:   {options.EnableHdr}");

        // Ephemeral UDP port reservation
        using var vSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        vSock.Bind(new IPEndPoint(IPAddress.Any, 0));
        int clientVideoPort = ((IPEndPoint)vSock.LocalEndPoint!).Port;
        vSock.Close();

        using var aSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        aSock.Bind(new IPEndPoint(IPAddress.Any, 0));
        int clientAudioPort = ((IPEndPoint)aSock.LocalEndPoint!).Port;
        aSock.Close();

        using var cSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        cSock.Bind(new IPEndPoint(IPAddress.Any, 0));
        int clientControlPort = ((IPEndPoint)cSock.LocalEndPoint!).Port;
        cSock.Close();

        Console.WriteLine($"[+] Bound Local UDP Sockets (Video: {clientVideoPort}, Audio: {clientAudioPort}, Feedback: {clientControlPort})");

        // Connect TCP control channel to host
        using var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(hostIp, options.Port, ct).ConfigureAwait(false);
            await using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            var request = new ClientHandshakeRequest(
                ClientVideoPort: clientVideoPort,
                ClientAudioPort: clientAudioPort,
                ClientControlPort: clientControlPort,
                Width: (uint)options.Width,
                Height: (uint)options.Height,
                Fps: (uint)options.Fps,
                BitrateKbps: (uint)options.BitrateKbps,
                Codec: options.Codec);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);

            string? responseLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                Console.WriteLine("[-] Host closed connection without handshake response.");
                return;
            }

            HostHandshakeResponse? response = null;
            try
            {
                response = JsonSerializer.Deserialize<HostHandshakeResponse>(responseLine);
            }
            // ALLOWED_EXCEPTION: Ignore invalid JSON response format.
            catch (JsonException)
            {
            }

            if (response == null || !string.Equals(response.Status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[-] Handshake failed: {response?.Status ?? "Unknown host error"}");
                return;
            }

            Console.WriteLine($"\n[+] Handshake Accepted by Host! Streaming Session 0x{response.SessionId:X16} ACTIVE.");
            Console.WriteLine($"    Remote Video Port:    {response.HostVideoPort}");
            Console.WriteLine($"    Remote Audio Port:    {response.HostAudioPort}");
            Console.WriteLine($"    Remote Feedback Port: {response.HostControlPort}");
            Console.WriteLine("\nStreaming video and audio... Type 'help' for commands or press Ctrl+C to disconnect.\n");

            var videoCodec = options.Codec.ToLowerInvariant() switch
            {
                "h264" => MoonshineVideoCodec.H264,
                "av1" => MoonshineVideoCodec.Av1,
                _ => MoonshineVideoCodec.Hevc
            };

            var sessionConfig = new ClientSessionConfig
            {
                HostAddress = hostIp,
                HostVideoPort = response.HostVideoPort,
                HostAudioPort = response.HostAudioPort,
                HostControlFeedbackPort = response.HostControlPort,
                LocalVideoPort = clientVideoPort,
                LocalAudioPort = clientAudioPort,
                LocalControlFeedbackPort = clientControlPort,
                SessionId = response.SessionId,
                VideoCodec = videoCodec,
                VideoWidth = (uint)options.Width,
                VideoHeight = (uint)options.Height,
                VideoFps = (uint)options.Fps,
                VideoBitrateKbps = (uint)options.BitrateKbps,
                PerformHandshake = false
            };

            await using var session = new MoonshineClientStreamingSession(sessionConfig);
            await session.StartAsync(ct).ConfigureAwait(false);

            var monitorTask = Task.Run(() => RunInteractiveCommandLoop(session, ct), ct);

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }
        // ALLOWED_EXCEPTION: Display user-facing diagnostics when streaming session terminates unexpectedly.
        catch (Exception ex)
        {
            Console.WriteLine($"\n[-] Client Connection Error: {ex.Message}");
            Console.WriteLine($"    Ensure the host server is running at {hostIp}:{options.Port} and Windows Firewall allows incoming connections.");
        }

        Console.WriteLine("\n[*] Tearing down client streaming session and releasing decoder surfaces...");
    }

    private static void RunInteractiveCommandLoop(MoonshineClientStreamingSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Console.Write("moonshine-client> ");
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string cmd = line.Trim().ToLowerInvariant();
            string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0];

            switch (verb)
            {
                case "help" or "?":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  stats          - Show live video, audio, and network statistics");
                    Console.WriteLine("  stop / exit    - Disconnect and stop streaming");
                    break;
                case "stats":
                    var m = session.Metrics;
                    Console.WriteLine("=== Live Streaming Session Metrics ===");
                    Console.WriteLine($"  Session State:           {session.State}");
                    Console.WriteLine($"  Video Packets Received:  {m.TotalVideoPacketsReceived}");
                    Console.WriteLine($"  Video Frames Completed:  {m.TotalVideoFramesCompleted}");
                    Console.WriteLine($"  Audio Packets Received:  {m.TotalAudioPacketsReceived}");
                    Console.WriteLine($"  Audio Frames Decoded:    {m.TotalAudioFramesDecoded}");
                    Console.WriteLine($"  FEC Recovered Packets:   {m.TotalFecRecoveredPackets}");
                    Console.WriteLine($"  Lost Packets:            {m.TotalLostPackets}");
                    Console.WriteLine($"  Input Packets Sent:      {m.TotalInputPacketsSent}");
                    Console.WriteLine($"  Average Jitter:          {m.AverageJitterUs:F2} us");
                    Console.WriteLine($"  Round Trip Time (RTT):   {m.RoundTripTimeUs / 1000.0:F2} ms");
                    Console.WriteLine("======================================");
                    break;
                case "stop" or "exit" or "quit":
                    return;
                default:
                    Console.WriteLine($"Unknown command '{verb}'. Type 'help' for assistance.");
                    break;
            }
        }
    }
}
