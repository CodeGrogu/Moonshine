using System;
using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

[Collection("HardwareExclusive")]
public class VirtualAudioIpcBridgePipelineTests
{
    [Fact]
    public void VirtualAudioIpcBridgePipeline_CreateAndDispose_ExecutesCleanly()
    {
        using var pipeline = new VirtualAudioIpcBridgePipeline(isHostServer: true, sampleRate: 48000, channels: 2);
        pipeline.SampleRate.Should().Be(48000);
        pipeline.Channels.Should().Be(2);
        pipeline.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void VirtualAudioIpcBridgePipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new VirtualAudioIpcBridgePipeline(isHostServer: true, sampleRate: 48000, channels: 2);
        pipeline.Dispose();
        var act = () => pipeline.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void VirtualAudioIpcBridgePipeline_WriteAndReadPcm_ExecutesWithoutExceptions()
    {
        using var pipeline = new VirtualAudioIpcBridgePipeline(isHostServer: true, sampleRate: 48000, channels: 2);

        float[] micPcm = new float[960];
        Array.Fill(micPcm, 0.25f);
        int written = pipeline.WriteCapturePcm(micPcm);
        written.Should().Be(960);

        float[] renderPcm = new float[960];
        int read = pipeline.ReadRenderPcm(renderPcm, waitEvent: false, timeoutMs: 10);
        read.Should().Be(0); // Unpumped render channel underruns safely and zero pads

        bool ok = pipeline.TryGetMetrics(out var metrics);
        ok.Should().BeTrue();
        metrics.CapturePacketsWritten.Should().BeGreaterThan(0);
        metrics.RenderUnderruns.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VirtualAudioIpcBridgePipeline_ApplyCrossfade_RampsGainSmoothly()
    {
        float[] outgoing = new float[100];
        float[] incoming = new float[100];
        float[] dest = new float[100];

        Array.Fill(outgoing, 1.0f);
        Array.Fill(incoming, 2.0f);

        VirtualAudioIpcBridgePipeline.ApplyCrossfade(outgoing, incoming, dest);

        // At index 0, dest is outgoing (1.0)
        dest[0].Should().BeApproximately(1.0f, 0.05f);

        // At index 99, dest is close to incoming (2.0)
        dest[99].Should().BeApproximately(2.0f, 0.05f);

        // Midpoint should be between 1.0 and 2.0
        dest[50].Should().BeInRange(1.0f, 2.0f);
    }
}
