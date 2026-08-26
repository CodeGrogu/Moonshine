using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Session;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;

namespace Moonshine.App;

public static class LoopbackTestRunner
{
    public static async Task RunLoopbackAsync(CliOptions options, CancellationToken ct)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine In-Process Loopback Performance Benchmark");
        Console.WriteLine("==========================================================");

        int durationSec = Math.Max(1, options.DurationSeconds);
        int targetFps = Math.Max(1, options.Fps);
        int totalFrames = durationSec * targetFps;
        int frameSizeBytes = 65536; // 64 KB compressed video frame payload
        int mtuPayload = 1188;
        int packetsPerFrame = (frameSizeBytes + mtuPayload - 1) / mtuPayload; // ~56 packets

        Console.WriteLine($"[*] Running {durationSec}s loopback streaming benchmark ({targetFps} FPS, ~{totalFrames} frames)...");
        Console.WriteLine($"    Benchmark Frame Size: {frameSizeBytes / 1024} KB ({packetsPerFrame} packets / frame)");

        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            ConnectionTimeoutSeconds = 0
        };

        await using var session = new MoonshineClientStreamingSession(config);
        await session.StartAsync(ct).ConfigureAwait(false);

        int clientVideoPort = session.BoundLocalVideoPort;
        var clientVideoEp = new IPEndPoint(IPAddress.Loopback, clientVideoPort);
        using var senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        long startMemory = GC.GetAllocatedBytesForCurrentThread();
        var frameLatenciesUs = new List<double>(totalFrames);
        long benchmarkStartQpc = Stopwatch.GetTimestamp();

        byte[] datagram = new byte[MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + mtuPayload];

        for (uint frameIdx = 0; frameIdx < (uint)totalFrames && !ct.IsCancellationRequested; frameIdx++)
        {
            long frameStartQpc = Stopwatch.GetTimestamp();
            ulong timestampUs = (ulong)(frameStartQpc * 1_000_000.0 / Stopwatch.Frequency);

            for (int pktIdx = 0; pktIdx < packetsPerFrame; pktIdx++)
            {
                int thisPayload = Math.Min(mtuPayload, frameSizeBytes - (pktIdx * mtuPayload));

                var mshnHdr = new MoonshinePacketHeader(
                    Magic: MoonshineProtocolConstants.Magic,
                    Version: MoonshineProtocolConstants.Version10,
                    MessageType: MoonshineMessageType.VideoPacket,
                    PayloadSize: (uint)(MoonshineVideoPacketCodec.HeaderSize + thisPayload),
                    SequenceNumber: (frameIdx * (uint)packetsPerFrame) + (uint)pktIdx,
                    SessionId: config.SessionId,
                    TimestampUs: timestampUs);

                MoonshineVideoAttributes flags = MoonshineVideoAttributes.None;
                if (pktIdx == 0) flags |= MoonshineVideoAttributes.FrameStart;
                if (pktIdx == packetsPerFrame - 1) flags |= MoonshineVideoAttributes.FrameEnd;
                if (frameIdx % 60 == 0 && pktIdx == 0) flags |= MoonshineVideoAttributes.Keyframe;

                var videoHdr = new MoonshineVideoPacketHeader
                {
                    StreamId = 1,
                    FrameIndex = frameIdx,
                    PacketIndex = (uint)pktIdx,
                    TotalPackets = (uint)packetsPerFrame,
                    FecBlockIndex = 0,
                    PayloadSize = (ushort)thisPayload,
                    PacketType = 0,
                    Flags = flags,
                    TotalFrameBytes = (uint)frameSizeBytes
                };

                MoonshineProtocolCodec.TryWriteHeader(in mshnHdr, datagram);
                MoonshineVideoPacketCodec.TryWriteHeader(in videoHdr,
                    datagram.AsSpan(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize));

                await senderSocket.SendToAsync(datagram.AsMemory(0, MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + thisPayload), SocketFlags.None, clientVideoEp, ct).ConfigureAwait(false);
            }

            long frameEndQpc = Stopwatch.GetTimestamp();
            double frameDurationUs = (frameEndQpc - frameStartQpc) * 1_000_000.0 / Stopwatch.Frequency;
            frameLatenciesUs.Add(frameDurationUs);

            // Bounded frame pacing
            int elapsedMs = (int)((Stopwatch.GetTimestamp() - frameStartQpc) * 1000.0 / Stopwatch.Frequency);
            int sleepTargetMs = Math.Max(0, (1000 / targetFps) - elapsedMs);
            if (sleepTargetMs > 0)
            {
                await Task.Delay(sleepTargetMs, ct).ConfigureAwait(false);
            }
        }

        // Wait for trailing packets
        await Task.Delay(200, ct).ConfigureAwait(false);

        long benchmarkEndQpc = Stopwatch.GetTimestamp();
        double totalBenchmarkDurationSec = (benchmarkEndQpc - benchmarkStartQpc) / (double)Stopwatch.Frequency;
        long endMemory = GC.GetAllocatedBytesForCurrentThread();
        long allocatedBytes = endMemory - startMemory;

        long receivedPackets = (long)session.Metrics.TotalVideoPacketsReceived;
        long totalSentPackets = totalFrames * packetsPerFrame;
        double lossPct = (double)(totalSentPackets - receivedPackets) / totalSentPackets * 100.0;
        double throughputMbps = (receivedPackets * (MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + mtuPayload) * 8.0) / (totalBenchmarkDurationSec * 1_000_000.0);

        frameLatenciesUs.Sort();
        double p50Us = frameLatenciesUs.Count > 0 ? frameLatenciesUs[(int)(frameLatenciesUs.Count * 0.50)] : 0;
        double p95Us = frameLatenciesUs.Count > 0 ? frameLatenciesUs[(int)(frameLatenciesUs.Count * 0.95)] : 0;
        double p99Us = frameLatenciesUs.Count > 0 ? frameLatenciesUs[(int)(frameLatenciesUs.Count * 0.99)] : 0;

        Console.WriteLine("\n==========================================================");
        Console.WriteLine("Moonshine Loopback Benchmark Results");
        Console.WriteLine("==========================================================");
        Console.WriteLine($"  Total Streaming Duration:  {totalBenchmarkDurationSec:F2} s");
        Console.WriteLine($"  Frames Sent / Streamed:    {totalFrames} frames ({totalFrames / totalBenchmarkDurationSec:F1} FPS)");
        Console.WriteLine($"  Packets Sent:              {totalSentPackets}");
        Console.WriteLine($"  Packets Received:          {receivedPackets}");
        Console.WriteLine($"  Packet Loss Rate:          {lossPct:F2}%");
        Console.WriteLine($"  Measured Throughput:       {throughputMbps:F2} Mbps");
        Console.WriteLine($"  Per-Frame Ingestion P50:   {p50Us:F2} us ({p50Us / 1000.0:F3} ms)");
        Console.WriteLine($"  Per-Frame Ingestion P95:   {p95Us:F2} us ({p95Us / 1000.0:F3} ms)");
        Console.WriteLine($"  Per-Frame Ingestion P99:   {p99Us:F2} us ({p99Us / 1000.0:F3} ms)");
        Console.WriteLine($"  GC Allocations / Frame:    {allocatedBytes / Math.Max(1, totalFrames)} bytes");
        Console.WriteLine("==========================================================\n");
    }
}
