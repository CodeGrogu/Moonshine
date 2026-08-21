using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostVirtualMicSinkPipelineTests
{
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

        Span<byte> opusPayload = stackalloc byte[64];
        opusPayload.Fill(160);

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

        Span<byte> opusPayload = stackalloc byte[64];
        opusPayload.Fill(180);
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
        byte[] dummy = new byte[16];
        Action act = () => pipeline.TryPushOpusPacket(dummy, 0, 0);
        act.Should().Throw<ObjectDisposedException>();
    }
}
