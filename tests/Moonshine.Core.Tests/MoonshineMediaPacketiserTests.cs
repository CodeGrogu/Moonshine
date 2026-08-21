using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineMediaPacketiserTests
{
    [Fact]
    public void VideoPacketiser_CalculatePacketCount_MatchesExpected()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        packetiser.CalculatePacketCount(0).Should().Be(0);
        packetiser.CalculatePacketCount(500).Should().Be(1);
        packetiser.CalculatePacketCount(1000).Should().Be(1);
        packetiser.CalculatePacketCount(1001).Should().Be(2);
        packetiser.CalculatePacketCount(2500).Should().Be(3);
    }

    [Fact]
    public void VideoPacketiser_SingleSlice_EmitsValidDatagram()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1188);
        byte[] payload = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E];

        int datagramsReceived = 0;
        int bytesReceived = 0;

        packetiser.PacketiseFrame(payload, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, datagram =>
        {
            datagramsReceived++;
            bytesReceived = datagram.Length;
            datagram.Length.Should().Be(MoonshineVideoPacketiser.TotalHeaderOverhead + payload.Length);
        });

        datagramsReceived.Should().Be(1);
        bytesReceived.Should().Be(MoonshineVideoPacketiser.TotalHeaderOverhead + payload.Length);
    }

    [Fact]
    public void VideoPacketiser_MultiSliceWithVariableTail_EmitsExactTotalBytes()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        byte[] frameData = new byte[2450]; // 1000 + 1000 + 450 (3 slices)
        for (int i = 0; i < frameData.Length; i++) frameData[i] = (byte)(i & 0xFF);

        List<int> sliceSizes = new();
        int emitted = packetiser.PacketiseFrame(frameData, frameIndex: 5, timestampUs: 5000, isKeyframe: false, isHdr10: false, datagram =>
        {
            int payloadSize = datagram.Length - MoonshineVideoPacketiser.TotalHeaderOverhead;
            sliceSizes.Add(payloadSize);
        });

        emitted.Should().Be(3);
        sliceSizes.Should().Equal([1000, 1000, 450]);
    }

    [Fact]
    public void AudioPacketiser_PacketiseAudioFrame_EmitsExactPayload()
    {
        var packetiser = new MoonshineAudioPacketiser(streamId: 2, sessionId: 100, sampleRate: 48000, channels: 2);
        byte[] opusData = [0xFC, 0xFF, 0xFE, 0xFD, 0x01, 0x02, 0x03, 0x04];

        int emitted = packetiser.PacketiseAudioFrame(opusData, sampleIndex: 480, frameDurationUs: 10000, timestampUs: 10000, datagram =>
        {
            datagram.Length.Should().Be(MoonshineAudioPacketiser.TotalHeaderOverhead + opusData.Length);
        });

        emitted.Should().Be(1);
    }

    [Fact]
    public void VideoPacketiser_HotPath_ZeroGCAllocations()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1188);
        byte[] frameData = new byte[3500];

        VideoPacketSink sink = static _ => { };

        // Warm up
        packetiser.PacketiseFrame(frameData, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, sink);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            packetiser.PacketiseFrame(frameData, frameIndex: (ulong)(i + 2), timestampUs: (ulong)(i * 16666), isKeyframe: false, isHdr10: false, sink);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0, "Video packetiser steady-state hot path must have zero GC allocations");
    }
}
