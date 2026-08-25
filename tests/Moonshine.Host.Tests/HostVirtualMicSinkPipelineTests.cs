using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

[Collection("HardwareExclusive")]
public class HostVirtualMicSinkPipelineTests
{
    private static byte[] GenerateRealOpusPacket(uint sampleRate, uint durationMs)
    {
        using var encoder = new OpusAudioEncoderPipeline(
            sampleRate: sampleRate,
            topology: AudioChannelTopology.Mono,
            bitrate: 64000,
            frameDurationMs: durationMs,
            complexity: 8,
            useVbr: true
        );

        int frameSamples = (int)(sampleRate * durationMs) / 1000;
        float[] pcm = new float[frameSamples];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = 0.3f * MathF.Sin(2.0f * MathF.PI * 440.0f * (i / (float)sampleRate));
        }

        byte[] payload = new byte[512];
        bool ok = encoder.TryEncode(pcm.AsSpan(), (uint)frameSamples, payload.AsSpan(), out int written);
        ok.Should().BeTrue();
        written.Should().BeGreaterThan(0);

        byte[] result = new byte[written];
        Array.Copy(payload, result, written);
        return result;
    }

    [Fact]
    public void HostVirtualMicSinkPipeline_PushOpusAndPullPcm_WorksCorrectly()
    {
        using var pipeline = new HostVirtualMicSinkPipeline(
            sampleRate: 48000,
            channels: 1,
            targetLatencyMs: 10,
            gainMultiplier: 1.0f,
            noiseGateThresholdDb: -50.0f,
            isMuted: false
        );

        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(1);
        pipeline.TargetLatencyMs.Should().Be(10);
        pipeline.IsInitialized.Should().BeTrue();

        byte[] opusPayload = GenerateRealOpusPacket(48000, 10);

        bool pushOk = pipeline.TryPushOpusPacket(opusPayload, timestamp: 480, sequenceNumber: 1);
        pushOk.Should().BeTrue();

        Span<float> pcmOut = stackalloc float[480];
        bool pullOk = pipeline.TryPullPcm(pcmOut, out int samplesRead);

        pullOk.Should().BeTrue();
        samplesRead.Should().Be(480);

        HostMicSinkMetrics metrics = pipeline.GetMetrics();
        metrics.TotalPacketsReceived.Should().Be(1);
        metrics.TotalSamplesRendered.Should().Be(480);
        metrics.LossCount.Should().Be(0);
    }

    [Fact]
    public void HostVirtualMicSinkPipeline_MuteAndGainControl_OperatesAsExpected()
    {
        using var pipeline = new HostVirtualMicSinkPipeline(48000, 1, 10);

        pipeline.SetGain(2.5f);
        pipeline.SetMute(true);

        byte[] opusPayload = GenerateRealOpusPacket(48000, 10);
        pipeline.TryPushOpusPacket(opusPayload, 480, 1);

        Span<float> pcmOut = stackalloc float[480];
        pipeline.TryPullPcm(pcmOut, out int samplesRead);

        samplesRead.Should().Be(480);
        foreach (float val in pcmOut)
        {
            val.Should().Be(0.0f);
        }

        pipeline.SetMute(false);
        pipeline.TryPushOpusPacket(opusPayload, 960, 2);
        pipeline.TryPullPcm(pcmOut, out samplesRead);
        samplesRead.Should().Be(480);
    }

    [Fact]
    public void HostVirtualMicSinkPipeline_Disposed_ThrowsObjectDisposedException()
    {
        var pipeline = new HostVirtualMicSinkPipeline();
        pipeline.Dispose();
        pipeline.Dispose();

        pipeline.IsInitialized.Should().BeFalse();
        byte[] testPayload = new byte[16];
        Action act = () => pipeline.TryPushOpusPacket(testPayload, 0, 0);
        act.Should().Throw<ObjectDisposedException>();
    }
}
