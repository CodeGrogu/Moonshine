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

    [Fact]
    public unsafe void MicCapture_CreateAndRead_Mono_ReturnsValidPcm()
    {
        IntPtr handle = MoonshineNativeMethods.MicCaptureCreate(48000, 1, 10);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int isActive = MoonshineNativeMethods.MicCaptureIsActive(handle);
            isActive.Should().Be(1);

            float[] buffer = new float[480];
            fixed (float* ptr = buffer)
            {
                int readRes = MoonshineNativeMethods.MicCaptureReadFloat(
                    handle,
                    ptr,
                    (uint)buffer.Length,
                    out uint samplesRead,
                    out ulong timestampQpc
                );

                readRes.Should().Be(1);
                samplesRead.Should().BeGreaterThan(0u);
                samplesRead.Should().BeLessThanOrEqualTo(480u);
                timestampQpc.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.MicCaptureDestroy(handle);
        }
    }

    [Fact]
    public unsafe void MicCapture_CreateAndRead_Stereo_ReturnsValidPcm()
    {
        IntPtr handle = MoonshineNativeMethods.MicCaptureCreate(48000, 2, 10);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int isActive = MoonshineNativeMethods.MicCaptureIsActive(handle);
            isActive.Should().Be(1);

            float[] buffer = new float[960];
            fixed (float* ptr = buffer)
            {
                int readRes = MoonshineNativeMethods.MicCaptureReadFloat(
                    handle,
                    ptr,
                    (uint)buffer.Length,
                    out uint samplesRead,
                    out ulong timestampQpc
                );

                readRes.Should().Be(1);
                samplesRead.Should().BeGreaterThan(0u);
                samplesRead.Should().BeLessThanOrEqualTo(960u);
                timestampQpc.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.MicCaptureDestroy(handle);
        }
    }

    [Fact]
    public unsafe void MicCapture_InvalidHandle_ReturnsFailure()
    {
        float[] buffer = new float[480];
        fixed (float* ptr = buffer)
        {
            int readRes = MoonshineNativeMethods.MicCaptureReadFloat(
                IntPtr.Zero,
                ptr,
                (uint)buffer.Length,
                out uint samplesRead,
                out ulong timestampQpc
            );

            readRes.Should().Be(0);
            samplesRead.Should().Be(0);
        }

        int isActive = MoonshineNativeMethods.MicCaptureIsActive(IntPtr.Zero);
        isActive.Should().Be(0);

        int recoverRes = MoonshineNativeMethods.MicCaptureRecover(IntPtr.Zero);
        recoverRes.Should().Be(0);
    }

    [Fact]
    public unsafe void MicCapture_Recover_ValidHandle_ReturnsSuccess()
    {
        IntPtr handle = MoonshineNativeMethods.MicCaptureCreate(48000, 1, 10);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int recoverRes = MoonshineNativeMethods.MicCaptureRecover(handle);
            recoverRes.Should().Be(1);

            int isActive = MoonshineNativeMethods.MicCaptureIsActive(handle);
            isActive.Should().Be(1);

            float[] buffer = new float[480];
            fixed (float* ptr = buffer)
            {
                int readRes = MoonshineNativeMethods.MicCaptureReadFloat(
                    handle,
                    ptr,
                    (uint)buffer.Length,
                    out uint samplesRead,
                    out ulong timestampQpc
                );

                readRes.Should().Be(1);
                samplesRead.Should().BeGreaterThan(0u);
                samplesRead.Should().BeLessThanOrEqualTo(480u);
                timestampQpc.Should().BeGreaterThan(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.MicCaptureDestroy(handle);
        }
    }

    [Fact]
    public void MicCapture_Recover_InvalidHandle_ReturnsFailure()
    {
        int recoverRes = MoonshineNativeMethods.MicCaptureRecover(IntPtr.Zero);
        recoverRes.Should().Be(0);

        int bogusRecover = MoonshineNativeMethods.MicCaptureRecover(new IntPtr(0x12345678));
        bogusRecover.Should().Be(0);
    }

    [Fact]
    public unsafe void AudioWasapi_InvalidHandle_FailsSafelyWithoutThrowing()
    {
        float[] sample = [0.5f, 0.5f];
        fixed (float* ptr = sample)
        {
            // Null handle submit
            int submitZero = MoonshineNativeMethods.AudioSubmitPcm(IntPtr.Zero, ptr, 1);
            submitZero.Should().NotBe(0);

            // Bogus handle submit
            int submitBogus = MoonshineNativeMethods.AudioSubmitPcm(new IntPtr(0x12345678), ptr, 1);
            submitBogus.Should().NotBe(0);
        }

        // Metrics on invalid handles
        MoonshineNativeMethods.AudioGetMetrics(IntPtr.Zero, out ulong renderedZero, out uint underrunsZero);
        renderedZero.Should().Be(0);
        underrunsZero.Should().Be(0);

        MoonshineNativeMethods.AudioGetMetrics(new IntPtr(0x12345678), out ulong renderedBogus, out uint underrunsBogus);
        renderedBogus.Should().Be(0);
        underrunsBogus.Should().Be(0);

        // Destruction of invalid handles must be safe no-op
        MoonshineNativeMethods.AudioDestroy(IntPtr.Zero);
        MoonshineNativeMethods.AudioDestroy(new IntPtr(0x12345678));
    }

    [Fact]
    public unsafe void AudioWasapi_PostDisposal_FailsClosedViaSafeHandleStore()
    {
        IntPtr handle = MoonshineNativeMethods.AudioCreateWasapi(48000, 2, 0);
        handle.Should().NotBe(IntPtr.Zero);

        float[] sample = [0.1f, 0.2f];
        fixed (float* ptr = sample)
        {
            int submitInitial = MoonshineNativeMethods.AudioSubmitPcm(handle, ptr, 1);
            submitInitial.Should().Be(0);

            // Destroy the handle
            MoonshineNativeMethods.AudioDestroy(handle);

            // Subsequent submit to released handle must fail closed without use-after-free crash
            int submitPostDestroy = MoonshineNativeMethods.AudioSubmitPcm(handle, ptr, 1);
            submitPostDestroy.Should().NotBe(0);
        }
    }
}


