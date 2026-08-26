using FluentAssertions;
using Moonshine.Core.Audio;
using Xunit;

namespace Moonshine.Core.Tests;

public sealed class WasapiMicrophoneCapturePipelineTests
{
    [Fact]
    public void Pipeline_Initialises_WithDefaultParameters()
    {
        using var pipeline = new WasapiMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bufferDurationMs: 10
        );

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(1);
        pipeline.BufferDurationMs.Should().Be(10);
        pipeline.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_TryReadSamples_Mono_ReadsValidData()
    {
        using var pipeline = new WasapiMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bufferDurationMs: 10
        );

        Span<float> buffer = stackalloc float[480];
        bool ok = pipeline.TryReadSamples(buffer, out int samplesRead, out ulong timestampQpc);

        ok.Should().BeTrue();
        samplesRead.Should().BeGreaterThan(0);
        samplesRead.Should().BeLessThanOrEqualTo(480);
        timestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Pipeline_TryReadSamples_Stereo_ReadsValidData()
    {
        using var pipeline = new WasapiMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 2,
            bufferDurationMs: 10
        );

        Span<float> buffer = stackalloc float[960];
        bool ok = pipeline.TryReadSamples(buffer, out int samplesRead, out ulong timestampQpc);

        ok.Should().BeTrue();
        samplesRead.Should().BeGreaterThan(0);
        samplesRead.Should().BeLessThanOrEqualTo(960);
        timestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Pipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new WasapiMicrophoneCapturePipeline();
        pipeline.Dispose();
        pipeline.Dispose();

        Span<float> buffer = stackalloc float[480];
        pipeline.TryReadSamples(buffer, out _, out _).Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Pipeline_TryRecover_SucceedsOnActivePipeline()
    {
        using var pipeline = new WasapiMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bufferDurationMs: 10
        );

        pipeline.IsActive.Should().BeTrue();

        bool recovered = pipeline.TryRecover();
        recovered.Should().BeTrue();
        pipeline.IsActive.Should().BeTrue();

        Span<float> buffer = stackalloc float[480];
        bool ok = pipeline.TryReadSamples(buffer, out int samplesRead, out ulong timestampQpc);
        ok.Should().BeTrue();
        samplesRead.Should().BeGreaterThan(0);
        samplesRead.Should().BeLessThanOrEqualTo(480);
        timestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Pipeline_TryRecover_FailsAfterDispose()
    {
        var pipeline = new WasapiMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bufferDurationMs: 10
        );

        pipeline.Dispose();

        bool recovered = pipeline.TryRecover();
        recovered.Should().BeFalse();
        pipeline.IsActive.Should().BeFalse();
    }
}
