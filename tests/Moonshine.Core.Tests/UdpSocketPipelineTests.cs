#if MOONSHINE_LEGACY_INTEROP
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Pipelines;
using Moonshine.Interop;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Core.Tests;

public class UdpSocketPipelineTests
{
    [Fact]
    public async Task UdpSocketPipeline_CreateAndDisposeAsync_GracefullyCleansUpResources()
    {
        var pipeline = new UdpSocketPipeline(0);
        pipeline.Start();

        await pipeline.DisposeAsync();
    }

    [Fact]
    public unsafe void UdpSocketPipeline_ProcessDatagram_ParsesRtpAndFiresCallback()
    {
        MoonshinePacketDesc receivedDesc = default;
        bool callbackFired = false;

        using var pipeline = new UdpSocketPipeline(
            localPort: 0,
            packetCallback: desc =>
            {
                receivedDesc = desc;
                callbackFired = true;
            }
        );

        byte[] rawPacket = new byte[1400];
        rawPacket[0] = 0x80; // V=2
        rawPacket[1] = 98 | 0x80; // HEVC Payload type + Marker (end of frame)
        rawPacket[2] = 0x12; // Seq = 0x1234 (4660)
        rawPacket[3] = 0x34;
        rawPacket[4] = 0x00; // Timestamp = 0x00010203
        rawPacket[5] = 0x01;
        rawPacket[6] = 0x02;
        rawPacket[7] = 0x03;
        rawPacket[12] = 0xFF; // First payload byte

        pipeline.ProcessDatagram(rawPacket);

        callbackFired.Should().BeTrue();
        receivedDesc.SequenceNumber.Should().Be(4660);
        receivedDesc.FrameIndex.Should().Be(0x00010203);
        receivedDesc.PayloadSize.Should().Be(1400 - 12);
        receivedDesc.PacketType.Should().Be(0); // Video Data
        (receivedDesc.Flags & 2).Should().Be(2); // End of frame marker
        (*receivedDesc.PayloadPtr).Should().Be(0xFF);

        pipeline.Metrics.PacketsReceived.Should().Be(0); // Incremented in socket receive loop
        pipeline.Metrics.PacketsDropped.Should().Be(0);
    }

    [Fact]
    public void UdpSocketPipeline_ProcessDatagram_InvalidHeader_IncrementsDroppedCount()
    {
        using var pipeline = new UdpSocketPipeline(localPort: 0);

        byte[] truncatedPacket = [0x80, 0x01]; // Too short for RTP (less than 12 bytes)
        pipeline.ProcessDatagram(truncatedPacket);

        pipeline.Metrics.PacketsDropped.Should().Be(1);
    }

    [Fact]
    public unsafe void UdpSocketPipeline_ProcessDatagram_WithNativeSpsc_EnqueuesSuccessfully()
    {
        IntPtr spscHandle = MoonshineNativeMethods.SpscCreate(128);
        spscHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            IntPtr returnQueue = IntPtr.Zero;
            int returnedSlots = 0;
            using var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle,
                nativeConsumerStopAndJoin: () =>
                {
                    while (MoonshineNativeMethods.SpscDequeue(spscHandle, out var packet) != 0)
                    {
                        MoonshineNativeMethods.SlotReturnEnqueue(returnQueue, packet.BufferSlotIndex).Should().Be(1);
                        Interlocked.Increment(ref returnedSlots);
                    }
                }
            );
            returnQueue = pipeline.ReturnQueueHandle;

            byte[] rawPacket = new byte[100];
            rawPacket[0] = 0x80;
            rawPacket[1] = 96; // H.264
            rawPacket[2] = 0x00;
            rawPacket[3] = 0x05; // Seq = 5

            pipeline.ProcessDatagram(rawPacket);

            nuint size = MoonshineNativeMethods.SpscSize(spscHandle);
            size.Should().Be(1);

            // The owner-side shutdown barrier drains the native queue and returns the backing slot.
            pipeline.Dispose();
            returnedSlots.Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(spscHandle);
        }
    }

    [Fact]
    public async Task UdpSocketPipeline_LiveUdpSocket_ReceivesDatagramsOverLoopback()
    {
        var receivedPackets = new List<uint>();
        using var pipeline = new UdpSocketPipeline(
            localPort: 0,
            packetCallback: desc =>
            {
                lock (receivedPackets)
                {
                    receivedPackets.Add(desc.SequenceNumber);
                }
            }
        );

        pipeline.Start();
        int targetPort = pipeline.Port;
        targetPort.Should().BeGreaterThan(0);

        // Send 10 UDP datagrams over loopback
        using var senderClient = new UdpClient();
        var targetEndpoint = new IPEndPoint(IPAddress.Loopback, targetPort);

        for (ushort i = 1; i <= 10; i++)
        {
            byte[] packet = new byte[64];
            packet[0] = 0x80;
            packet[1] = 98; // HEVC
            packet[2] = (byte)(i >> 8);
            packet[3] = (byte)(i & 0xFF);
            await senderClient.SendAsync(packet, packet.Length, targetEndpoint);
        }

        // Wait up to 2 seconds for packets to arrive
        for (int i = 0; i < 100; i++)
        {
            lock (receivedPackets)
            {
                if (receivedPackets.Count >= 10) break;
            }
            await Task.Delay(20);
        }

        lock (receivedPackets)
        {
            receivedPackets.Should().HaveCountGreaterThanOrEqualTo(1);
        }

        pipeline.Metrics.PacketsReceived.Should().BeGreaterThanOrEqualTo(1);
        pipeline.Metrics.BytesReceived.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UdpSocketPipeline_100kDatagrams_RecyclesAllSlotsWithZeroStarvation()
    {
        const int poolCapacity = 2048;
        const int packetCount = 100000;

        IntPtr spscHandle = MoonshineNativeMethods.SpscCreate(1024);
        spscHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            using var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle,
                poolSlotCount: poolCapacity,
                nativeConsumerStopAndJoin: () => { }
            );

            IntPtr returnQueue = pipeline.ReturnQueueHandle;
            returnQueue.Should().NotBe(IntPtr.Zero);

            int dequeuedCount = 0;
            var uniqueSlotsObserved = new HashSet<int>();
            bool producerDone = false;

            // Single consumer thread dequeuing from forward SPSC and returning to SPSC return queue
            var consumerTask = Task.Run(() =>
            {
                while (!Volatile.Read(ref producerDone) || MoonshineNativeMethods.SpscSize(spscHandle) > 0)
                {
                    if (MoonshineNativeMethods.SpscDequeue(spscHandle, out var desc) != 0)
                    {
                        desc.BufferSlotIndex.Should().BeInRange(0, poolCapacity - 1);
                        lock (uniqueSlotsObserved)
                        {
                            uniqueSlotsObserved.Add(desc.BufferSlotIndex);
                        }

                        // Return slot to unmanaged return queue
                        int ret = MoonshineNativeMethods.SlotReturnEnqueue(returnQueue, desc.BufferSlotIndex);
                        ret.Should().Be(1);
                        Interlocked.Increment(ref dequeuedCount);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            });

            // Producer: Feed 100,000 RTP datagrams (~50x pool capacity), yielding if queue is near capacity
            byte[] rawPacket = new byte[128];
            rawPacket[0] = 0x80;
            rawPacket[1] = 98; // HEVC

            for (int i = 0; i < packetCount; i++)
            {
                while (MoonshineNativeMethods.SpscSize(spscHandle) >= 1000)
                {
                    Thread.Yield();
                }

                rawPacket[2] = (byte)(i >> 8);
                rawPacket[3] = (byte)(i & 0xFF);
                pipeline.ProcessDatagram(rawPacket);
            }

            Volatile.Write(ref producerDone, true);
            await consumerTask;

            // Assertions
            dequeuedCount.Should().Be(packetCount);
            pipeline.Metrics.PacketsDropped.Should().Be(0);

            // Final drain of any remaining returned slots in pool
            unsafe
            {
                pipeline.BufferPool.TryRent(out int testSlot, out _, out _);
                if (testSlot >= 0) pipeline.BufferPool.Return(testSlot);
            }

            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();
            pipeline.BufferPool.FreeCount.Should().Be(poolCapacity);
            pipeline.BufferPool.RentedCount.Should().Be(0);
            pipeline.BufferPool.InFlightCount.Should().Be(0);
            uniqueSlotsObserved.Count.Should().BeGreaterThan(100);
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(spscHandle);
        }
    }

    [Fact]
    public void UdpSocketPipeline_ForwardQueueFull_ReturnsSlotImmediatelyWithoutLeak()
    {
        const int poolCapacity = 16;
        const int queueCapacity = 4;

        IntPtr spscHandle = MoonshineNativeMethods.SpscCreate(queueCapacity);
        spscHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            using var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle,
                poolSlotCount: poolCapacity,
                nativeConsumerStopAndJoin: () => { }
            );

            byte[] rawPacket = new byte[64];
            rawPacket[0] = 0x80;
            rawPacket[1] = 96;

            // Fill queue to capacity (4 items)
            for (int i = 0; i < queueCapacity; i++)
            {
                rawPacket[2] = (byte)(i >> 8);
                rawPacket[3] = (byte)(i & 0xFF);
                pipeline.ProcessDatagram(rawPacket);
            }

            pipeline.Metrics.PacketsDropped.Should().Be(0);
            pipeline.BufferPool.InFlightCount.Should().Be(queueCapacity);

            // Push 5th packet while forward queue is completely full
            rawPacket[2] = 0xFF;
            rawPacket[3] = 0xFF;
            pipeline.ProcessDatagram(rawPacket);

            // Dropped count must increment by 1
            pipeline.Metrics.PacketsDropped.Should().Be(1);

            // The dropped packet's slot must have been immediately returned to Free
            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();
            pipeline.BufferPool.FreeCount.Should().Be(poolCapacity - queueCapacity);
            pipeline.BufferPool.RentedCount.Should().Be(0);
            pipeline.BufferPool.InFlightCount.Should().Be(queueCapacity);

            // Clean up queue
            while (MoonshineNativeMethods.SpscDequeue(spscHandle, out var dequeued) != 0)
            {
                pipeline.BufferPool.ReturnInFlight(dequeued.BufferSlotIndex);
            }

            pipeline.BufferPool.FreeCount.Should().Be(poolCapacity);
            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(spscHandle);
        }
    }

    [Fact]
    public unsafe void PinnedBufferPool_InvalidOrDuplicateReturn_RejectedSafely()
    {
        using var pool = new PinnedBufferPool(slotCount: 8, slotSize: 64);
        pool.FreeCount.Should().Be(8);
        pool.ValidateInvariant().Should().BeTrue();

        // 1. Rent a slot
        pool.TryRent(out int slot0, out _, out _).Should().BeTrue();
        slot0.Should().Be(7); // Top of free stack
        pool.FreeCount.Should().Be(7);
        pool.RentedCount.Should().Be(1);

        // 2. Out of bounds returns
        pool.Return(-1);
        pool.Return(100);
        pool.ReturnRented(-5);
        pool.ReturnInFlight(999);
        pool.FreeCount.Should().Be(7);

        // 3. Return rented slot
        pool.ReturnRented(slot0);
        pool.FreeCount.Should().Be(8);
        pool.RentedCount.Should().Be(0);

        // 4. Duplicate return of already Free slot is ignored safely
        pool.Return(slot0);
        pool.ReturnRented(slot0);
        pool.FreeCount.Should().Be(8);
        pool.ValidateInvariant().Should().BeTrue();
    }

    [Fact]
    public unsafe void UdpSocketPipeline_NativeConsumerDropPath_RecyclesSlotBackToFreeState()
    {
        const int poolCapacity = 4;
        IntPtr spscHandle = MoonshineNativeMethods.SpscCreate(poolCapacity);
        spscHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            using var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle,
                poolSlotCount: poolCapacity,
                nativeConsumerStopAndJoin: () => { }
            );

            IntPtr returnQueue = pipeline.ReturnQueueHandle;
            returnQueue.Should().NotBe(IntPtr.Zero);

            byte[] rawPacket = new byte[64];
            rawPacket[0] = 0x80;
            rawPacket[1] = 98;

            // 1. Ingest 4 packets to exhaust the pool
            for (int i = 0; i < poolCapacity; i++)
            {
                rawPacket[2] = (byte)(i >> 8);
                rawPacket[3] = (byte)(i & 0xFF);
                pipeline.ProcessDatagram(rawPacket);
            }

            pipeline.BufferPool.FreeCount.Should().Be(0);
            pipeline.BufferPool.InFlightCount.Should().Be(poolCapacity);
            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();

            // Pool is fully exhausted
            pipeline.BufferPool.TryRent(out int _, out _, out _).Should().BeFalse();

            // 2. Downstream native consumer dequeues a packet and decides to drop/discard it
            int dequeueRes = MoonshineNativeMethods.SpscDequeue(spscHandle, out var discardedDesc);
            dequeueRes.Should().Be(1);
            int discardedSlot = discardedDesc.BufferSlotIndex;
            discardedSlot.Should().BeInRange(0, poolCapacity - 1);

            // 3. Consumer returns slot index to return queue upon discarding
            int enqueueRet = MoonshineNativeMethods.SlotReturnEnqueue(returnQueue, discardedSlot);
            enqueueRet.Should().Be(1);

            // Prior to TryRent draining, slot is still in return queue and marked InFlight
            pipeline.BufferPool.FreeCount.Should().Be(0);

            // 4. TryRent is called by subsequent ingestion: drains return ring, reclaims slot to Free, and rents it
            bool rentSuccess = pipeline.BufferPool.TryRent(out int reallocatedSlot, out _, out _);
            rentSuccess.Should().BeTrue();
            reallocatedSlot.Should().Be(discardedSlot);

            // Invariant & state assertions
            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();
            pipeline.BufferPool.FreeCount.Should().Be(0);
            pipeline.BufferPool.RentedCount.Should().Be(1);
            pipeline.BufferPool.InFlightCount.Should().Be(poolCapacity - 1);

            // 5. Clean up remaining items
            pipeline.BufferPool.ReturnRented(reallocatedSlot);
            while (MoonshineNativeMethods.SpscDequeue(spscHandle, out var remaining) != 0)
            {
                int cleanupRet = MoonshineNativeMethods.SlotReturnEnqueue(returnQueue, remaining.BufferSlotIndex);
                cleanupRet.Should().Be(1);
            }

            // Drain return queue
            pipeline.BufferPool.TryRent(out int tempSlot, out _, out _);
            if (tempSlot >= 0) pipeline.BufferPool.ReturnRented(tempSlot);

            pipeline.BufferPool.FreeCount.Should().Be(poolCapacity);
            pipeline.BufferPool.RentedCount.Should().Be(0);
            pipeline.BufferPool.InFlightCount.Should().Be(0);
            pipeline.BufferPool.ValidateInvariant().Should().BeTrue();
            pipeline.BufferPool.AssertQuiescent();
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(spscHandle);
        }
    }

    [Fact]
    public unsafe void UdpSocketPipeline_ProcessDatagram_WithNvVideoHeader_PopulatesExactFramingMetadata()
    {
        MoonshinePacketDesc receivedDesc = default;
        bool callbackFired = false;

        using var pipeline = new UdpSocketPipeline(
            localPort: 0,
            packetCallback: desc =>
            {
                receivedDesc = desc;
                callbackFired = true;
            },
            parseGameStreamVideoHeaders: true
        );

        // 12-byte RTP header + 4-byte reserved area + 16-byte NV_VIDEO_PACKET + 100-byte payload = 132 bytes
        byte[] rawPacket = new byte[132];
        rawPacket[0] = 0x80; // V=2
        rawPacket[1] = 98; // HEVC
        rawPacket[2] = 0x00; // Seq = 1
        rawPacket[3] = 0x01;
        rawPacket[4] = 0x00; // Timestamp = 1000
        rawPacket[5] = 0x00;
        rawPacket[6] = 0x03;
        rawPacket[7] = 0xE8;

        // Four reserved bytes follow RTP. NV_VIDEO_PACKET begins at offset 16.
        BitConverter.TryWriteBytes(rawPacket.AsSpan(16, 4), 0x00000200u); // Stream packet index = 2
        BitConverter.TryWriteBytes(rawPacket.AsSpan(20, 4), 500u);        // Frame index = 500
        rawPacket[24] = 0x06;                                             // Start | End
        rawPacket[32] = 0xAA; // First video payload byte

        pipeline.ProcessDatagram(rawPacket);

        callbackFired.Should().BeTrue();
        receivedDesc.FrameIndex.Should().Be(500);
        receivedDesc.PacketIndex.Should().Be(2);
        receivedDesc.StreamPacketIndex.Should().Be(2);
        receivedDesc.TotalPackets.Should().Be(0);
        receivedDesc.Flags.Should().Be(0x03);
        receivedDesc.PayloadSize.Should().Be(100);
        (*receivedDesc.PayloadPtr).Should().Be(0xAA);
    }

    [Fact]
    public unsafe void UdpSocketPipeline_DelayedNativeConsumerShutdown_SafelyRecyclesSlots()
    {
        const int poolCapacity = 32;
        IntPtr spscHandle = MoonshineNativeMethods.SpscCreate((nuint)poolCapacity);
        spscHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            IntPtr returnQueue = IntPtr.Zero;
            Thread? consumerThread = null;
            int returnedSlots = 0;
            var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle,
                poolSlotCount: poolCapacity,
                nativeConsumerStopAndJoin: () => consumerThread!.Join()
            );

            returnQueue = pipeline.ReturnQueueHandle;

            // Ingest 10 datagrams
            for (int i = 0; i < 10; i++)
            {
                byte[] rawPacket = new byte[100];
                rawPacket[0] = 0x80;
                rawPacket[1] = 98;
                rawPacket[2] = 0x00;
                rawPacket[3] = (byte)i;
                pipeline.ProcessDatagram(rawPacket);
            }

            pipeline.BufferPool.InFlightCount.Should().Be(10);

            // Simulate dedicated native consumer thread draining and recycling with delay
            consumerThread = new Thread(() =>
            {
                Thread.Sleep(20);
                while (MoonshineNativeMethods.SpscDequeue(spscHandle, out var desc) != 0)
                {
                    int ret = MoonshineNativeMethods.SlotReturnEnqueue(returnQueue, desc.BufferSlotIndex);
                    ret.Should().Be(1);
                    Interlocked.Increment(ref returnedSlots);
                }
            });
            consumerThread.Start();

            // Dispose owns the stop-and-join barrier, return-ring drain, and quiescence assertion.
            pipeline.Dispose();
            returnedSlots.Should().Be(10);
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(spscHandle);
        }
    }
}
#endif
