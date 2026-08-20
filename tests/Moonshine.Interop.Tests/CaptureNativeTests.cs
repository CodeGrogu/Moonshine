using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class CaptureNativeTests
{
    [Fact]
    public void MoonshineCaptureFrameDesc_HasExactExpectedSize()
    {
        unsafe
        {
            // void* (8) + uint*3 (12) + ulong (8) + uint (4) + byte (1) + reserved[3] (3) = 36 bytes
            sizeof(MoonshineCaptureFrameDesc).Should().Be(36);
        }
    }

    [Fact]
    public void CaptureCreateDxgi_DefaultAdapter_ReturnsValidOrNullHandle()
    {
        uint width = 0;
        uint height = 0;
        IntPtr handle = MoonshineNativeMethods.CaptureCreateDxgi(0, 0, out width, out height);

        if (handle != IntPtr.Zero)
        {
            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);

            MoonshineNativeMethods.CaptureAcquireFrame(handle, 50, out _);
            MoonshineNativeMethods.CaptureReleaseFrame(handle);
            MoonshineNativeMethods.CaptureDestroy(handle);
        }
    }

    [Fact]
    public void CaptureDestroy_NullHandle_DoesNotThrow()
    {
        MoonshineNativeMethods.CaptureDestroy(IntPtr.Zero);
    }
}
