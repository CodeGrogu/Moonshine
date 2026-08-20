using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class WgcNativeTests
{
    [Fact]
    public void CaptureCreateWgc_DefaultMonitor_ReturnsValidOrNullHandle()
    {
        uint width = 0;
        uint height = 0;
        IntPtr handle = MoonshineNativeMethods.CaptureCreateWgc(IntPtr.Zero, 60, out width, out height);

        if (handle != IntPtr.Zero)
        {
            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);

            MoonshineNativeMethods.CaptureAcquireFrame(handle, 50, out _);
            MoonshineNativeMethods.CaptureReleaseFrame(handle);
            MoonshineNativeMethods.CaptureDestroy(handle);
        }
    }
}
