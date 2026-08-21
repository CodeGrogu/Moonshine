using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Network;
using Moonshine.Core.Runtime;
using Xunit;

namespace Moonshine.Core.Tests;

public class NetworkExposureTests
{
    [Fact]
    public async Task HostRole_ExposesOnlyRequiredHostEndpoints()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        RoleTransitionResult startResult = await coordinator.StartAsync(ApplicationRole.Host);
        startResult.Success.Should().BeTrue();

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.Host);
        status.Host.ActiveListenerCount.Should().Be(6);

        // Client has zero listeners or activity
        status.Client.State.Should().Be(RuntimeState.Stopped);
        status.Client.IsRunning.Should().BeFalse();

        RoleTransitionResult stopResult = await coordinator.StopAsync();
        stopResult.Success.Should().BeTrue();

        RuntimeStatus stoppedStatus = coordinator.GetStatus();
        stoppedStatus.Host.ActiveListenerCount.Should().Be(0);
    }

    [Fact]
    public async Task ClientRole_ExposesNoHostListeners()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        RoleTransitionResult startResult = await coordinator.StartAsync(ApplicationRole.Client);
        startResult.Success.Should().BeTrue();

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.Client);
        status.Host.ActiveListenerCount.Should().Be(0);
        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Host.IsRunning.Should().BeFalse();

        await coordinator.StopAsync();
    }

    [Fact]
    public void DisabledRole_HasZeroListeningSockets()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        coordinator.ActiveRole.Should().Be(ApplicationRole.None);
        coordinator.State.Should().Be(RuntimeState.Stopped);

        RuntimeStatus status = coordinator.GetStatus();
        status.Host.ActiveListenerCount.Should().Be(0);
        status.Host.State.Should().Be(RuntimeState.Stopped);
        status.Client.State.Should().Be(RuntimeState.Stopped);
    }

    [Fact]
    public async Task HostAndClientRole_ExposesHostListenersAndAllowsClientSimultaneously()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        RoleTransitionResult startResult = await coordinator.StartAsync(ApplicationRole.HostAndClient);
        startResult.Success.Should().BeTrue();

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.HostAndClient);
        status.Host.ActiveListenerCount.Should().Be(6);
        status.Client.State.Should().Be(RuntimeState.Unsupported); // Fail-closed active baseline

        RoleTransitionResult stopResult = await coordinator.StopAsync();
        stopResult.Success.Should().BeTrue();

        RuntimeStatus stoppedStatus = coordinator.GetStatus();
        stoppedStatus.Host.ActiveListenerCount.Should().Be(0);
    }

    [Fact]
    public void HostEndpointConfig_Profiles_ReflectIntentAccurately()
    {
        var ephemeral = HostEndpointConfig.Ephemeral;
        ephemeral.Profile.Should().Be(HostNetworkProfile.EphemeralTesting);
        ephemeral.IsEphemeral.Should().BeTrue();

        var prod = HostEndpointConfig.ProductionDefault;
        prod.Profile.Should().Be(HostNetworkProfile.Production);
        prod.IsEphemeral.Should().BeFalse();
        prod.ControlTcpPort.Should().Be(48010);
        prod.DiscoveryUdpPort.Should().Be(48010);
        prod.VideoUdpPort.Should().Be(47998);
        prod.ControlFeedbackUdpPort.Should().Be(47999);
        prod.AudioUdpPort.Should().Be(48000);
        prod.MicUdpPort.Should().Be(48002);

        var custom = HostEndpointConfig.Custom(IPAddress.Loopback, 50000, 50001, 50002, 50003, 50004, 50005);
        custom.Profile.Should().Be(HostNetworkProfile.Custom);
        custom.IsEphemeral.Should().BeFalse();
        custom.ControlTcpPort.Should().Be(50000);
    }

    [Fact]
    public void FirewallManager_RejectsEphemeralTestingConfiguration()
    {
        Action act = () => MoonshineFirewallManager.GetRequiredRules(HostEndpointConfig.Ephemeral);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ephemeral*");
    }

    [Fact]
    public async Task PortConflict_ProducesExplicitFaultAndRollsBackPartiallyOpenedListeners()
    {
        // 1. Occupy an ephemeral TCP port
        var occupyingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        occupyingSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
        occupyingSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        occupyingSocket.Listen(1);
        int conflictPort = ((IPEndPoint)occupyingSocket.LocalEndPoint!).Port;

        try
        {
            using var networkManager = new MoonshineHostNetworkManager();
            var conflictConfig = new HostEndpointConfig
            {
                Profile = HostNetworkProfile.Custom,
                BindAddress = IPAddress.Loopback,
                ControlTcpPort = conflictPort,
                DiscoveryUdpPort = 0,
                VideoUdpPort = 0,
                ControlFeedbackUdpPort = 0,
                AudioUdpPort = 0,
                MicUdpPort = 0
            };

            // 2. Starting listeners should fail with explicit MoonshinePortConflictException
            Func<Task> act = async () => await networkManager.StartListenersAsync(conflictConfig);
            await act.Should().ThrowAsync<MoonshinePortConflictException>()
                .Where(ex => ex.Protocol == ProtocolType.Tcp && ex.Role == ApplicationRole.Host);

            // 3. Verify all partially opened listeners were cleanly rolled back
            networkManager.ActiveListenerCount.Should().Be(0);
            networkManager.IsExposed.Should().BeFalse();
        }
        finally
        {
            occupyingSocket.Dispose();
        }
    }

    [Fact]
    public async Task RepeatedRoleTransitions_DoNotLeakListenersOrSockets()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        for (int i = 0; i < 5; i++)
        {
            // Transition: None -> Host
            var r1 = await coordinator.StartAsync(ApplicationRole.Host);
            r1.Success.Should().BeTrue();
            coordinator.GetStatus().Host.ActiveListenerCount.Should().Be(6);

            // Transition: Host -> Client
            var r2 = await coordinator.TransitionToRoleAsync(ApplicationRole.Client);
            r2.Success.Should().BeTrue();
            coordinator.GetStatus().Host.ActiveListenerCount.Should().Be(0);

            // Transition: Client -> HostAndClient
            var r3 = await coordinator.TransitionToRoleAsync(ApplicationRole.HostAndClient);
            r3.Success.Should().BeTrue();
            coordinator.GetStatus().Host.ActiveListenerCount.Should().Be(6);

            // Transition: HostAndClient -> None
            var r4 = await coordinator.StopAsync();
            r4.Success.Should().BeTrue();
            coordinator.GetStatus().Host.ActiveListenerCount.Should().Be(0);
        }
    }

    [Fact]
    public void FirewallManager_GeneratesExplicitMinimalAndReversibleScripts()
    {
        var config = HostEndpointConfig.ProductionDefault;

        var rules = MoonshineFirewallManager.GetRequiredRules(config);
        rules.Should().HaveCount(6);

        string enableScript = MoonshineFirewallManager.GenerateEnableFirewallScript(config, "C:\\Moonshine\\Moonshine.exe");
        enableScript.Should().Contain("New-NetFirewallRule");
        enableScript.Should().Contain("48010");
        enableScript.Should().Contain("47998");
        enableScript.Should().Contain("47999");
        enableScript.Should().Contain("48000");
        enableScript.Should().Contain("48002");
        enableScript.Should().Contain("-Program \"C:\\Moonshine\\Moonshine.exe\"");

        string disableScript = MoonshineFirewallManager.GenerateDisableFirewallScript();
        disableScript.Should().Contain("Get-NetFirewallRule");
        disableScript.Should().Contain("Remove-NetFirewallRule");
    }
}
