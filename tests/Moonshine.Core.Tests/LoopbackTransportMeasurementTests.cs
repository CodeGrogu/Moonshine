using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Session;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;
using Xunit;
using Xunit.Abstractions;

namespace Moonshine.Core.Tests;

/// <summary>
/// End-to-end loopback transport measurement tests.
/// Sends MNBP video packets over localhost UDP and measures throughput, jitter, and loss.
/// </summary>
public class LoopbackTransportMeasurementTests
{
    private readonly ITestOutputHelper _output;

    public LoopbackTransportMeasurementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task LoopbackTransport_SendAndReceive1000VideoPackets_MeasuresThroughputAndJitter()
    {
        const int totalPackets = 1000;
        const int payloadSize = 1188; // Moonshine default MTU payload

        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            ConnectionTimeoutSeconds = 0
        };

        await using var session = new MoonshineClientStreamingSession(config);
        await session.StartAsync();

        int clientVideoPort = session.BoundLocalVideoPort;
        var clientVideoEp = new IPEndPoint(IPAddress.Loopback, clientVideoPort);
        using var senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Pre-build video datagrams
        byte[] datagram = new byte[MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + payloadSize];

        // Fill payload with deterministic pattern
        for (int i = MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize; i < datagram.Length; i++)
        {
            datagram[i] = (byte)(i & 0xFF);
        }

        var videoHdr = new MoonshineVideoPacketHeader
        {
            StreamId = 1,
            FrameIndex = 0,
            PacketIndex = 0,
            TotalPackets = 1,
            FecBlockIndex = 0,
            PayloadSize = (ushort)payloadSize,
            PacketType = 0,
            Flags = MoonshineVideoAttributes.Keyframe | MoonshineVideoAttributes.FrameStart | MoonshineVideoAttributes.FrameEnd,
            TotalFrameBytes = (uint)payloadSize
        };

        MoonshineVideoPacketCodec.TryWriteHeader(
            in videoHdr,
            datagram.AsSpan(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize));

        // Send packets with microsecond-precision timing
        long[] sendTimestamps = new long[totalPackets];
        long startQpc = Stopwatch.GetTimestamp();

        for (uint seq = 0; seq < totalPackets; seq++)
        {
            ulong timestampUs = (ulong)((Stopwatch.GetTimestamp() - startQpc) * 1_000_000.0 / Stopwatch.Frequency);
            var mshnHdr = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.VideoPacket,
                PayloadSize: (uint)(MoonshineVideoPacketCodec.HeaderSize + payloadSize),
                SequenceNumber: seq,
                SessionId: config.SessionId,
                TimestampUs: timestampUs);

            MoonshineProtocolCodec.TryWriteHeader(in mshnHdr, datagram);
            sendTimestamps[seq] = Stopwatch.GetTimestamp();

            await senderSocket.SendToAsync(datagram, SocketFlags.None, clientVideoEp);
        }

        // Wait for client to receive packets
        for (int i = 0; i < 200 && (long)session.Metrics.TotalVideoPacketsReceived < totalPackets; i++)
        {
            await Task.Delay(10);
        }

        long endQpc = Stopwatch.GetTimestamp();
        double totalDurationMs = (endQpc - startQpc) * 1000.0 / Stopwatch.Frequency;

        // Calculate metrics
        long received = (long)session.Metrics.TotalVideoPacketsReceived;
        long lost = totalPackets - received;
        double lossRate = (double)lost / totalPackets * 100.0;
        double throughputMbps = (received * (MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + payloadSize) * 8.0) / (totalDurationMs / 1000.0) / 1_000_000.0;
        double packetsPerSecond = received / (totalDurationMs / 1000.0);

        // Calculate inter-packet jitter from send timestamps
        double totalJitterUs = 0;
        int jitterSamples = 0;
        for (int i = 1; i < Math.Min(received, totalPackets); i++)
        {
            double intervalUs = (sendTimestamps[i] - sendTimestamps[i - 1]) * 1_000_000.0 / Stopwatch.Frequency;
            double expectedIntervalUs = (sendTimestamps[1] - sendTimestamps[0]) * 1_000_000.0 / Stopwatch.Frequency;
            totalJitterUs += Math.Abs(intervalUs - expectedIntervalUs);
            jitterSamples++;
        }
        double averageJitterUs = jitterSamples > 0 ? totalJitterUs / jitterSamples : 0;

        // Output measurement results
        _output.WriteLine("=== Moonshine Loopback Transport Measurement ===");
        _output.WriteLine($"Packets Sent:      {totalPackets}");
        _output.WriteLine($"Packets Received:  {received}");
        _output.WriteLine($"Packets Lost:      {lost}");
        _output.WriteLine($"Loss Rate:         {lossRate:F2}%");
        _output.WriteLine($"Payload Size:      {payloadSize} B");
        _output.WriteLine($"Datagram Size:     {datagram.Length} B");
        _output.WriteLine($"Duration:          {totalDurationMs:F2} ms");
        _output.WriteLine($"Throughput:        {throughputMbps:F2} Mbps");
        _output.WriteLine($"Packets/sec:       {packetsPerSecond:F0}");
        _output.WriteLine($"Avg Send Jitter:   {averageJitterUs:F2} us");
        _output.WriteLine("================================================");

        // Assertions
        received.Should().BeGreaterThanOrEqualTo(totalPackets * 95 / 100, "at least 95% of packets should arrive over localhost");
        throughputMbps.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public async Task LoopbackTransport_BurstSend64KBFrame_MeasuresReassemblyLatency()
    {
        // Simulate a 64 KB compressed video frame split across multiple MTU-sized packets
        const int frameSize = 65536;
        const int mtuPayload = 1188;
        int totalPackets = (frameSize + mtuPayload - 1) / mtuPayload; // ~56 packets

        var config = new ClientSessionConfig
        {
            HostAddress = IPAddress.Loopback,
            LocalVideoPort = 0,
            LocalAudioPort = 0,
            LocalControlFeedbackPort = 0,
            ConnectionTimeoutSeconds = 0
        };

        await using var session = new MoonshineClientStreamingSession(config);
        await session.StartAsync();

        int clientVideoPort = session.BoundLocalVideoPort;
        var clientVideoEp = new IPEndPoint(IPAddress.Loopback, clientVideoPort);
        using var senderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        long burstStart = Stopwatch.GetTimestamp();
        ulong timestampUs = (ulong)(burstStart * 1_000_000.0 / Stopwatch.Frequency);

        for (int pktIdx = 0; pktIdx < totalPackets; pktIdx++)
        {
            int thisPayload = Math.Min(mtuPayload, frameSize - (pktIdx * mtuPayload));
            byte[] datagram = new byte[MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize + thisPayload];

            var mshnHdr = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.VideoPacket,
                PayloadSize: (uint)(MoonshineVideoPacketCodec.HeaderSize + thisPayload),
                SequenceNumber: (uint)pktIdx,
                SessionId: config.SessionId,
                TimestampUs: timestampUs);

            MoonshineVideoAttributes flags = MoonshineVideoAttributes.None;
            if (pktIdx == 0) flags |= MoonshineVideoAttributes.FrameStart | MoonshineVideoAttributes.Keyframe;
            if (pktIdx == totalPackets - 1) flags |= MoonshineVideoAttributes.FrameEnd;

            var videoHdr = new MoonshineVideoPacketHeader
            {
                StreamId = 1,
                FrameIndex = 0,
                PacketIndex = (uint)pktIdx,
                TotalPackets = (uint)totalPackets,
                FecBlockIndex = 0,
                PayloadSize = (ushort)thisPayload,
                PacketType = 0,
                Flags = flags,
                TotalFrameBytes = (uint)frameSize
            };

            MoonshineProtocolCodec.TryWriteHeader(in mshnHdr, datagram);
            MoonshineVideoPacketCodec.TryWriteHeader(in videoHdr,
                datagram.AsSpan(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize));

            await senderSocket.SendToAsync(datagram, SocketFlags.None, clientVideoEp);
        }

        // Wait for all packets to arrive
        for (int i = 0; i < 200 && (long)session.Metrics.TotalVideoPacketsReceived < totalPackets; i++)
        {
            await Task.Delay(10);
        }

        long burstEnd = Stopwatch.GetTimestamp();
        double burstDurationUs = (burstEnd - burstStart) * 1_000_000.0 / Stopwatch.Frequency;
        long received = (long)session.Metrics.TotalVideoPacketsReceived;

        _output.WriteLine("=== Moonshine 64 KB Frame Burst Measurement ===");
        _output.WriteLine($"Frame Size:        {frameSize} B");
        _output.WriteLine($"Packets Sent:      {totalPackets}");
        _output.WriteLine($"Packets Received:  {received}");
        _output.WriteLine($"Burst Duration:    {burstDurationUs:F2} us");
        _output.WriteLine($"MTU Payload:       {mtuPayload} B");
        _output.WriteLine("================================================");

        received.Should().BeGreaterThanOrEqualTo(totalPackets * 95 / 100);
    }
}
