using FluentAssertions;
using Moonshine.Core.Audio;
using Xunit;

namespace Moonshine.Core.Tests;

public class AudioPipelineTests
{
    [Fact]
    public void MoonshineAudioPipeline_StereoExclusive_InitializesAndSubmitsPcm()
    {
        using var pipeline = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Stereo, isExclusive: true);

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(AudioChannelConfiguration.Stereo);
        pipeline.IsExclusive.Should().BeTrue();

        float[] pcm = new float[512];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0.25f;
        }

        bool success = pipeline.SubmitPcm(pcm);
        success.Should().BeTrue();

        pipeline.Metrics.FramesSubmitted.Should().Be(256);
        pipeline.Metrics.FramesRendered.Should().Be(256);
        pipeline.Metrics.BufferUnderruns.Should().Be(0);
    }

    [Fact]
    public void MoonshineAudioPipeline_Surround51_InitializesAndSubmitsPcm()
    {
        using var pipeline = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Surround51, isExclusive: true);

        float[] pcm = new float[600];
        bool success = pipeline.SubmitPcm(pcm);
        success.Should().BeTrue();

        pipeline.Metrics.FramesSubmitted.Should().Be(100);
        pipeline.Metrics.FramesRendered.Should().Be(100);
    }

    [Fact]
    public void MoonshineAudioPipeline_Surround71_InitializesAndSubmitsPcm()
    {
        using var pipeline = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Surround71, isExclusive: true);

        float[] pcm = new float[800];
        bool success = pipeline.SubmitPcm(pcm);
        success.Should().BeTrue();

        pipeline.Metrics.FramesSubmitted.Should().Be(100);
        pipeline.Metrics.FramesRendered.Should().Be(100);
    }

    [Fact]
    public void MoonshineAudioPipeline_SubmitEmptySpan_ReturnsFalse()
    {
        using var pipeline = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Stereo, isExclusive: false);

        bool success = pipeline.SubmitPcm(ReadOnlySpan<float>.Empty);
        success.Should().BeFalse();
    }

    [Fact]
    public void MoonshineAudioPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new MoonshineAudioPipeline(48000, AudioChannelConfiguration.Stereo, isExclusive: false);
        pipeline.Dispose();
        pipeline.Dispose();
    }
}
