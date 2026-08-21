using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class VirtualAudioDriverNativeTests
{
    [Fact]
    public void VirtualAudioDriver_CreateAndDestroy_LifecycleSucceeds()
    {
        IntPtr handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
        handle.Should().NotBe(IntPtr.Zero);

        MoonshineNativeMethods.VirtualAudioDriverDestroy(handle);
    }

    [Fact]
    public void VirtualAudioDriver_GetStatus_ReturnsValidCapabilities()
    {
        IntPtr handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int res = MoonshineNativeMethods.VirtualAudioDriverGetStatus(handle, out var status);
            res.Should().Be(1);
            status.SupportedSampleRatesCount.Should().Be(5);
            status.SupportedChannelsCount.Should().Be(4);
            status.GetDriverVersion().Should().Be("1.0.0.0");
        }
        finally
        {
            MoonshineNativeMethods.VirtualAudioDriverDestroy(handle);
        }
    }

    [Fact]
    public void VirtualAudioDriver_ValidateFormat_ValidAndInvalidInputs()
    {
        IntPtr handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            // Valid standard rates and channel counts
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 44100, 2, 4).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 2, 4).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 6, 4).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 8, 4).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 1, 1).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 96000, 2, 2).Should().Be(1);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 192000, 2, 3).Should().Be(1);

            // Invalid rates / channels / formats
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 32000, 2, 4).Should().Be(0);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 3, 4).Should().Be(0);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 2, 0).Should().Be(0);
            MoonshineNativeMethods.VirtualAudioDriverValidateFormat(handle, 48000, 2, 5).Should().Be(0);
        }
        finally
        {
            MoonshineNativeMethods.VirtualAudioDriverDestroy(handle);
        }
    }

    [Fact]
    public unsafe void VirtualAudioDriver_GetEndpointNames_ReturnsExpectedNames()
    {
        IntPtr handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            Span<byte> renderBuffer = stackalloc byte[64];
            Span<byte> captureBuffer = stackalloc byte[64];

            fixed (byte* pRender = renderBuffer)
            fixed (byte* pCapture = captureBuffer)
            {
                int res = MoonshineNativeMethods.VirtualAudioDriverGetEndpointNames(
                    handle,
                    pRender,
                    (uint)renderBuffer.Length,
                    pCapture,
                    (uint)captureBuffer.Length
                );

                res.Should().Be(1);
                string renderName = Marshal.PtrToStringAnsi((IntPtr)pRender) ?? string.Empty;
                string captureName = Marshal.PtrToStringAnsi((IntPtr)pCapture) ?? string.Empty;

                renderName.Should().Be("Moonshine Audio");
                captureName.Should().Be("Moonshine Microphone");
            }
        }
        finally
        {
            MoonshineNativeMethods.VirtualAudioDriverDestroy(handle);
        }
    }

    [Fact]
    public void VirtualAudioDriver_Mmcss_EnableAndDisableSucceeds()
    {
        IntPtr handle = MoonshineNativeMethods.VirtualAudioDriverCreate();
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int enableRes = MoonshineNativeMethods.VirtualAudioDriverEnableMmcss(handle, out IntPtr taskHandle);
            if (enableRes == 1 && taskHandle != IntPtr.Zero)
            {
                int disableRes = MoonshineNativeMethods.VirtualAudioDriverDisableMmcss(handle, taskHandle);
                disableRes.Should().Be(1);
            }
        }
        finally
        {
            MoonshineNativeMethods.VirtualAudioDriverDestroy(handle);
        }
    }
}
