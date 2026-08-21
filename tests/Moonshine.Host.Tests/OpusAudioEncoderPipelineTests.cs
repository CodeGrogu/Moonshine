using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

public class OpusAudioEncoderPipelineTests
{
    [Fact]
    public void OpusAudioEncoderPipeline_Stereo_EncodesValidOpusFrame()
    {
        using var pipeline = new OpusAudioEncoderPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bitrate: 160000,
            frameDurationMs: 5,
            complexity: 8,
            useVbr: true
        );

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(2);
        pipeline.Topology.Should().Be(AudioChannelTopology.Stereo);
        pipeline.Bitrate.Should().Be(160000);
        pipeline.FrameDurationMs.Should().Be(5);
        pipeline.Complexity.Should().Be(8);
        pipeline.UseVbr.Should().BeTrue();
        pipeline.StreamsCount.Should().Be(1);
        pipeline.IsActive.Should().BeTrue();

        Span<float> pcm = stackalloc float[480]; // 240 * 2
        pcm.Fill(0.33f);

        Span<byte> payload = stackalloc byte[1024];
        bool ok = pipeline.TryEncode(pcm, 240, payload, out int bytesWritten);

        ok.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(0);

        pipeline.GetMetrics(out ulong frames, out ulong bytes, out double avgUs, out uint curBitrate, out uint streams);
        frames.Should().Be(1);
        bytes.Should().Be((ulong)bytesWritten);
        curBitrate.Should().Be(160000);
        streams.Should().Be(1);
    }

    [Fact]
    public void OpusAudioEncoderPipeline_Surround51_EncodesMultiStreamFrame()
    {
        using var pipeline = new OpusAudioEncoderPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Surround51,
            bitrate: 256000,
            frameDurationMs: 10
        );

        pipeline.Channels.Should().Be(6);
        pipeline.StreamsCount.Should().Be(4);

        Span<short> pcm16 = stackalloc short[2880]; // 480 * 6
        pcm16.Fill(1000);

        Span<byte> payload = stackalloc byte[2048];
        bool ok = pipeline.TryEncodePcm16(pcm16, 480, payload, out int bytesWritten);

        ok.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OpusAudioEncoderPipeline_Surround71_EncodesMultiStreamFrame()
    {
        using var pipeline = new OpusAudioEncoderPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Surround71,
            bitrate: 450000,
            frameDurationMs: 5
        );

        pipeline.Channels.Should().Be(8);
        pipeline.StreamsCount.Should().Be(6);

        Span<float> pcm = stackalloc float[1920]; // 240 * 8
        pcm.Fill(0.1f);

        Span<byte> payload = stackalloc byte[2048];
        bool ok = pipeline.TryEncode(pcm, 240, payload, out int bytesWritten);

        ok.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OpusAudioEncoderPipeline_DynamicBitrateAndComplexity_UpdatesMetrics()
    {
        using var pipeline = new OpusAudioEncoderPipeline(48000, AudioChannelTopology.Stereo, 128000);

        pipeline.SetBitrate(256000).Should().BeTrue();
        pipeline.Bitrate.Should().Be(256000);

        pipeline.SetComplexity(10).Should().BeTrue();
        pipeline.Complexity.Should().Be(10);
    }

    [Fact]
    public void OpusAudioEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new OpusAudioEncoderPipeline();
        pipeline.Dispose();
        pipeline.Dispose();

        Span<float> pcm = stackalloc float[128];
        Span<byte> payload = stackalloc byte[128];
        pipeline.TryEncode(pcm, 64, payload, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }
}
