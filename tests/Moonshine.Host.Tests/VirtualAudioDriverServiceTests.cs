using FluentAssertions;
using Moonshine.Host.Audio;
using Xunit;

namespace Moonshine.Host.Tests;

public class VirtualAudioDriverServiceTests
{
    [Fact]
    public void VirtualAudioDriverService_CreateAndDispose_ExecutesCleanly()
    {
        var service = new VirtualAudioDriverService();
        service.IsInitialized.Should().BeTrue();

        service.Dispose();
        service.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void VirtualAudioDriverService_DoubleDispose_IsSafe()
    {
        var service = new VirtualAudioDriverService();
        service.Dispose();
        service.Dispose();

        service.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void VirtualAudioDriverService_GetStatus_ReturnsExpectedMetadata()
    {
        using var service = new VirtualAudioDriverService();

        bool ok = service.TryGetStatus(out var status);
        ok.Should().BeTrue();
        status.SupportedSampleRatesCount.Should().Be(5);
        status.SupportedChannelsCount.Should().Be(4);
        status.GetDriverVersion().Should().Be("1.0.0.0");
    }

    [Fact]
    public void VirtualAudioDriverService_ValidateFormat_ValidatesAllStandardRates()
    {
        using var service = new VirtualAudioDriverService();

        service.ValidateFormat(44100, 2, 4).Should().BeTrue();
        service.ValidateFormat(48000, 2, 4).Should().BeTrue();
        service.ValidateFormat(48000, 6, 4).Should().BeTrue();
        service.ValidateFormat(48000, 8, 4).Should().BeTrue();
        service.ValidateFormat(96000, 2, 2).Should().BeTrue();

        service.ValidateFormat(22050, 2, 4).Should().BeFalse();
        service.ValidateFormat(48000, 5, 4).Should().BeFalse();
    }

    [Fact]
    public void VirtualAudioDriverService_GetEndpointNames_ReturnsRenderAndCaptureNames()
    {
        using var service = new VirtualAudioDriverService();

        bool ok = service.TryGetEndpointNames(out string renderName, out string captureName);
        ok.Should().BeTrue();
        renderName.Should().Be("Moonshine Audio");
        captureName.Should().Be("Moonshine Microphone");
    }

    [Fact]
    public void VirtualAudioDriverService_Disposed_ThrowsObjectDisposedException()
    {
        var service = new VirtualAudioDriverService();
        service.Dispose();

        Action act = () => service.IsDriverInstalled();
        act.Should().Throw<ObjectDisposedException>();
    }
}
