using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class VideoNativeTests
{
    [Fact]
    public void VideoQueryCaps_ReportsNoUnsupportedDecoderCapabilities()
    {
        MoonshineNativeMethods.VideoQueryCaps(out var caps).Should().Be(0);
        caps.MaxWidth.Should().Be(0);
        caps.MaxHeight.Should().Be(0);
        caps.MaxFps.Should().Be(0);
        caps.SupportsHevc.Should().Be(0);
        caps.SupportsAv1.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VideoCreate_ReturnsNullUntilDecodeSubmissionExists(bool d3d12)
    {
        IntPtr handle = d3d12 ? MoonshineNativeMethods.VideoCreateD3D12(IntPtr.Zero, 1920, 1080, 1) : MoonshineNativeMethods.VideoCreateD3D11(IntPtr.Zero, 1920, 1080, 1);
        handle.Should().Be(IntPtr.Zero);
    }
}
