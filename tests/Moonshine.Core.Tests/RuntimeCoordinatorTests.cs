using FluentAssertions;
using Moonshine.Core.Runtime;
using Xunit;

namespace Moonshine.Core.Tests;

public class RuntimeCoordinatorTests
{
    [Fact]
    public void MoonshineRuntimeCoordinator_InitialState_IsStoppedAndZeroResources()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        coordinator.ActiveRole.Should().Be(ApplicationRole.None);
        coordinator.State.Should().Be(RuntimeState.Stopped);
        coordinator.IsRunning.Should().BeFalse();

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.None);
        status.State.Should().Be(RuntimeState.Stopped);

        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Host.IsRunning.Should().BeFalse();
        status.Host.ActiveSessionCount.Should().Be(0);
        status.Host.ActiveListenerCount.Should().Be(0);
        status.Host.ActiveWorkerCount.Should().Be(0);
        status.Host.ActiveBufferCount.Should().Be(0);

        status.Client.State.Should().Be(RuntimeState.Stopped);
        status.Client.IsRunning.Should().BeFalse();
        status.Client.IsConnected.Should().BeFalse();
        status.Client.ActiveWorkerCount.Should().Be(0);
        status.Client.ActiveBufferCount.Should().Be(0);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_StartHostRole_StartsOnlyHost_ClientHasZeroResources()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        RoleTransitionResult result = await coordinator.StartAsync(ApplicationRole.Host);

        result.Success.Should().BeTrue();
        result.TargetRole.Should().Be(ApplicationRole.Host);
        coordinator.ActiveRole.Should().Be(ApplicationRole.Host);

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.Host);
        status.State.Should().Be(RuntimeState.Unsupported); // Fail-closed baseline

        // Host is queried, Client is completely inactive with zero resources
        status.Client.State.Should().Be(RuntimeState.Stopped);
        status.Client.IsRunning.Should().BeFalse();
        status.Client.ActiveWorkerCount.Should().Be(0);
        status.Client.ActiveBufferCount.Should().Be(0);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_StartClientRole_StartsOnlyClient_HostHasZeroResources()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        RoleTransitionResult result = await coordinator.StartAsync(ApplicationRole.Client);

        result.Success.Should().BeTrue();
        result.TargetRole.Should().Be(ApplicationRole.Client);
        coordinator.ActiveRole.Should().Be(ApplicationRole.Client);

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.Client);
        status.State.Should().Be(RuntimeState.Running);

        // Client is queried, Host is completely inactive with zero resources
        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Host.IsRunning.Should().BeFalse();
        status.Host.ActiveSessionCount.Should().Be(0);
        status.Host.ActiveListenerCount.Should().Be(0);
        status.Host.ActiveWorkerCount.Should().Be(0);
        status.Host.ActiveBufferCount.Should().Be(0);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_StartHostAndClientRole_StartsBothConcurrently_IsolatesState()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        RoleTransitionResult result = await coordinator.StartAsync(ApplicationRole.HostAndClient);

        result.Success.Should().BeTrue();
        result.TargetRole.Should().Be(ApplicationRole.HostAndClient);
        coordinator.ActiveRole.Should().Be(ApplicationRole.HostAndClient);

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.HostAndClient);
        status.State.Should().Be(RuntimeState.Unsupported);
        status.Host.State.Should().Be(RuntimeState.Unsupported);
        status.Client.State.Should().Be(RuntimeState.Running);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_Stop_TerminatesAllActiveServicesAndFreesResources()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        await coordinator.StartAsync(ApplicationRole.HostAndClient);
        RoleTransitionResult stopResult = await coordinator.StopAsync();

        stopResult.Success.Should().BeTrue();
        stopResult.TargetRole.Should().Be(ApplicationRole.None);
        coordinator.ActiveRole.Should().Be(ApplicationRole.None);
        coordinator.State.Should().Be(RuntimeState.Stopped);

        RuntimeStatus status = coordinator.GetStatus();
        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Client.State.Should().Be(RuntimeState.Stopped);
        status.Host.ActiveListenerCount.Should().Be(0);
        status.Client.ActiveWorkerCount.Should().Be(0);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_TransitionFromHostToClient_ClosesHostAndStartsClient()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        await coordinator.StartAsync(ApplicationRole.Host);
        coordinator.ActiveRole.Should().Be(ApplicationRole.Host);

        RoleTransitionResult transitionResult = await coordinator.TransitionToRoleAsync(ApplicationRole.Client);
        transitionResult.Success.Should().BeTrue();
        transitionResult.TargetRole.Should().Be(ApplicationRole.Client);
        coordinator.ActiveRole.Should().Be(ApplicationRole.Client);

        RuntimeStatus status = coordinator.GetStatus();
        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Client.State.Should().Be(RuntimeState.Running);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_TransitionFromClientToHostAndClient_ActivatesBothRoles()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        await coordinator.StartAsync(ApplicationRole.Client);
        coordinator.ActiveRole.Should().Be(ApplicationRole.Client);

        RoleTransitionResult transitionResult = await coordinator.TransitionToRoleAsync(ApplicationRole.HostAndClient);
        transitionResult.Success.Should().BeTrue();
        transitionResult.TargetRole.Should().Be(ApplicationRole.HostAndClient);
        coordinator.ActiveRole.Should().Be(ApplicationRole.HostAndClient);

        RuntimeStatus status = coordinator.GetStatus();
        status.Host.State.Should().Be(RuntimeState.Unsupported);
        status.Client.State.Should().Be(RuntimeState.Running);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_Restart_ExecutesGracefully()
    {
        using var coordinator = new MoonshineRuntimeCoordinator();

        await coordinator.StartAsync(ApplicationRole.Host);
        RoleTransitionResult restartResult = await coordinator.RestartAsync();

        restartResult.Success.Should().BeTrue();
        coordinator.ActiveRole.Should().Be(ApplicationRole.Host);
    }

    [Fact]
    public async Task MoonshineRuntimeCoordinator_StartupFault_RollsBackSuccessfully_LeavesZeroResources()
    {
        var testHost = new MockTestHostService(shouldThrowOnStart: false);
        var testClient = new MockTestClientService(shouldThrowOnStart: true);

        using var coordinator = new MoonshineRuntimeCoordinator(
            hostServiceFactory: () => testHost,
            clientServiceFactory: () => testClient);

        bool faultDispatched = false;
        coordinator.Faulted += (_, _) => faultDispatched = true;

        RoleTransitionResult result = await coordinator.StartAsync(ApplicationRole.HostAndClient);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        coordinator.State.Should().Be(RuntimeState.Faulted);
        coordinator.ActiveRole.Should().Be(ApplicationRole.None);
        faultDispatched.Should().BeTrue();

        // Rollback verification: Host was started, but then rolled back and stopped when Client threw
        testHost.StartCount.Should().Be(1);
        testHost.StopCount.Should().Be(1);
        testHost.IsRunning.Should().BeFalse();
        testHost.HasActiveResources.Should().BeFalse();
    }

    [Fact]
    public void MoonshineRuntimeCoordinator_DoubleDispose_IsSafeAndIdempotent()
    {
        var coordinator = new MoonshineRuntimeCoordinator();
        coordinator.Dispose();
        coordinator.Dispose();

        coordinator.State.Should().Be(RuntimeState.Stopped);
        coordinator.ActiveRole.Should().Be(ApplicationRole.None);
    }

    private sealed class MockTestHostService(bool shouldThrowOnStart) : IMoonshineHostService
    {
        public ApplicationRole Role => ApplicationRole.Host;
        public RuntimeState State { get; private set; } = RuntimeState.Stopped;
        public bool IsRunning => State == RuntimeState.Running;
        public bool HasActiveResources { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (shouldThrowOnStart)
            {
                State = RuntimeState.Faulted;
                throw new InvalidOperationException("Injected host start failure for rollback test.");
            }
            State = RuntimeState.Running;
            HasActiveResources = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            State = RuntimeState.Stopped;
            HasActiveResources = false;
            return ValueTask.CompletedTask;
        }

        public async ValueTask RestartAsync(CancellationToken cancellationToken = default)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public HostStatus GetStatus() => new(
            State: State,
            IsRunning: IsRunning,
            ActiveSessionCount: HasActiveResources ? 1 : 0,
            ActiveListenerCount: HasActiveResources ? 1 : 0,
            ActiveWorkerCount: HasActiveResources ? 1 : 0,
            ActiveBufferCount: HasActiveResources ? 1 : 0);

        public void Dispose()
        {
            State = RuntimeState.Stopped;
            HasActiveResources = false;
        }
    }

    private sealed class MockTestClientService(bool shouldThrowOnStart) : IMoonshineClientService
    {
        public ApplicationRole Role => ApplicationRole.Client;
        public RuntimeState State { get; private set; } = RuntimeState.Stopped;
        public bool IsRunning => State == RuntimeState.Running;
        public bool HasActiveResources { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (shouldThrowOnStart)
            {
                State = RuntimeState.Faulted;
                throw new InvalidOperationException("Injected client start failure for rollback test.");
            }
            State = RuntimeState.Running;
            HasActiveResources = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            State = RuntimeState.Stopped;
            HasActiveResources = false;
            return ValueTask.CompletedTask;
        }

        public async ValueTask RestartAsync(CancellationToken cancellationToken = default)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public ClientStatus GetStatus() => new(
            State: State,
            IsRunning: IsRunning,
            IsConnected: HasActiveResources,
            ActiveWorkerCount: HasActiveResources ? 1 : 0,
            ActiveBufferCount: HasActiveResources ? 1 : 0);

        public void Dispose()
        {
            State = RuntimeState.Stopped;
            HasActiveResources = false;
        }
    }
}
