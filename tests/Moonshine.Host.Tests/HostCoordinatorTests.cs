using FluentAssertions;
using Moonshine.Host;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostCoordinatorTests
{
    [Fact]
    public void MoonshineHostCoordinator_InitialState_IsDisabled()
    {
        using var coordinator = new MoonshineHostCoordinator();
        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void MoonshineHostCoordinator_Enable_ReportsUnsupportedUntilNativeTransportExists()
    {
        using var coordinator = new MoonshineHostCoordinator();
        coordinator.Enable();
        coordinator.State.Should().Be(HostState.Unsupported);
        coordinator.IsRunning.Should().BeFalse();

        coordinator.Disable();
        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.IsRunning.Should().BeFalse();
    }
}
