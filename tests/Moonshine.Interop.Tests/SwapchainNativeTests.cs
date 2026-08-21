using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class SwapchainNativeTests
{
    [Fact]
    public void SwapchainCreate_ReturnsNullUntilPresentationRetainsANativeSwapchain() => MoonshineNativeMethods.SwapchainCreate(IntPtr.Zero, IntPtr.Zero, 1920, 1080, 2, 0).Should().Be(IntPtr.Zero);

    [Fact]
    public void SwapchainOperations_NullHandle_ReturnFailure()
    {
        MoonshineNativeMethods.SwapchainPresent(IntPtr.Zero, 0, 0).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainResize(IntPtr.Zero, 1920, 1080).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainSetHdr(IntPtr.Zero, 1).Should().NotBe(0);
    }
}
