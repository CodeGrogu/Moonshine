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

    [Fact]
    public void VirtualAudioDriverService_GetInstallationState_ReturnsValidState()
    {
        using var service = new VirtualAudioDriverService();

        DriverInstallationState state = service.GetInstallationState();
        // The installation state must be one of the defined enum values
        state.Should().BeOneOf(
            DriverInstallationState.NotInstalled,
            DriverInstallationState.Installed,
            DriverInstallationState.EndpointsActive
        );
    }

    [Fact]
    public void VirtualAudioDriverService_TryInstallDriver_WithNullOrWhiteSpace_ThrowsArgumentException()
    {
        using var service = new VirtualAudioDriverService();

        Action actNull = () => service.TryInstallDriver(null!);
        actNull.Should().Throw<ArgumentException>();

        Action actEmpty = () => service.TryInstallDriver("   ");
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VirtualAudioDriverService_TryInstallDriver_WithNonExistentPath_ReturnsFalse()
    {
        using var service = new VirtualAudioDriverService();

        // Attempting to install a non-existent INF file should fail gracefully without throwing
        bool result = service.TryInstallDriver("C:\\non_existent_path\\MoonshineAudio.inf");
        result.Should().BeFalse();
    }

    [Fact]
    public void VirtualAudioDriverService_LifecycleMethods_WhenDisposed_ThrowObjectDisposedException()
    {
        var service = new VirtualAudioDriverService();
        service.Dispose();

        Action actState = () => service.GetInstallationState();
        actState.Should().Throw<ObjectDisposedException>();

        Action actInstall = () => service.TryInstallDriver("test.inf");
        actInstall.Should().Throw<ObjectDisposedException>();

        Action actRemove = () => service.TryRemoveDriver();
        actRemove.Should().Throw<ObjectDisposedException>();

        Action actRestart = () => service.TryRestartDriver();
        actRestart.Should().Throw<ObjectDisposedException>();

        Action actMmcss = () => service.TryEnableMmcss(out _);
        actMmcss.Should().Throw<ObjectDisposedException>();
    }
}
