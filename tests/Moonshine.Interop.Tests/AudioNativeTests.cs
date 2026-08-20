using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class AudioNativeTests
{
    [Fact]
    public unsafe void AudioCreateWasapi_StereoExclusive_ReturnsNonNullHandle()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCreateWasapi(48000, 2, 1);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] samples = new float[256];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.5f;
            }

            fixed (float* ptr = samples)
            {
                int submitRes = MoonshineNativeMethods.AudioSubmitPcm(handle, ptr, 128);
                submitRes.Should().Be(0);
            }

            MoonshineNativeMethods.AudioGetMetrics(handle, out ulong rendered, out uint underruns);
            rendered.Should().Be(128);
            underruns.Should().Be(0);
        }
        finally
        {
            MoonshineNativeMethods.AudioDestroy(handle);
        }
    }

    [Fact]
    public unsafe void AudioCreateWasapi_Surround51_ReturnsNonNullHandle()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCreateWasapi(48000, 6, 1);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] samples = new float[600];
            fixed (float* ptr = samples)
            {
                int submitRes = MoonshineNativeMethods.AudioSubmitPcm(handle, ptr, 100);
                submitRes.Should().Be(0);
            }

            MoonshineNativeMethods.AudioGetMetrics(handle, out ulong rendered, out _);
            rendered.Should().Be(100);
        }
        finally
        {
            MoonshineNativeMethods.AudioDestroy(handle);
        }
    }

    [Fact]
    public unsafe void AudioCreateWasapi_Surround71_ReturnsNonNullHandle()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCreateWasapi(48000, 8, 1);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] samples = new float[800];
            fixed (float* ptr = samples)
            {
                int submitRes = MoonshineNativeMethods.AudioSubmitPcm(handle, ptr, 100);
                submitRes.Should().Be(0);
            }

            MoonshineNativeMethods.AudioGetMetrics(handle, out ulong rendered, out _);
            rendered.Should().Be(100);
        }
        finally
        {
            MoonshineNativeMethods.AudioDestroy(handle);
        }
    }

    [Fact]
    public unsafe void AudioSubmitPcm_NullBuffer_ReturnsFailure()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCreateWasapi(48000, 2, 0);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int submitRes = MoonshineNativeMethods.AudioSubmitPcm(handle, null, 100);
            submitRes.Should().NotBe(0);
        }
        finally
        {
            MoonshineNativeMethods.AudioDestroy(handle);
        }
    }
}
