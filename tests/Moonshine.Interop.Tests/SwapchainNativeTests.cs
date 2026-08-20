using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class SwapchainNativeTests
{
    [Fact]
    public void SwapchainCreate_ValidParameters_ReturnsNonNullHandle()
    {
        IntPtr handle = MoonshineNativeMethods.SwapchainCreate(IntPtr.Zero, IntPtr.Zero, 1920, 1080, 2, 0);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            int presentRes = MoonshineNativeMethods.SwapchainPresent(handle, 0, 0x00000200);
            presentRes.Should().Be(0);

            int resizeRes = MoonshineNativeMethods.SwapchainResize(handle, 2560, 1440);
            resizeRes.Should().Be(0);

            int hdrRes = MoonshineNativeMethods.SwapchainSetHdr(handle, 1);
            hdrRes.Should().Be(0);
        }
        finally
        {
            MoonshineNativeMethods.SwapchainDestroy(handle);
        }
    }

    [Fact]
    public void SwapchainPresent_NullHandle_ReturnsFailure()
    {
        int presentRes = MoonshineNativeMethods.SwapchainPresent(IntPtr.Zero, 0, 0);
        presentRes.Should().NotBe(0);

        int resizeRes = MoonshineNativeMethods.SwapchainResize(IntPtr.Zero, 1920, 1080);
        resizeRes.Should().NotBe(0);

        int hdrRes = MoonshineNativeMethods.SwapchainSetHdr(IntPtr.Zero, 1);
        hdrRes.Should().NotBe(0);
    }
}
