using System;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class VirtualAudioIpcNativeTests
{
    [Fact]
    public void AudioIpcBridge_CreateAndDestroy_ExecutesCleanly()
    {
        IntPtr handle = MoonshineNativeMethods.AudioIpcBridgeCreate(1, 48000, 2);
        handle.Should().NotBe(IntPtr.Zero);

        int isConnected = MoonshineNativeMethods.AudioIpcBridgeIsConnected(handle);
        isConnected.Should().Be(1);

        MoonshineNativeMethods.AudioIpcBridgeDestroy(handle);
    }

    [Fact]
    public void AudioIpcBridge_GetMetrics_ReturnsValidInitialMetrics()
    {
        IntPtr handle = MoonshineNativeMethods.AudioIpcBridgeCreate(1, 48000, 2);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int res = MoonshineNativeMethods.AudioIpcBridgeGetMetrics(handle, out var metrics);
            res.Should().Be(1);
            metrics.SampleRate.Should().Be(48000);
            metrics.Channels.Should().Be(2);
            metrics.IsConnected.Should().Be(1);
            metrics.RenderUnderruns.Should().Be(0);
            metrics.CapturePacketsWritten.Should().Be(0);
        }
        finally
        {
            MoonshineNativeMethods.AudioIpcBridgeDestroy(handle);
        }
    }

    [Fact]
    public unsafe void AudioIpcBridge_WriteCaptureAndReadRender_ReturnsExpectedCounts()
    {
        IntPtr handle = MoonshineNativeMethods.AudioIpcBridgeCreate(1, 48000, 2);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] samples = new float[960]; // 10ms of stereo 48kHz audio
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.5f;
            }

            fixed (float* ptr = samples)
            {
                long written = MoonshineNativeMethods.AudioIpcBridgeWriteCapturePcm(handle, ptr, (uint)samples.Length);
                written.Should().Be(samples.Length);
            }

            float[] renderOut = new float[960];
            fixed (float* ptr = renderOut)
            {
                // In unpumped render buffer, underrun returns 0 and pads with silence
                long read = MoonshineNativeMethods.AudioIpcBridgeReadRenderPcm(handle, ptr, (uint)renderOut.Length, 0, 10);
                read.Should().Be(0);
            }

            MoonshineNativeMethods.AudioIpcBridgeGetMetrics(handle, out var metrics);
            metrics.CapturePacketsWritten.Should().BeGreaterThan(0);
            metrics.RenderUnderruns.Should().BeGreaterThan(0);
        }
        finally
        {
            MoonshineNativeMethods.AudioIpcBridgeDestroy(handle);
        }
    }

    [Fact]
    public void AudioIpcBridge_MmcssScheduling_ExecutesCleanly()
    {
        IntPtr handle = MoonshineNativeMethods.AudioIpcBridgeCreate(1, 48000, 2);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int ok = MoonshineNativeMethods.AudioIpcBridgeEnableMmcss(handle);
            (ok == 1 || ok == 0).Should().BeTrue();

            MoonshineNativeMethods.AudioIpcBridgeRevertMmcss(handle);
        }
        finally
        {
            MoonshineNativeMethods.AudioIpcBridgeDestroy(handle);
        }
    }
}
