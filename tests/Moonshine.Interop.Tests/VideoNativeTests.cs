using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class VideoNativeTests
{
    [Fact]
    public void VideoQueryCaps_ReturnsLiveHardwareCapabilities()
    {
        int res = MoonshineNativeMethods.VideoQueryCaps(out var caps);
        res.Should().Be(0);

        (caps.SupportsH264 == 0 || caps.SupportsH264 == 1).Should().BeTrue();
        (caps.SupportsHevc == 0 || caps.SupportsHevc == 1).Should().BeTrue();
        (caps.SupportsAv1 == 0 || caps.SupportsAv1 == 1).Should().BeTrue();
        (caps.Supports10Bit == 0 || caps.Supports10Bit == 1).Should().BeTrue();
        (caps.SupportsHdr10 == 0 || caps.SupportsHdr10 == 1).Should().BeTrue();
        (caps.SupportsD3D12 == 0 || caps.SupportsD3D12 == 1).Should().BeTrue();
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1080u)]
    [InlineData(1920u, 0u)]
    public void VideoCreate_InvalidDimensions_FailsClosed(uint width, uint height)
    {
        IntPtr d3d11Handle = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, width, height, 1);
        d3d11Handle.Should().Be(IntPtr.Zero);

        IntPtr d3d12Handle = MoonshineNativeMethods.VideoCreateD3D12(IntPtr.Zero, width, height, 1);
        d3d12Handle.Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void VideoOperations_NullHandle_FailClosedGracefully()
    {
        MoonshineFrameDesc frame = default;
        MoonshineNativeMethods.VideoSubmitFrame(IntPtr.Zero, in frame).Should().Be(-1);
        MoonshineNativeMethods.VideoGetTexture(IntPtr.Zero).Should().Be(IntPtr.Zero);
        MoonshineNativeMethods.VideoReset(IntPtr.Zero, 1920, 1080).Should().Be(-1);
        MoonshineNativeMethods.VideoDestroy(IntPtr.Zero);
    }
}
