using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Moonshine.Core.Discovery;
using Moonshine.Core.Network;
using Moonshine.Core.Runtime;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Discovery;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineLanDiscoveryTests
{
    [Fact]
    public async Task DiscoveryEngine_DiscoversAnnouncingHost_Successfully()
    {
        // Select an ephemeral port for testing isolation
        int testPort = 54800 + Random.Shared.Next(0, 500);
        var hostUuid = new MoonshineUuid128(Guid.NewGuid());
        var endpointConfig = HostEndpointConfig.Custom(
            bindAddress: IPAddress.Loopback,
            controlTcpPort: 48010,
            discoveryUdpPort: testPort,
            videoUdpPort: 47998,
            controlFeedbackUdpPort: 47999,
            audioUdpPort: 48000,
            micUdpPort: 48002);

        using var advertiser = new MoonshineHostDiscoveryAdvertiser(
            endpointConfig: endpointConfig,
            hostUuid: hostUuid,
            hostname: "TEST-GAMING-RIG",
            gpuName: "NVIDIA RTX 4090",
            capabilities: MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.Hdr10,
            advertisementInterval: TimeSpan.FromMilliseconds(50));

        advertiser.Start();

        await using var discoveryEngine = new MoonshineLanDiscoveryEngine(
            discoveryPort: testPort,
            sweepInterval: TimeSpan.FromMilliseconds(100),
            hostTimeout: TimeSpan.FromSeconds(2));

        MoonshineDiscoveredHost? discovered = null;
        var hostDiscoveredTcs = new TaskCompletionSource<MoonshineDiscoveredHost>();
        discoveryEngine.HostDiscovered += host =>
        {
            discovered = host;
            hostDiscoveredTcs.TrySetResult(host);
        };

        discoveryEngine.Start();

        // Send active probe directly to loopback
        await discoveryEngine.SendProbeAsync(new IPEndPoint(IPAddress.Loopback, testPort));

        var completedTask = await Task.WhenAny(hostDiscoveredTcs.Task, Task.Delay(2000));
        completedTask.Should().Be(hostDiscoveredTcs.Task, "Discovery engine should receive advertisement or probe response within timeout");

        discovered.Should().NotBeNull();
        discovered!.HostUuid.Should().Be(hostUuid);
        discovered.Hostname.Should().Be("TEST-GAMING-RIG");
        discovered.GpuName.Should().Be("NVIDIA RTX 4090");
        discovered.ControlTcpPort.Should().Be(48010);
        discovered.DiscoveryUdpPort.Should().Be(testPort);
        discovered.SupportsHdr10.Should().BeTrue();
        discovered.IsOnline.Should().BeTrue();

        discoveryEngine.ActiveHosts.Should().ContainSingle();
    }

    [Fact]
    public async Task DiscoveryEngine_StaleHost_ExpiresDeterministically()
    {
        int testPort = 55300 + Random.Shared.Next(0, 500);
        var hostUuid = new MoonshineUuid128(Guid.NewGuid());
        var endpointConfig = HostEndpointConfig.Custom(
            bindAddress: IPAddress.Loopback,
            controlTcpPort: 48010,
            discoveryUdpPort: testPort,
            videoUdpPort: 47998,
            controlFeedbackUdpPort: 47999,
            audioUdpPort: 48000,
            micUdpPort: 48002);

        var advertiser = new MoonshineHostDiscoveryAdvertiser(
            endpointConfig: endpointConfig,
            hostUuid: hostUuid,
            hostname: "EPHEMERAL-HOST",
            advertisementInterval: TimeSpan.FromMilliseconds(20));

        advertiser.Start();

        await using var discoveryEngine = new MoonshineLanDiscoveryEngine(
            discoveryPort: testPort,
            sweepInterval: TimeSpan.FromMilliseconds(50),
            hostTimeout: TimeSpan.FromMilliseconds(150));

        var discoveredTcs = new TaskCompletionSource<MoonshineDiscoveredHost>();
        var lostTcs = new TaskCompletionSource<MoonshineDiscoveredHost>();

        discoveryEngine.HostDiscovered += host => discoveredTcs.TrySetResult(host);
        discoveryEngine.HostLost += host => lostTcs.TrySetResult(host);

        discoveryEngine.Start();
        await discoveryEngine.SendProbeAsync(new IPEndPoint(IPAddress.Loopback, testPort));

        var discTask = await Task.WhenAny(discoveredTcs.Task, Task.Delay(2000));
        discTask.Should().Be(discoveredTcs.Task);

        // Terminate advertiser
        advertiser.Dispose();

        // Wait for stale timeout and sweep
        var lostTask = await Task.WhenAny(lostTcs.Task, Task.Delay(2000));
        lostTask.Should().Be(lostTcs.Task, "Discovery engine should fire HostLost when advertisements cease");

        var lostHost = await lostTcs.Task;
        lostHost.HostUuid.Should().Be(hostUuid);
        lostHost.IsOnline.Should().BeFalse();

        discoveryEngine.ActiveHosts.Should().BeEmpty();
    }

    [Fact]
    public async Task RoleIsolation_ClientOnlyMode_ExposesNoHostDiscoveryServices()
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
    public async Task RoleIsolation_HostAndHostClient_AdvertiseCleanly()
    {
        using var coordinator = new MoonshineRuntimeCoordinator(
            hostEndpointConfig: HostEndpointConfig.Ephemeral);

        RoleTransitionResult startResult = await coordinator.StartAsync(ApplicationRole.Host);
        startResult.Success.Should().BeTrue();

        RuntimeStatus status = coordinator.GetStatus();
        status.ActiveRole.Should().Be(ApplicationRole.Host);
        status.Host.ActiveListenerCount.Should().Be(6);

        await coordinator.StopAsync();

        RoleTransitionResult hostClientResult = await coordinator.StartAsync(ApplicationRole.HostAndClient);
        hostClientResult.Success.Should().BeTrue();

        RuntimeStatus hostClientStatus = coordinator.GetStatus();
        hostClientStatus.ActiveRole.Should().Be(ApplicationRole.HostAndClient);
        hostClientStatus.Host.ActiveListenerCount.Should().Be(6);

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task DiscoveryEngine_MalformedPackets_HandledGracefully()
    {
        int testPort = 55800 + Random.Shared.Next(0, 500);

        await using var discoveryEngine = new MoonshineLanDiscoveryEngine(
            discoveryPort: testPort,
            sweepInterval: TimeSpan.FromMilliseconds(50),
            hostTimeout: TimeSpan.FromMilliseconds(500));

        discoveryEngine.Start();

        using var rawSender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Send random noise
        byte[] garbage = new byte[100];
        Random.Shared.NextBytes(garbage);
        await rawSender.SendToAsync(garbage, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, testPort));

        // Send invalid magic header
        byte[] badHeader = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];
        var fakeHeader = new MoonshinePacketHeader(0xDEADBEEF, 1, MoonshineMessageType.DiscoveryAnnouncement, 192, 1, 0, 0);
        MoonshineProtocolCodec.TryWriteHeader(fakeHeader, badHeader);
        await rawSender.SendToAsync(badHeader, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, testPort));

        // Ensure engine is still alive and has zero hosts
        await Task.Delay(100);
        discoveryEngine.ActiveHosts.Should().BeEmpty();
    }

    [Fact]
    public async Task HostDiscoveryAdvertiser_HealthAndTelemetry_ReportsAccurately()
    {
        int testPort = 56000 + Random.Shared.Next(0, 500);
        var config = HostEndpointConfig.Custom(IPAddress.Loopback, 48010, testPort, 48011, 48012, 48013, 48014);

        using var advertiser = new MoonshineHostDiscoveryAdvertiser(
            endpointConfig: config,
            advertisementInterval: TimeSpan.FromMilliseconds(50));

        advertiser.Health.Should().Be(DiscoveryAdvertiserHealth.Uninitialised);

        advertiser.Start();
        advertiser.Health.Should().BeOneOf(DiscoveryAdvertiserHealth.Active, DiscoveryAdvertiserHealth.Degraded);

        await Task.Delay(150);
        advertiser.TotalAnnouncementsEmitted.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HostDiscoveryAdvertiser_PortConflict_ReportsFaultedState()
    {
        int testPort = 56600 + Random.Shared.Next(0, 500);

        // Occupy the port with exclusive access
        using var blockingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        blockingSocket.ExclusiveAddressUse = true;
        blockingSocket.Bind(new IPEndPoint(IPAddress.Any, testPort));

        var config = HostEndpointConfig.Custom(IPAddress.Any, 48010, testPort, 48011, 48012, 48013, 48014);

        using var advertiser = new MoonshineHostDiscoveryAdvertiser(endpointConfig: config);
        advertiser.Start();

        advertiser.Health.Should().Be(DiscoveryAdvertiserHealth.Faulted);
        advertiser.LastError.Should().NotBeNullOrWhiteSpace();
        advertiser.LastError.Should().Contain("bind failed");
    }
}
