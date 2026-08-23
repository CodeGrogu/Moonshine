using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Core.Media;
using Moonshine.Interop;
#if MOONSHINE_LEGACY_INTEROP
using Moonshine.Protocol.RTP;
#endif
using Xunit;

namespace Moonshine.Core.Tests;

public sealed class ClientAudioPipelineTests
{
    private static unsafe byte[] CreateEncodedOpusFrame(
        uint sampleRate,
        uint channels,
        uint bitrate,
        uint frameDurationMs,
        ReadOnlySpan<float> pcmSamples)
    {
        IntPtr enc = MoonshineNativeMethods.OpusEncoderCreate(
            sampleRate,
            channels,
            bitrate,
            frameDurationMs,
            complexity: 8,
            useVbr: 1
        );
        enc.Should().NotBe(IntPtr.Zero);

        try
        {
            byte[] outBuf = new byte[2048];
            fixed (float* pcmPtr = pcmSamples)
            fixed (byte* outPtr = outBuf)
            {
                uint frameSamples = (uint)(pcmSamples.Length / (int)channels);
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(
                    enc,
                    pcmPtr,
                    frameSamples,
                    outPtr,
                    (uint)outBuf.Length,
                    out uint bytesWritten
                );
                res.Should().Be(1);
                bytesWritten.Should().BeGreaterThan(0);
                return outBuf[..(int)bytesWritten];
            }
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(enc);
        }
    }

    [Fact]
    public void OpusAudioDecoderPipeline_Stereo_DecodeFloat_MatchesSampleCount()
    {
        float[] pcmIn = new float[480];
        for (int i = 0; i < pcmIn.Length; i++)
        {
            pcmIn[i] = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * (i / 48000.0f));
        }

        byte[] compressed = CreateEncodedOpusFrame(48000, 2, 160000, 5, pcmIn);

        using var decoder = new OpusAudioDecoderPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo
        );

        float[] pcmOut = new float[480];
        bool decOk = decoder.DecodeFloat(compressed, pcmOut, out uint samplesDecoded);
        decOk.Should().BeTrue();
        samplesDecoded.Should().Be(480);

        var metrics = decoder.Metrics;
        metrics.TotalFramesDecoded.Should().Be(1);
        metrics.TotalSamplesDecoded.Should().Be(480);
        metrics.DecodeErrors.Should().Be(0);
        metrics.StreamsCount.Should().Be(1);
    }

    [Fact]
    public void AudioJitterBuffer_ReordersOutOfOrderPackets_Accurately()
    {
        var jitterBuffer = new AudioJitterBuffer(capacity: 16, maxPacketSize: 512);

        byte[] pkt1 = [1, 2, 3, 4];
        byte[] pkt2 = [5, 6, 7, 8];
        byte[] pkt3 = [9, 10, 11, 12];

        // Push out of sequence: pkt2 (seq 1), pkt1 (seq 0), pkt3 (seq 2)
        jitterBuffer.Push(sequence: 1, timestampQpc: 2000, pkt2).Should().BeTrue();
        jitterBuffer.Push(sequence: 0, timestampQpc: 1000, pkt1).Should().BeTrue();
        jitterBuffer.Push(sequence: 2, timestampQpc: 3000, pkt3).Should().BeTrue();

        byte[] popBuf = new byte[64];

        // Pop seq 0
        bool pop0 = jitterBuffer.Pop(popBuf, out int len0, out uint seq0, out ulong ts0);
        pop0.Should().BeTrue();
        seq0.Should().Be(0);
        ts0.Should().Be(1000);
        popBuf[..len0].Should().Equal(pkt1);

        // Pop seq 1
        bool pop1 = jitterBuffer.Pop(popBuf, out int len1, out uint seq1, out ulong ts1);
        pop1.Should().BeTrue();
        seq1.Should().Be(1);
        ts1.Should().Be(2000);
        popBuf[..len1].Should().Equal(pkt2);

        // Pop seq 2
        bool pop2 = jitterBuffer.Pop(popBuf, out int len2, out uint seq2, out ulong ts2);
        pop2.Should().BeTrue();
        seq2.Should().Be(2);
        ts2.Should().Be(3000);
        popBuf[..len2].Should().Equal(pkt3);

        // Buffer now empty -> underrun
        bool pop3 = jitterBuffer.Pop(popBuf, out _, out _, out _);
        pop3.Should().BeFalse();

        var metrics = jitterBuffer.Metrics;
        metrics.PacketsPushed.Should().Be(3);
        metrics.PacketsPopped.Should().Be(3);
        metrics.BufferUnderruns.Should().Be(1);
    }

    [Fact]
    public void ClientAudioPipeline_DirectFrameProcessing_Succeeds()
    {
        using var pipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        float[] pcmIn = new float[480];
        byte[] compressed = CreateEncodedOpusFrame(48000, 2, 160000, 5, pcmIn);

        bool processed = pipeline.ProcessDirectFrame(compressed);
        processed.Should().BeTrue();

        var metrics = pipeline.Metrics;
        metrics.FramesDecoded.Should().Be(1);
        metrics.FramesRendered.Should().Be(1);
        metrics.DecodeErrors.Should().Be(0);
    }

    [Fact]
    public void ClientAudioPipeline_IngestMoonshinePacket_ProcessesCleanly()
    {
        using var pipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        var packetiser = new MoonshineAudioPacketiser(streamId: 1, sessionId: 0x1234, sampleRate: 48000, channels: 2);

        float[] pcmIn = new float[480];
        byte[] compressed = CreateEncodedOpusFrame(48000, 2, 160000, 5, pcmIn);

        bool packetised = false;
        bool ingested = false;

        packetiser.PacketiseAudioFrame(
            compressed,
            sampleIndex: 0,
            frameDurationUs: 5000,
            timestampUs: 999999,
            sink: datagram =>
            {
                packetised = true;
                ingested = pipeline.IngestMoonshinePacket(datagram);
            }
        );

        packetised.Should().BeTrue();
        ingested.Should().BeTrue();
        pipeline.Metrics.PacketsReceived.Should().Be(1);
    }

#if MOONSHINE_LEGACY_INTEROP
    [Fact]
    public void ClientAudioPipeline_IngestRtpPacket_ProcessesCleanly()
    {
        using var pipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        float[] pcmIn = new float[480];
        byte[] compressed = CreateEncodedOpusFrame(48000, 2, 160000, 5, pcmIn);

        byte[] rtpPkt = new byte[RtpAudioHeader.Size + compressed.Length];
        rtpPkt[0] = 0x80; // V=2
        rtpPkt[1] = 97; // PT=97
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(rtpPkt.AsSpan(2, 2), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(rtpPkt.AsSpan(4, 4), 480000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(rtpPkt.AsSpan(8, 4), 0x11223344);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(rtpPkt.AsSpan(12, 2), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(rtpPkt.AsSpan(14, 2), 1);
        compressed.CopyTo(rtpPkt.AsSpan(RtpAudioHeader.Size));

        bool ingested = pipeline.IngestRtpPacket(rtpPkt);
        ingested.Should().BeTrue();

        pipeline.Metrics.PacketsReceived.Should().Be(1);
    }
#endif

    [Fact]
    public void ClientAudioPipeline_ZeroGCAllocations_SteadyStateHotPath()
    {
        using var pipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        float[] pcmIn = new float[480];
        byte[] compressed = CreateEncodedOpusFrame(48000, 2, 160000, 5, pcmIn);

        // Warm up
        for (int i = 0; i < 50; i++)
        {
            pipeline.ProcessDirectFrame(compressed);
        }

        // Assert 0 GC allocations over 200 iterations
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 200; i++)
        {
            pipeline.ProcessDirectFrame(compressed);
        }

        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        (allocAfter - allocBefore).Should().Be(0, "steady-state client audio frame decoding and rendering must have zero heap allocations");
    }

    [Fact]
    public void ClientAudioPipeline_ReconfigureFormat_UpdatesPropertiesSafely()
    {
        using var pipeline = new MoonshineClientAudioPipeline(
            sampleRate: 48000,
            channels: AudioChannelConfiguration.Stereo,
            isExclusive: false,
            startBackgroundWorker: false
        );

        pipeline.Channels.Should().Be(AudioChannelConfiguration.Stereo);

        pipeline.ReconfigureFormat(48000, AudioChannelConfiguration.Surround51);
        pipeline.Channels.Should().Be(AudioChannelConfiguration.Surround51);

        pipeline.ReconfigureFormat(48000, AudioChannelConfiguration.Surround71);
        pipeline.Channels.Should().Be(AudioChannelConfiguration.Surround71);
    }
}
