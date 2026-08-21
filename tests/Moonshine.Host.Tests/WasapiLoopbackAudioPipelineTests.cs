using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

public class WasapiLoopbackAudioPipelineTests
{
    [Fact]
    public void WasapiLoopbackAudioPipeline_Initialize_PropertiesMatch()
    {
        using var pipeline = new WasapiLoopbackAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Surround51,
            bufferDurationMs: 10
        );

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(6);
        pipeline.Topology.Should().Be(AudioChannelTopology.Surround51);
        pipeline.BufferDurationMs.Should().Be(10);
        pipeline.IsActive.Should().BeTrue();
    }

    [Fact]
    public void WasapiLoopbackAudioPipeline_TryReadSamples_ReadsValidData()
    {
        using var pipeline = new WasapiLoopbackAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Stereo,
            bufferDurationMs: 5
        );

        Span<float> buffer = stackalloc float[480];
        bool ok = pipeline.TryReadSamples(buffer, out int samplesRead, out ulong timestampQpc);

        ok.Should().BeTrue();
        samplesRead.Should().Be(480);
        timestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WasapiLoopbackAudioPipeline_TryReadSamplesPcm16_ReadsValidData()
    {
        using var pipeline = new WasapiLoopbackAudioPipeline(
            sampleRate: 48000,
            topology: AudioChannelTopology.Surround71,
            bufferDurationMs: 5
        );

        Span<short> buffer = stackalloc short[1920]; // 240 * 8
        bool ok = pipeline.TryReadSamplesPcm16(buffer, out int samplesRead, out ulong timestampQpc);

        ok.Should().BeTrue();
        samplesRead.Should().Be(1920);
        timestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WasapiLoopbackAudioPipeline_Metrics_TrackAccurateCounts()
    {
        using var pipeline = new WasapiLoopbackAudioPipeline(48000, AudioChannelTopology.Stereo, 5);

        Span<float> buffer = stackalloc float[480];
        pipeline.TryReadSamples(buffer, out _, out _).Should().BeTrue();
        pipeline.TryReadSamples(buffer, out _, out _).Should().BeTrue();

        pipeline.GetMetrics(out ulong frames, out ulong samples, out uint underruns, out uint overruns);
        frames.Should().Be(2);
        samples.Should().Be(480);
        underruns.Should().Be(0);
        overruns.Should().Be(0);
    }

    [Fact]
    public void WasapiLoopbackAudioPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new WasapiLoopbackAudioPipeline();
        pipeline.Dispose();
        pipeline.Dispose();

        Span<float> buffer = stackalloc float[128];
        pipeline.TryReadSamples(buffer, out _, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }
}
