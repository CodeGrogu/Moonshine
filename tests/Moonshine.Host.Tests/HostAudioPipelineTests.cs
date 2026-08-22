using System.Buffers.Binary;
using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Host.Audio;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostAudioPipelineTests
{
    [Fact]
    public void HostAudioPipeline_Initialisation_PropertiesMatch()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5
        );

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Topology.Should().Be(AudioChannelTopology.Stereo);
        pipeline.Channels.Should().Be(2);
        pipeline.Bitrate.Should().Be(160000);
        pipeline.FrameDurationMs.Should().Be(5);
        pipeline.IsRunning.Should().BeFalse();
        pipeline.ActiveBackend.Should().NotBe(HostAudioBackend.Disabled);
    }

    [Fact]
    public void HostAudioPipeline_WasapiFallback_OperatesSeamlessly()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5,
            forceWasapiFallback: true
        );

        pipeline.ActiveBackend.Should().Be(HostAudioBackend.WasapiLoopbackFallback);

        bool packetEmitted = false;
        bool ok = pipeline.ProcessNextAudioFrame(datagram =>
        {
            packetEmitted = true;
            datagram.Length.Should().BeGreaterThan(MoonshineAudioPacketiser.TotalHeaderOverhead);
        }, preferMoonshineFraming: true);

        ok.Should().BeTrue();
        packetEmitted.Should().BeTrue();
    }

    [Fact]
    public void HostAudioPipeline_ProcessNextAudioFrame_MoonshineFraming_EmitsValidDatagram()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5,
            streamId: 42,
            sessionId: 0xDEADBEEFCAFE
        );

        int packetsReceived = 0;
        int lastPacketLength = 0;
        uint lastMagic = 0;
        uint lastStreamId = 0;

        AudioPacketSink sink = datagram =>
        {
            packetsReceived++;
            lastPacketLength = datagram.Length;

            // Header 32 bytes: Magic at offset 0
            lastMagic = BinaryPrimitives.ReadUInt32BigEndian(datagram[..4]);

            // Audio Header at offset 32: StreamId at offset 32 (4 bytes)
            lastStreamId = BinaryPrimitives.ReadUInt32BigEndian(datagram.Slice(32, 4));
        };

        bool ok = pipeline.ProcessNextAudioFrame(sink, preferMoonshineFraming: true);

        ok.Should().BeTrue();
        packetsReceived.Should().Be(1);
        lastPacketLength.Should().BeGreaterThan(56);
        lastMagic.Should().Be(MoonshineProtocolConstants.Magic);
        lastStreamId.Should().Be(42);

        var metrics = pipeline.Metrics;
        metrics.TotalFramesCaptured.Should().Be(1);
        metrics.TotalFramesEncoded.Should().Be(1);
        metrics.TotalPacketsEmitted.Should().Be(1);
    }

    [Fact]
    public void HostAudioPipeline_ProcessNextAudioFrame_RtpFraming_EmitsValidRtpPacket()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5
        );

        int packetsReceived = 0;
        byte firstByte = 0;
        byte payloadType = 0;

        AudioPacketSink sink = datagram =>
        {
            packetsReceived++;
            firstByte = datagram[0];
            payloadType = (byte)(datagram[1] & 0x7F);
        };

        bool ok = pipeline.ProcessNextAudioFrame(sink, preferMoonshineFraming: false);

        ok.Should().BeTrue();
        packetsReceived.Should().Be(1);
        firstByte.Should().Be(0x80); // RTP v2
        payloadType.Should().Be(97); // Opus PT
    }

    [Theory]
    [InlineData(AudioChannelTopology.Surround51, 6u, 256000u)]
    [InlineData(AudioChannelTopology.Surround71, 8u, 450000u)]
    public void HostAudioPipeline_MultiChannel_Surround51And71_EncodesAndEmits(
        AudioChannelTopology topology,
        uint expectedChannels,
        uint bitrate)
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: topology,
            bitrate: bitrate,
            frameDurationMs: 5
        );

        pipeline.Channels.Should().Be(expectedChannels);

        bool packetEmitted = false;
        bool ok = pipeline.ProcessNextAudioFrame(datagram =>
        {
            packetEmitted = true;
            datagram.Length.Should().BeGreaterThan(MoonshineAudioPacketiser.TotalHeaderOverhead);
        }, preferMoonshineFraming: true);

        ok.Should().BeTrue();
        packetEmitted.Should().BeTrue();
    }

    [Fact]
    public void HostAudioPipeline_ReconfigureBitrate_UpdatesActiveBitrate()
    {
        using var pipeline = new MoonshineHostAudioPipeline(bitrate: 160000);
        pipeline.Bitrate.Should().Be(160000);

        pipeline.ReconfigureBitrate(320000);
        pipeline.Bitrate.Should().Be(320000);
    }

    [Fact]
    public void HostAudioPipeline_StartAndStop_BackgroundWorkerLifecycle()
    {
        using var pipeline = new MoonshineHostAudioPipeline(frameDurationMs: 5);

        int packetCount = 0;
        AudioPacketSink sink = _ => Interlocked.Increment(ref packetCount);

        pipeline.Start(sink, preferMoonshineFraming: true).Should().BeTrue();
        pipeline.IsRunning.Should().BeTrue();

        Thread.Sleep(30); // Let 5-6 audio frames process

        pipeline.Stop();
        pipeline.IsRunning.Should().BeFalse();

        packetCount.Should().BeGreaterThan(0);
        pipeline.Metrics.TotalPacketsEmitted.Should().Be((ulong)packetCount);
    }

    [Fact]
    public void HostAudioPipeline_ZeroGCAllocations_SteadyStateHotPath()
    {
        using var pipeline = new MoonshineHostAudioPipeline(frameDurationMs: 5);

        AudioPacketSink sink = datagram =>
        {
            _ = datagram.Length;
        };

        // Warmup
        for (int i = 0; i < 20; i++)
        {
            pipeline.ProcessNextAudioFrame(sink, preferMoonshineFraming: true);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 500; i++)
        {
            pipeline.ProcessNextAudioFrame(sink, preferMoonshineFraming: true);
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        (allocatedAfter - allocatedBefore).Should().Be(0);
    }

    [Fact]
    public void HostAudioPipeline_DoubleDispose_IsSafeAndIdempotent()
    {
        var pipeline = new MoonshineHostAudioPipeline();
        pipeline.Dispose();
        pipeline.Dispose();

        pipeline.ActiveBackend.Should().Be(HostAudioBackend.Disabled);
        pipeline.IsRunning.Should().BeFalse();

        Action act = () => pipeline.ProcessNextAudioFrame(_ => { });
        act.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(AudioChannelTopology.Mono, 1)]
    [InlineData(AudioChannelTopology.Stereo, 2)]
    [InlineData(AudioChannelTopology.Surround51, 6)]
    [InlineData(AudioChannelTopology.Surround71, 8)]
    public void HostAudioPipeline_ProcessPcmFrame_ValidBuffers_EncodesAndEmitsAcrossTopologies(AudioChannelTopology topology, int channels)
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: topology,
            bitrate: 160000,
            frameDurationMs: 5
        );

        int totalSamples = 240 * channels;
        var pcm = new float[totalSamples];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        bool packetEmitted = false;
        bool ok = pipeline.ProcessPcmFrame(pcm, datagram =>
        {
            packetEmitted = true;
            datagram.Length.Should().BeGreaterThan(56);
        }, preferMoonshineFraming: true);

        ok.Should().BeTrue();
        packetEmitted.Should().BeTrue();
    }

    [Fact]
    public void HostAudioPipeline_ProcessPcmFrame_TruncatedBuffers_FailsClosedSafely()
    {
        using var pipeline = new MoonshineHostAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5
        );

        // Required: 240 samples * 2 channels = 480 floats. Provide truncated buffer of 100 floats.
        var truncatedPcm = new float[100];
        bool packetEmitted = false;

        bool ok = pipeline.ProcessPcmFrame(truncatedPcm, _ => packetEmitted = true, preferMoonshineFraming: true);

        ok.Should().BeFalse();
        packetEmitted.Should().BeFalse();
    }
}
