using FluentAssertions;
using Moonshine.Host.Color;
using Xunit;

namespace Moonshine.Host.Tests;

public class D3DColorSpaceConverterTests
{
    [Fact]
    public void D3DColorSpaceConverter_InitializeAndDispose_ExecutesCleanly()
    {
        using var converter = new D3DColorSpaceConverter(1920, 1080, 24, 104);
        converter.Width.Should().Be(1920);
        converter.Height.Should().Be(1080);
        converter.InFormat.Should().Be(24);
        converter.OutFormat.Should().Be(104);
    }

    [Fact]
    public void D3DColorSpaceConverter_DoubleDispose_IsSafe()
    {
        var converter = new D3DColorSpaceConverter(1920, 1080, 87, 103);
        converter.Dispose();
        converter.Dispose();

        converter.TryConvert(IntPtr.Zero, IntPtr.Zero).Should().BeFalse();
    }
}
