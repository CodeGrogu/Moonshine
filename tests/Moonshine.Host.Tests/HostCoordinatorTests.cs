using FluentAssertions;
using Moonshine.Core.Runtime;
using Moonshine.Host;
using Xunit;

namespace Moonshine.Host.Tests;

[CollectionDefinition("HardwareExclusive", DisableParallelization = true)]
public class HostHardwareExclusiveFixture { }

[Collection("HardwareExclusive")]
public class HostCoordinatorTests
{
    [Fact]
    public void MoonshineHostCoordinator_InitialState_IsDisabled()
    {
        using var coordinator = new MoonshineHostCoordinator();
        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.IsRunning.Should().BeFalse();
        coordinator.HasActiveResources.Should().BeFalse();

        HostStatus status = coordinator.GetStatus();
        status.State.Should().Be(RuntimeState.Stopped);
        status.IsRunning.Should().BeFalse();
        status.ActiveSessionCount.Should().Be(0);
        status.ActiveListenerCount.Should().Be(0);
        status.ActiveWorkerCount.Should().Be(0);
        status.ActiveBufferCount.Should().Be(0);
    }

    [Fact]
    public void MoonshineHostCoordinator_Enable_ReportsRunning()
    {
        using var coordinator = new MoonshineHostCoordinator();
        coordinator.Enable();
        coordinator.State.Should().Be(HostState.Running);
        coordinator.IsRunning.Should().BeTrue();

        coordinator.Disable();
        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.IsRunning.Should().BeFalse();
        coordinator.HasActiveResources.Should().BeFalse();
    }

    [Fact]
    public async Task MoonshineHostCoordinator_LifecycleAsync_TransitionsAndCleansUp()
    {
        using var coordinator = new MoonshineHostCoordinator();

        await coordinator.StartAsync();
        coordinator.State.Should().Be(HostState.Running);
        coordinator.IsRunning.Should().BeTrue();
        coordinator.HasActiveResources.Should().BeTrue();

        await coordinator.RestartAsync();
        coordinator.State.Should().Be(HostState.Running);

        await coordinator.StopAsync();
        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.HasActiveResources.Should().BeFalse();
    }

    [Fact]
    public void MoonshineHostCoordinator_DoubleDispose_IsSafeAndIdempotent()
    {
        var coordinator = new MoonshineHostCoordinator();
        coordinator.Enable();
        coordinator.Dispose();
        coordinator.Dispose();

        coordinator.State.Should().Be(HostState.Disabled);
        coordinator.HasActiveResources.Should().BeFalse();
    }
}
