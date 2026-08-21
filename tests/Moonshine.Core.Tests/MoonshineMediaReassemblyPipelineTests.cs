using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineMediaReassemblyPipelineTests
{
    [Fact]
    public unsafe void PacketiserAndReassembly_EndToEnd_ReconstructsExactBytes()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        // Ground-truth 2450 byte payload (1000 + 1000 + 450 bytes)
        byte[] groundTruth = new byte[2450];
        for (int i = 0; i < groundTruth.Length; i++)
        {
            groundTruth[i] = (byte)((i * 37 + 19) & 0xFF);
        }

        List<byte[]> datagrams = new();
        packetiser.PacketiseFrame(groundTruth, frameIndex: 101, timestampUs: 100000, isKeyframe: true, isHdr10: false, datagram =>
        {
            datagrams.Add(datagram.ToArray());
        });

        datagrams.Count.Should().Be(3);

        // Ingest all 3 datagrams
        int res0 = reassembly.IngestDatagram(datagrams[0]);
        res0.Should().Be(0); // Incomplete

        int res1 = reassembly.IngestDatagram(datagrams[1]);
        res1.Should().Be(0); // Incomplete

        int res2 = reassembly.IngestDatagram(datagrams[2]);
        res2.Should().Be(1); // Complete!

        int popRes = reassembly.TryPopCompletedFrame(out var poppedFrame);
        popRes.Should().Be(1);
        poppedFrame.FrameIndex.Should().Be(101);
        poppedFrame.TotalBytes.Should().Be((uint)groundTruth.Length);
        poppedFrame.PacketCount.Should().Be(3);
        poppedFrame.IsKeyframe.Should().Be(1);

        ReadOnlySpan<byte> reassembledSpan = new(poppedFrame.FrameBuffer, (int)poppedFrame.TotalBytes);
        reassembledSpan.SequenceEqual(groundTruth).Should().BeTrue("Reassembled frame must match ground truth byte-for-byte");
    }

    [Theory]
    [InlineData(new int[] { 0, 1, 2, 3, 4 })] // In-order
    [InlineData(new int[] { 4, 3, 2, 1, 0 })] // Complete reverse (tail first)
    [InlineData(new int[] { 4, 0, 1, 2, 3 })] // Tail first then in-order
    [InlineData(new int[] { 2, 0, 4, 1, 3 })] // Shuffled
    public unsafe void PacketiserAndReassembly_ArrivalPermutations_ReconstructsExactBytes(int[] arrivalOrder)
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 800);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] groundTruth = new byte[3650]; // 800*4 + 450 = 5 slices
        for (int i = 0; i < groundTruth.Length; i++)
        {
            groundTruth[i] = (byte)((i * 13 + 7) & 0xFF);
        }

        List<byte[]> datagrams = new();
        packetiser.PacketiseFrame(groundTruth, frameIndex: 202, timestampUs: 200000, isKeyframe: false, isHdr10: false, datagram =>
        {
            datagrams.Add(datagram.ToArray());
        });

        datagrams.Count.Should().Be(5);

        for (int step = 0; step < arrivalOrder.Length; step++)
        {
            int sliceIdx = arrivalOrder[step];
            int res = reassembly.IngestDatagram(datagrams[sliceIdx]);
            if (step == arrivalOrder.Length - 1)
            {
                res.Should().Be(1, "Last packet in permutation must complete the frame");
            }
            else
            {
                res.Should().Be(0, "Intermediate packet in permutation must not complete the frame prematurely");
            }
        }

        int popRes = reassembly.TryPopCompletedFrame(out var poppedFrame);
        popRes.Should().Be(1);
        poppedFrame.TotalBytes.Should().Be((uint)groundTruth.Length);

        ReadOnlySpan<byte> reassembledSpan = new(poppedFrame.FrameBuffer, (int)poppedFrame.TotalBytes);
        reassembledSpan.SequenceEqual(groundTruth).Should().BeTrue();
    }

    [Fact]
    public void Reassembly_DuplicatePackets_IgnoredGracefully()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] frameData = new byte[2000]; // 2 slices
        List<byte[]> datagrams = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 303, timestampUs: 300000, isKeyframe: false, isHdr10: false, datagram =>
        {
            datagrams.Add(datagram.ToArray());
        });

        reassembly.IngestDatagram(datagrams[0]).Should().Be(0);
        reassembly.IngestDatagram(datagrams[0]).Should().Be(0); // Duplicate slice 0
        reassembly.IngestDatagram(datagrams[1]).Should().Be(1); // Frame complete

        reassembly.TryPopCompletedFrame(out var frame).Should().Be(1);
        frame.TotalBytes.Should().Be(2000);
    }

    [Fact]
    public unsafe void PacketiserAndReassembly_WithFecPacketLoss_RecoversExactPayload()
    {
        // 4 Data Shards, 2 Parity Shards (can recover up to 2 lost data packets)
        int mtu = 1000;
        int k = 4;
        int m = 2;

        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: k, fecParityShards: m);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: k, fecParityShards: m, mtuPayloadSize: mtu);

        // Ground-truth 3450 byte payload (4 data packets: 1000, 1000, 1000, 450 + 2 parity packets = 6 total packets)
        byte[] groundTruth = new byte[3450];
        for (int i = 0; i < groundTruth.Length; i++)
        {
            groundTruth[i] = (byte)((i * 29 + 17) & 0xFF);
        }

        List<byte[]> allPackets = new();
        packetiser.PacketiseFrame(groundTruth, frameIndex: 404, timestampUs: 400000, isKeyframe: true, isHdr10: false, datagram =>
        {
            allPackets.Add(datagram.ToArray());
        });

        allPackets.Count.Should().Be(6); // 4 data + 2 parity

        // Simulate network loss: Drop data packet 1 and data packet 3!
        // Ingest: packet 0, packet 2, parity 0, parity 1
        reassembly.IngestDatagram(allPackets[0]).Should().Be(0);
        reassembly.IngestDatagram(allPackets[2]).Should().Be(0);
        reassembly.IngestDatagram(allPackets[4]).Should().Be(0); // Parity 0 (still waiting for enough shards)
        int fecCompleteRes = reassembly.IngestDatagram(allPackets[5]); // Parity 1 triggers FEC reconstruction!

        fecCompleteRes.Should().Be(1, "FEC reconstruction must recover lost data shards and complete the frame");

        int popRes = reassembly.TryPopCompletedFrame(out var poppedFrame);
        popRes.Should().Be(1);
        poppedFrame.FrameIndex.Should().Be(404);
        poppedFrame.TotalBytes.Should().Be((uint)groundTruth.Length);

        ReadOnlySpan<byte> reassembledSpan = new(poppedFrame.FrameBuffer, (int)poppedFrame.TotalBytes);
        reassembledSpan.SequenceEqual(groundTruth).Should().BeTrue("FEC reconstructed payload must match original ground truth byte-for-byte");

        reassembly.Metrics.PacketsRecoveredFec.Should().Be(2);
    }

    [Fact]
    public void Reassembly_InvalidDatagrams_FailsClosed()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        Span<byte> tiny = stackalloc byte[10];
        reassembly.IngestDatagram(tiny).Should().Be(-1);

        Span<byte> garbage = stackalloc byte[100];
        garbage.Fill(0xFF);
        reassembly.IngestDatagram(garbage).Should().Be(-1);
    }

    [Fact]
    public void Reassembly_HotPath_ZeroGCAllocations()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1188);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] frameData = new byte[2000];
        List<byte[]> datagrams = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, datagram =>
        {
            datagrams.Add(datagram.ToArray());
        });

        // Warm up
        foreach (var d in datagrams) reassembly.IngestDatagram(d);
        reassembly.TryPopCompletedFrame(out _);

        byte[][] datagramArray = datagrams.ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            for (int d = 0; d < datagramArray.Length; d++)
            {
                reassembly.IngestDatagram(datagramArray[d]);
            }
            reassembly.TryPopCompletedFrame(out _);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0, "Media reassembly steady-state hot path must have zero GC allocations");
    }

    [Fact]
    public void SlotModel_SupersededFrameLatePackets_DroppedGracefully()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] frameData = new byte[2000]; // 2 slices
        List<byte[]> frame1Packets = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 1, timestampUs: 1000, isKeyframe: false, isHdr10: false, d => frame1Packets.Add(d.ToArray()));

        List<byte[]> frame17Packets = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 17, timestampUs: 17000, isKeyframe: false, isHdr10: false, d => frame17Packets.Add(d.ToArray()));

        // Ingest Frame 1 slice 0 (Slot 1 = Frame 1)
        reassembly.IngestDatagram(frame1Packets[0]).Should().Be(0);

        // Frame 17 arrives and reuses Slot 1 (17 % 16 = 1)
        reassembly.IngestDatagram(frame17Packets[0]).Should().Be(0);

        // Late packet from Frame 1 arrives at Slot 1 (superseded frame)
        int staleRes = reassembly.IngestDatagram(frame1Packets[1]);
        staleRes.Should().Be(0, "Late packet from superseded frame must be dropped without completing Frame 17");
        reassembly.Metrics.StalePacketsDropped.Should().Be(1);

        // Frame 17 slice 1 arrives and completes Frame 17
        reassembly.IngestDatagram(frame17Packets[1]).Should().Be(1);
        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(17);
    }

    [Fact]
    public void SlotModel_SupersededFrameParity_DroppedGracefully()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000, fecDataShards: 2, fecParityShards: 1);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: 2, fecParityShards: 1, mtuPayloadSize: 1000);

        byte[] frameData = new byte[2000]; // 2 data shards + 1 parity shard
        List<byte[]> frame1Packets = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 1, timestampUs: 1000, isKeyframe: false, isHdr10: false, d => frame1Packets.Add(d.ToArray()));

        List<byte[]> frame17Packets = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 17, timestampUs: 17000, isKeyframe: false, isHdr10: false, d => frame17Packets.Add(d.ToArray()));

        // Slot 1 receives Frame 1 data 0
        reassembly.IngestDatagram(frame1Packets[0]).Should().Be(0);

        // Frame 17 evicts Slot 1
        reassembly.IngestDatagram(frame17Packets[0]).Should().Be(0);

        // Parity from Frame 1 arrives at Slot 1 (superseded frame)
        reassembly.IngestDatagram(frame1Packets[2]).Should().Be(0, "Parity from superseded frame must be dropped");
        reassembly.Metrics.StalePacketsDropped.Should().Be(1);

        // Frame 17 completes normally
        reassembly.IngestDatagram(frame17Packets[1]).Should().Be(1);
        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(17);
    }

    [Fact]
    public void Fec_ReconstructionHotPath_ZeroGCAllocations()
    {
        int mtu = 1000;
        int k = 4;
        int m = 2;

        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: k, fecParityShards: m);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: k, fecParityShards: m, mtuPayloadSize: mtu);

        byte[] payload = new byte[3450]; // 4 data + 2 parity
        List<byte[]> allPackets = new();
        packetiser.PacketiseFrame(payload, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => allPackets.Add(d.ToArray()));

        byte[][] packetArray = allPackets.ToArray();

        // Warm up reconstruction path with Frame 1 (drop data 1 and 3, feed parity 0 and 1)
        reassembly.IngestDatagram(packetArray[0]);
        reassembly.IngestDatagram(packetArray[2]);
        reassembly.IngestDatagram(packetArray[4]);
        reassembly.IngestDatagram(packetArray[5]);
        reassembly.TryPopCompletedFrame(out _);

        // Measure GC allocations across 50 simulated FEC reconstruction cycles
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int cycle = 0; cycle < 50; cycle++)
        {
            ulong fIndex = (ulong)(cycle + 2);
            // Construct frame with packet index
            // Ingest packet 0, packet 2, parity 0, parity 1
            reassembly.IngestDatagram(packetArray[0]);
            reassembly.IngestDatagram(packetArray[2]);
            reassembly.IngestDatagram(packetArray[4]);
            reassembly.IngestDatagram(packetArray[5]);
            reassembly.TryPopCompletedFrame(out _);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0, "FEC reconstruction hot path must produce strictly zero GC heap allocations");
    }

    [Fact]
    public unsafe void Fec_PartialParityArrival_DoesNotCorruptState()
    {
        int mtu = 1000;
        int k = 4;
        int m = 2;

        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: k, fecParityShards: m);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: k, fecParityShards: m, mtuPayloadSize: mtu);

        byte[] groundTruth = new byte[3450];
        for (int i = 0; i < groundTruth.Length; i++) groundTruth[i] = (byte)(i & 0xFF);

        List<byte[]> allPackets = new();
        packetiser.PacketiseFrame(groundTruth, frameIndex: 88, timestampUs: 88000, isKeyframe: true, isHdr10: false, d => allPackets.Add(d.ToArray()));

        // Ingest data 0 and data 2 (data 1 and 3 are missing: 2 erasures)
        reassembly.IngestDatagram(allPackets[0]).Should().Be(0);
        reassembly.IngestDatagram(allPackets[2]).Should().Be(0);

        // First parity arrives (only 1 parity for 2 erasures, cannot reconstruct yet)
        reassembly.IngestDatagram(allPackets[4]).Should().Be(0);

        // Second parity arrives (2 parities for 2 erasures, completes reconstruction)
        reassembly.IngestDatagram(allPackets[5]).Should().Be(1);

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(88);
        popped.TotalBytes.Should().Be((uint)groundTruth.Length);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(groundTruth).Should().BeTrue();
    }

    [Fact]
    public void Fec_MultipleReconstructionAttempts_IsIdempotent()
    {
        int mtu = 1000;
        int k = 2;
        int m = 2;

        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: k, fecParityShards: m);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: k, fecParityShards: m, mtuPayloadSize: mtu);

        byte[] frameData = new byte[2000]; // 2 data + 2 parity
        List<byte[]> packets = new();
        packetiser.PacketiseFrame(frameData, frameIndex: 99, timestampUs: 99000, isKeyframe: false, isHdr10: false, d => packets.Add(d.ToArray()));

        // Drop data 1; ingest data 0 + parity 0 (triggers reconstruction and frame completion)
        reassembly.IngestDatagram(packets[0]).Should().Be(0);
        reassembly.IngestDatagram(packets[2]).Should().Be(1);

        // Now parity 1 arrives after frame is completed
        reassembly.IngestDatagram(packets[3]).Should().Be(0, "Subsequent parity arrivals after frame completion must be ignored safely");

        reassembly.Metrics.FramesCompleted.Should().Be(1);
        reassembly.Metrics.PacketsRecoveredFec.Should().Be(1);
    }
}
