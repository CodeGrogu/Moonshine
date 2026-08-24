using FluentAssertions;
using Moonshine.Host.Encoding;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class HardwareVideoEncoderPipelineTests
{
    [Fact]
    public void HardwareVideoEncoderPipeline_InitializeWithoutDevice_FailsClosedSafely()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 120,
            bitrateKbps: 30000,
            codec: VideoCodec.Av1,
            vendor: EncoderVendor.Auto,
            d3dDevice: IntPtr.Zero
        );

        pipeline.Width.Should().Be(1920);
        pipeline.Height.Should().Be(1080);
        pipeline.Fps.Should().Be(120);
        pipeline.BitrateKbps.Should().Be(30000);
        pipeline.Codec.Should().Be(VideoCodec.Av1);
        pipeline.IsActive.Should().BeFalse();
        pipeline.ImplementationKind.Should().Be(EncoderImplementationKind.Unimplemented);
        pipeline.IsHardwareAccelerated.Should().BeFalse();
        pipeline.HasProducedValidOutput.Should().BeFalse();
        pipeline.ImplementationType.Should().Be<HardwareVideoEncoderPipeline>();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Faulted);
        pipeline.FramesEncoded.Should().Be(0);
        pipeline.EncodingErrorsCount.Should().Be(0);
        pipeline.AverageEncodingLatencyMicroseconds.Should().Be(0.0);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_EncodeAndSubmit_WithoutDevice_ReturnsFailClosed()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 3840,
            height: 2160,
            fps: 60,
            bitrateKbps: 50000,
            codec: VideoCodec.HevcMain10,
            d3dDevice: IntPtr.Zero
        );

        Span<byte> buffer = stackalloc byte[1024 * 512];
        bool success = pipeline.TryEncodeFrame(IntPtr.Zero, false, out var desc, buffer, out int written);
        success.Should().BeFalse();
        written.Should().Be(0);
        pipeline.HasProducedValidOutput.Should().BeFalse();

        var submission = pipeline.SubmitFrame(IntPtr.Zero, true, buffer, out int submitWritten);
        submission.Submitted.Should().BeFalse();
        submission.OutputAvailable.Should().BeFalse();
        submission.KeyFrame.Should().BeFalse();
        submission.BytesWritten.Should().Be(0);
        submission.Result.Should().Be(EncoderResult.NotAvailable);

        bool polled = pipeline.TryPollPacket(buffer, out _, out int polledWritten);
        polled.Should().BeFalse();
        polledWritten.Should().Be(0);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_ReconfigureAndKeyframe_WithoutHandle_OperatesSafely()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.HevcMain10,
            d3dDevice: IntPtr.Zero
        );

        bool reconf = pipeline.Reconfigure(60000, 120);
        reconf.Should().BeFalse();
        pipeline.RequestKeyframe();
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_DoubleDispose_IsSafe()
    {
        var pipeline = new HardwareVideoEncoderPipeline(1920, 1080, d3dDevice: IntPtr.Zero);
        pipeline.Dispose();
        pipeline.Dispose();
        pipeline.RuntimeState.Should().Be(EncoderRuntimeState.Disposed);

        Span<byte> buffer = stackalloc byte[256];
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _).Should().BeFalse();
        var result = pipeline.SubmitFrame(IntPtr.Zero, false, buffer, out _);
        result.Submitted.Should().BeFalse();
        result.Result.Should().Be(EncoderResult.DeviceLost);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_TryEncodeFrame_InvalidBitstreamPayload_ReturnsFalse()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(
            width: 1920,
            height: 1080,
            fps: 60,
            bitrateKbps: 20000,
            codec: VideoCodec.Av1,
            d3dDevice: IntPtr.Zero
        );

        Span<byte> emptyBuffer = stackalloc byte[0];
        bool success = pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, emptyBuffer, out int written);
        success.Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void HardwareVideoEncoderPipeline_ZeroAllocationsHotPath()
    {
        using var pipeline = new HardwareVideoEncoderPipeline(1920, 1080, d3dDevice: IntPtr.Zero);
        byte[] buffer = new byte[1024];

        // Warm up
        pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            pipeline.TryEncodeFrame(IntPtr.Zero, false, out _, buffer, out _);
        }
        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

        (afterAlloc - beforeAlloc).Should().Be(0, "HardwareVideoEncoderPipeline hot path must be zero allocation");
    }
}
