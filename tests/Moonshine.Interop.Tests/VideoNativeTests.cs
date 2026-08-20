using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class VideoNativeTests
{
    [Fact]
    public void VideoQueryCaps_ReturnsValidHardwareCaps()
    {
        int res = MoonshineNativeMethods.VideoQueryCaps(out var caps);

        res.Should().Be(0);
        caps.MaxWidth.Should().BeGreaterThanOrEqualTo(1920);
        caps.MaxHeight.Should().BeGreaterThanOrEqualTo(1080);
        caps.MaxFps.Should().BeGreaterThanOrEqualTo(60);
        caps.SupportsHevc.Should().Be(1);
        caps.SupportsAv1.Should().Be(1);
    }

    [Fact]
    public unsafe void VideoCreateD3D11_ValidDimensions_ReturnsNonNullHandleAndDecodesFrame()
    {
        IntPtr handle = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            byte[] frameData = new byte[4096];
            fixed (byte* ptr = frameData)
            {
                var frame = new MoonshineFrameDesc
                {
                    FrameIndex = 1,
                    TotalBytes = (uint)frameData.Length,
                    PacketCount = 3,
                    IsKeyframe = 1,
                    FrameBuffer = ptr
                };

                int submitRes = MoonshineNativeMethods.VideoSubmitFrame(handle, in frame);
                submitRes.Should().Be(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.VideoDestroy(handle);
        }
    }

    [Fact]
    public unsafe void VideoCreateD3D12_ValidDimensions_ReturnsNonNullHandleAndDecodesFrame()
    {
        IntPtr handle = MoonshineNativeMethods.VideoCreateD3D12(IntPtr.Zero, 3840, 2160, 1);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            byte[] frameData = new byte[8192];
            fixed (byte* ptr = frameData)
            {
                var frame = new MoonshineFrameDesc
                {
                    FrameIndex = 2,
                    TotalBytes = (uint)frameData.Length,
                    PacketCount = 6,
                    IsKeyframe = 0,
                    FrameBuffer = ptr
                };

                int submitRes = MoonshineNativeMethods.VideoSubmitFrame(handle, in frame);
                submitRes.Should().Be(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.VideoDestroy(handle);
        }
    }

    [Fact]
    public void VideoSubmitFrame_NullBuffer_ReturnsFailure()
    {
        IntPtr handle = MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 0);
        handle.Should().NotBe(IntPtr.Zero);

        try
        {
            var emptyFrame = default(MoonshineFrameDesc);
            int submitRes = MoonshineNativeMethods.VideoSubmitFrame(handle, in emptyFrame);
            submitRes.Should().NotBe(0);
        }
        finally
        {
            MoonshineNativeMethods.VideoDestroy(handle);
        }
    }
}
