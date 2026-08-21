using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class WasapiCaptureNativeTests
{
    [Theory]
    [InlineData(2u)] // Stereo
    [InlineData(6u)] // 5.1 Surround
    [InlineData(8u)] // 7.1 Surround
    public unsafe void WasapiCapture_MultiChannel_ReadsExpectedChannelCounts(uint channels)
    {
        IntPtr handle = MoonshineNativeMethods.AudioCaptureCreate(48000, channels, 5);
        handle.Should().NotBe(IntPtr.Zero);

        int sampleCount = (int)(240 * channels);
        float[] buffer = new float[sampleCount];

        fixed (float* ptr = buffer)
        {
            int res = MoonshineNativeMethods.AudioCaptureReadFloat(
                handle,
                ptr,
                (uint)buffer.Length,
                out uint read,
                out ulong qpc
            );

            res.Should().Be(1);
            read.Should().Be((uint)sampleCount);
            qpc.Should().BeGreaterThan(0);
        }

        MoonshineNativeMethods.AudioCaptureDestroy(handle);
    }

    [Fact]
    public unsafe void WasapiCapture_ReadFloatAndPcm16_PopulatesValidSamples()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCaptureCreate(48000, 2, 5);
        handle.Should().NotBe(IntPtr.Zero);

        short[] pcm16Buffer = new short[480];
        fixed (short* ptr = pcm16Buffer)
        {
            int res = MoonshineNativeMethods.AudioCaptureReadPcm16(
                handle,
                ptr,
                (uint)pcm16Buffer.Length,
                out uint read,
                out ulong qpc
            );

            res.Should().Be(1);
            read.Should().Be(480);
            qpc.Should().BeGreaterThan(0);
        }

        MoonshineNativeMethods.AudioCaptureGetMetrics(
            handle,
            out ulong frames,
            out ulong samples,
            out uint underruns,
            out uint overruns
        );

        frames.Should().Be(1);
        samples.Should().Be(240);
        underruns.Should().Be(0);
        overruns.Should().Be(0);

        MoonshineNativeMethods.AudioCaptureDestroy(handle);
    }
}
