using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class SwapchainNativeTests
{
    [Fact]
    public void SwapchainCreate_NullHwnd_ReturnsNullHandle() =>
        MoonshineNativeMethods.SwapchainCreate(IntPtr.Zero, IntPtr.Zero, 1920, 1080, 2, 0).Should().Be(IntPtr.Zero);

    [Fact]
    public void SwapchainCreate_ZeroDimensions_ReturnsNullHandle() =>
        MoonshineNativeMethods.SwapchainCreate((IntPtr)1, IntPtr.Zero, 0, 0, 2, 0).Should().Be(IntPtr.Zero);

    [Fact]
    public void SwapchainOperations_NullHandle_ReturnFailure()
    {
        MoonshineNativeMethods.SwapchainPresent(IntPtr.Zero, 0, 0).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainPresentTexture(IntPtr.Zero, IntPtr.Zero, 0, 0).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainResize(IntPtr.Zero, 1920, 1080).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainSetHdr(IntPtr.Zero, 1).Should().NotBe(0);

        var meta = new MoonshineHdr10Metadata();
        MoonshineNativeMethods.SwapchainSetHdrMetadata(IntPtr.Zero, in meta).Should().NotBe(0);

        MoonshineNativeMethods.SwapchainGetMetrics(IntPtr.Zero, out var metrics).Should().NotBe(0);
        MoonshineNativeMethods.SwapchainIsTearingSupported(IntPtr.Zero).Should().Be(0);
        MoonshineNativeMethods.SwapchainGetWaitableObject(IntPtr.Zero).Should().Be(IntPtr.Zero);
    }
}
