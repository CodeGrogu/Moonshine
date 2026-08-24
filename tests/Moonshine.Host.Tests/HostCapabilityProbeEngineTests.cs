using System.Diagnostics;
using System.Net.NetworkInformation;
using FluentAssertions;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Session;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostCapabilityProbeEngineTests
{
    [Fact]
    public void ProbeLiveCapabilities_WithTopologyOverride_CalculatesExpectedFields()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 144,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "NVIDIA GeForce RTX 4090",
            DedicatedVideoMemoryBytes: 24_000_000_000,
            IsHardware: true
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
            topologyOverride: topology,
            adaptersOverride: new[] { adapter });

        caps.SupportsHdr10.Should().Be(1);
        caps.MaxEncodeFps.Should().Be(144);
        caps.SupportedAudioCodecs.Should().Be((uint)MoonshineAudioCodec.Opus);
        caps.MaxBitrateKbps.Should().Be(150000);
        caps.Reserved.Should().Be(0);
        caps.Reserved2.Should().Be(0);
    }

    [Fact]
    public void ProbeLiveCapabilities_WhenHeadless_DefaultsFpsTo60()
    {
        var topology = new DisplayTopology(
            Adapters: Array.Empty<DisplayAdapterInfo>(),
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 0
        );

        MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
            topologyOverride: topology,
            adaptersOverride: Array.Empty<DisplayAdapterInfo>());

        caps.SupportsHdr10.Should().Be(0);
        caps.MaxEncodeFps.Should().Be(60);
    }

    [Fact]
    public void ProbeBackendReadiness_WithAttachedDisplays_ReturnsAvailableCapture()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 2560,
            Height: 1440,
            RefreshRateNumerator: 120,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x2000,
            Description: "AMD Radeon RX 7900 XTX",
            DedicatedVideoMemoryBytes: 24_000_000_000,
            IsHardware: true
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 2560, 1440),
            IsHeadless: false,
            TimestampQpc: 0
        );

        HostBackendReadiness readiness = HostCapabilityProbeEngine.ProbeBackendReadiness(
            topologyOverride: topology,
            adaptersOverride: new[] { adapter });

        readiness.DesktopCapture.Should().Be(ComponentReadiness.Available);
        readiness.AudioLoopback.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);
        readiness.PrimaryGpuName.Should().Be("AMD Radeon RX 7900 XTX");
        readiness.AttachedDisplayCount.Should().Be(1);
        readiness.IsHeadless.Should().BeFalse();
    }

    [Fact]
    public async Task HostStreamingSession_LiveBackendReadiness_ReportsOperationalWhenStreaming()
    {
        var capture = new HostStreamingSessionTests.TestDesktopCapturePipeline { IsAvailable = true };
        var encoderPipeline = new HostStreamingSessionTests.TestVideoEncoderPipeline { IsActive = true };
        using var encoder = new UnifiedHardwareEncoderEngine(encoderPipeline);

        ushort basePort = (ushort)(60000 + Random.Shared.Next(0, 50) * 8);
        var config = new HostSessionConfig
        {
            LocalVideoPort = basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            LocalMicPort = (ushort)(basePort + 3),
            ClientVideoPort = (ushort)(basePort + 4),
            ClientAudioPort = (ushort)(basePort + 5),
            ClientControlFeedbackPort = (ushort)(basePort + 6),
            EnableMicrophoneBackchannel = true
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capture,
            encoderEngine: encoder);

        // Before starting: live readiness must report Available or Unsupported, not Operational
        HostBackendReadiness initialReadiness = session.GetLiveBackendReadiness();
        initialReadiness.VideoEncoder.Should().Be(ComponentReadiness.Available);
        initialReadiness.DesktopCapture.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);
        initialReadiness.AudioLoopback.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);
        initialReadiness.MicrophoneBackchannel.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);

        await session.StartAsync();
        session.IsStreaming.Should().BeTrue();

        // While streaming: live readiness must report Operational across active pipelines
        HostBackendReadiness liveReadiness = session.GetLiveBackendReadiness();
        liveReadiness.VideoEncoder.Should().Be(ComponentReadiness.Operational);
        liveReadiness.DesktopCapture.Should().Be(ComponentReadiness.Operational);
        liveReadiness.AudioLoopback.Should().Be(ComponentReadiness.Operational);
        liveReadiness.MicrophoneBackchannel.Should().Be(ComponentReadiness.Operational);

        await session.StopAsync();
        session.IsStreaming.Should().BeFalse();

        // After stopping: live readiness transitions back from Operational
        HostBackendReadiness stoppedReadiness = session.GetLiveBackendReadiness();
        stoppedReadiness.DesktopCapture.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);
        stoppedReadiness.AudioLoopback.Should().BeOneOf(ComponentReadiness.Available, ComponentReadiness.Unsupported);
    }

    [Fact]
    public void ProbeBackendReadiness_WithHeadlessTopologyAndZeroDisplays_ReturnsUnsupportedCapture()
    {
        var topology = new DisplayTopology(
            Adapters: Array.Empty<DisplayAdapterInfo>(),
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 0
        );

        HostBackendReadiness readiness = HostCapabilityProbeEngine.ProbeBackendReadiness(
            topologyOverride: topology,
            adaptersOverride: Array.Empty<DisplayAdapterInfo>());

        readiness.DesktopCapture.Should().Be(ComponentReadiness.Unsupported);
        readiness.AttachedDisplayCount.Should().Be(0);
        readiness.IsHeadless.Should().BeTrue();
    }

    [Fact]
    public void ProbeLiveCapabilities_WithLowVramGpu_ClampsEncodeDimensionsTo4K()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "NVIDIA GeForce GTX 1650 (4GB)",
            DedicatedVideoMemoryBytes: 4_000_000_000,
            IsHardware: true
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
            topologyOverride: topology,
            adaptersOverride: new[] { adapter });

        caps.MaxEncodeWidth.Should().Be(3840);
        caps.MaxEncodeHeight.Should().Be(2160);
    }

    [Fact]
    public void ProbeLiveCapabilities_WithSoftwareAdapter_ClampsEncodeDimensionsTo4K()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "Microsoft Basic Render Driver",
            DedicatedVideoMemoryBytes: 0,
            IsHardware: false
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
            topologyOverride: topology,
            adaptersOverride: new[] { adapter });

        caps.MaxEncodeWidth.Should().Be(3840);
        caps.MaxEncodeHeight.Should().Be(2160);
    }

    [Fact]
    public void ProbeLiveCapabilities_ExecutesInUnder5Milliseconds()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 144,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "Performance Probe GPU Adapter",
            DedicatedVideoMemoryBytes: 16_000_000_000,
            IsHardware: true
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        // Warm up JIT compilation and native method bindings
        MoonshineHostCapabilitiesResponsePayload warmupCaps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
            topologyOverride: topology,
            adaptersOverride: new[] { adapter });
        warmupCaps.MaxEncodeFps.Should().Be(144);
        warmupCaps.SupportedAudioCodecs.Should().Be((uint)MoonshineAudioCodec.Opus);

        // Benchmark execution time over 50 consecutive probes
        const int iterations = 50;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
                topologyOverride: topology,
                adaptersOverride: new[] { adapter });
            caps.MaxEncodeFps.Should().Be(144);
            caps.MaxEncodeWidth.Should().BeGreaterThan(0);
        }

        stopwatch.Stop();
        double elapsedMillisecondsPerCall = stopwatch.Elapsed.TotalMilliseconds / iterations;

        elapsedMillisecondsPerCall.Should().BeLessThan(5.0, "probing live capabilities must execute under 5 milliseconds per invocation");
    }

    [Fact]
    public void ProbeLiveCapabilities_DoesNotStartOrLeakStreamingSocketsOrPipelines()
    {
        var display = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 120,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8
        );

        var adapter = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "Resource Leak Verification Adapter",
            DedicatedVideoMemoryBytes: 8_000_000_000,
            IsHardware: true
        );

        var topology = new DisplayTopology(
            Adapters: new[] { adapter },
            Displays: new[] { display },
            PrimaryDisplay: display,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        int initialHandleCount = proc.HandleCount;

        // Perform 50 sequential probes
        for (int i = 0; i < 50; i++)
        {
            MoonshineHostCapabilitiesResponsePayload caps = HostCapabilityProbeEngine.ProbeLiveCapabilities(
                topologyOverride: topology,
                adaptersOverride: new[] { adapter });

            caps.MaxEncodeFps.Should().Be(120);
            caps.SupportedAudioCodecs.Should().Be((uint)MoonshineAudioCodec.Opus);
            caps.MaxBitrateKbps.Should().Be(150000);
            caps.Reserved.Should().Be(0);
            caps.Reserved2.Should().Be(0);
        }

        // Verify no resource handles or background streaming pipelines leaked across invocations
        proc.Refresh();
        int finalHandleCount = proc.HandleCount;

        (finalHandleCount - initialHandleCount).Should().BeLessThan(20, "probing hardware capabilities must not leak OS handles or streaming socket descriptors");
    }
}

