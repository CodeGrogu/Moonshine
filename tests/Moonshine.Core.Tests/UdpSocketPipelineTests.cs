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
            using var pipeline = new UdpSocketPipeline(
                localPort: 0,
                nativeSpscHandle: spscHandle
            );

            byte[] rawPacket = new byte[100];
            rawPacket[0] = 0x80;
            rawPacket[1] = 96; // H.264
            rawPacket[2] = 0x00;
            rawPacket[3] = 0x05; // Seq = 5

            pipeline.ProcessDatagram(rawPacket);

            nuint size = MoonshineNativeMethods.SpscSize(spscHandle);
            size.Should().Be(1);

            int dequeueResult = MoonshineNativeMethods.SpscDequeue(spscHandle, out var dequeuedPacket);
            dequeueResult.Should().Be(1);
            dequeuedPacket.SequenceNumber.Should().Be(5);
            dequeuedPacket.PayloadSize.Should().Be(100 - 12);
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
}
