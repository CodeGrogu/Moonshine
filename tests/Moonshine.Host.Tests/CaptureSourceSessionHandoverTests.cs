using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Moonshine.Core.Media;
using Moonshine.Host.Capture;
using Moonshine.Host.Encoding;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class CaptureSourceSessionHandoverTests
{
    private sealed class TestDesktopCapturePipeline : IDesktopCapturePipeline
    {
        public uint Width => Source?.Width ?? 1920;
        public uint Height => Source?.Height ?? 1080;
        public uint Format => Source?.Format ?? 28;
        public bool IsHdr => Source?.IsHdr ?? false;
        public uint AdapterIndex => Source?.AdapterIndex ?? 0;
        public uint OutputIndex => Source?.OutputIndex ?? 0;
        public bool IsAvailable { get; set; } = true;
        public CaptureSourceDescriptor? Source { get; set; }
        public int ReconfigureCount { get; private set; }
        public int RecoverCount { get; private set; }
        public CaptureMetrics Metrics => new(0, 0, 0, 0, Width, Height, Format, IsHdr, 0.0);

        public TestDesktopCapturePipeline(CaptureSourceDescriptor? initialSource = null)
        {
            Source = initialSource;
        }

        public unsafe bool TryAcquireNextFrame(uint timeoutMs, out MoonshineCaptureFrameDesc frame)
        {
            if (!IsAvailable)
            {
                frame = default;
                return false;
            }

            frame = new MoonshineCaptureFrameDesc
            {
                TextureHandle = (void*)0x1000,
                Width = Width,
                Height = Height,
                Format = Format,
                TimestampQpc = (ulong)Stopwatch.GetTimestamp()
            };
            return true;
        }

        public void ReleaseFrame()
        {
        }

        public bool TryRecover()
        {
            RecoverCount++;
            IsAvailable = true;
            return true;
        }

        public bool TryReconfigureSource(CaptureSourceDescriptor source)
        {
            ReconfigureCount++;
            Source = source;
            return true;
        }

        public void Dispose()
        {
            IsAvailable = false;
        }
    }

    private sealed class TestVideoEncoderPipeline : IVideoEncoderPipeline
    {
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public uint Fps { get; private set; }
        public uint BitrateKbps { get; private set; }
        public VideoCodec Codec => VideoCodec.HevcMain10;
        public EncoderVendor Vendor => EncoderVendor.Direct3D11Hardware;
        public bool IsActive { get; set; } = true;
        public double AverageEncodingLatencyMicroseconds => 200.0;

        public int ForceIdrCallCount { get; private set; }
        public int ReconfigureCallCount { get; private set; }

        public TestVideoEncoderPipeline(uint width = 1920, uint height = 1080, uint fps = 60, uint bitrateKbps = 20000)
        {
            Width = width;
            Height = height;
            Fps = fps;
            BitrateKbps = bitrateKbps;
        }

        public bool TryEncodeFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            out MoonshineEncodedPacketDesc desc,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            if (forceIdr)
            {
                ForceIdrCallCount++;
            }

            byte[] syntheticNalu = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE };
            syntheticNalu.CopyTo(outBitstream);
            bytesWritten = syntheticNalu.Length;

            desc = new MoonshineEncodedPacketDesc
            {
                PayloadSize = (uint)bytesWritten,
                IsKeyframe = (byte)(forceIdr ? 1 : 0),
                FrameIndex = 0,
                TimestampQpc = Stopwatch.GetTimestamp(),
                IsHeaderPacket = 0,
                TemporalId = 0,
                Reserved = 0
            };
            return true;
        }

        public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0)
        {
            BitrateKbps = bitrateKbps;
            Fps = fps;
            ReconfigureCallCount++;
            return true;
        }

        public void RequestKeyframe()
        {
            ForceIdrCallCount++;
        }

        public void Dispose()
        {
            IsActive = false;
        }
    }

    private sealed class TestDisplayTopologyWatcher : IDisplayTopologyWatcher
    {
        private DisplayTopology _currentTopology;

        public event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;

        public DisplayTopology CurrentTopology => Volatile.Read(ref _currentTopology);

        public TestDisplayTopologyWatcher(DisplayTopology initialTopology)
        {
            _currentTopology = initialTopology;
        }

        public void RaiseTopologyChanged(DisplayTopology newTopology, DisplayTopologyChangeType changeType, string description)
        {
            var oldTopology = Volatile.Read(ref _currentTopology);
            Volatile.Write(ref _currentTopology, newTopology);
            TopologyChanged?.Invoke(this, new DisplayTopologyChangedEventArgs(oldTopology, newTopology, changeType, description));
        }

        public void Refresh()
        {
        }

        public void Dispose()
        {
        }
    }

    private static (DisplayTopology TopologyG1, DisplayOutputInfo Display0_4K, DisplayOutputInfo Display1_1080p) CreateTopologyG1()
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true)
        };

        var d0 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 3840,
            Height: 2160,
            RefreshRateNumerator: 120,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: @"\\.\DISPLAY1",
            FriendlyName: "Primary 4K OLED",
            MonitorHandle: (IntPtr)0x1001,
            DesktopBounds: new DesktopBounds(0, 0, 3840, 2160),
            DpiScale: 150,
            IsPrimary: true
        );

        var d1 = new DisplayOutputInfo(
            DisplayIndex: 1,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY2",
            FriendlyName: "Secondary 1080p Monitor",
            MonitorHandle: (IntPtr)0x1002,
            DesktopBounds: new DesktopBounds(3840, 0, 5760, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        var topologyG1 = new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d0, d1 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 5760, 2160),
            IsHeadless: false,
            TimestampQpc: 1000,
            Generation: 1
        );

        return (topologyG1, d0, d1);
    }

    private static DisplayTopology CreateTopologyG2(DisplayOutputInfo remainingDisplay)
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true)
        };

        return new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { remainingDisplay },
            PrimaryDisplay: remainingDisplay,
            VirtualScreenBounds: remainingDisplay.Bounds,
            IsHeadless: false,
            TimestampQpc: 2000,
            Generation: 2
        );
    }

    private static DisplayTopology CreateHeadlessTopology(ulong generation = 2)
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true)
        };

        return new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 3000,
            Generation: generation
        );
    }

    [Fact]
    public void DxgiDesktopCapturePipeline_ReconfigureSource_UpdatesProperties()
    {
        var topology = DisplayManager.GetDisplayTopology();
        if (topology.IsHeadless || topology.Displays.Count == 0) return;

        var source = CaptureSourceSelector.SelectSource(topology).Source;
        if (source == null) return;

        using var pipeline = new DxgiDesktopCapturePipeline(source);
        if (pipeline.IsAvailable)
        {
            pipeline.Source.Should().NotBeNull();
            pipeline.AdapterIndex.Should().Be(source.AdapterIndex);
            pipeline.OutputIndex.Should().Be(source.OutputIndex);

            // Reconfigure to same or updated source
            bool reconfigured = pipeline.TryReconfigureSource(source);
            reconfigured.Should().BeTrue();
            pipeline.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void UnifiedDesktopCaptureEngine_ReconfigureSource_ExecutesSeamlessly()
    {
        var topology = DisplayManager.GetDisplayTopology();
        if (topology.IsHeadless || topology.Displays.Count == 0) return;

        var source = CaptureSourceSelector.SelectSource(topology).Source;
        if (source == null) return;

        using var engine = new UnifiedDesktopCaptureEngine(source);
        if (engine.IsAvailable)
        {
            engine.Source.Should().NotBeNull();
            engine.Width.Should().BeGreaterThan(0);
            engine.Height.Should().BeGreaterThan(0);

            bool reconfigured = engine.TryReconfigureSource(source);
            reconfigured.Should().BeTrue();
            engine.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public void MoonshineHostStreamingSession_HandleDisplayTopologyChanged_CoordinatesSafely()
    {
        var topology = DisplayManager.GetDisplayTopology();
        if (topology.IsHeadless || topology.Displays.Count == 0) return;

        var oldTopology = new DisplayTopology(
            Adapters: topology.Adapters,
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 100
        );

        using var session = new MoonshineHostStreamingSession();
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: oldTopology,
            newTopology: topology,
            changeType: DisplayTopologyChangeType.DisplayConnected,
            description: "Display connected test"
        );

        // When not running/streaming, it should safely ignore without state change or exceptions
        session.HandleDisplayTopologyChanged(changeArgs);
        session.CurrentTopologyGeneration.Should().Be(0);
        session.State.Should().Be(HostSessionState.Created);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_DisplayTopologyChanged_DynamicReconfigurationAndKeyframeHandover()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        // 1. Initial selection matches 1080p target resolution, picking Secondary Display 1
        var initialCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );
        var selectResult = CaptureSourceSelector.SelectSource(g1, initialCriteria);
        selectResult.IsSuccess.Should().BeTrue();
        selectResult.Source.Should().NotBeNull();
        selectResult.Source!.OutputIndex.Should().Be(1);
        selectResult.Source.Width.Should().Be(1920);
        selectResult.Source.Height.Should().Be(1080);

        var capturePipeline = new TestDesktopCapturePipeline(selectResult.Source);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 51200 + Random.Shared.Next(0, 500) * 10;
        var config = new HostSessionConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = (ushort)basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capturePipeline,
            encoderEngine: encoderEngine,
            topologyWatcher: watcher);

        await session.StartAsync();

        // Verify initial steady state under generation 1
        session.State.Should().Be(HostSessionState.Streaming);
        session.IsStreaming.Should().BeTrue();
        session.CurrentTopologyGeneration.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(1);
        capturePipeline.ReconfigureCount.Should().Be(0);

        ulong initialKeyframes = session.Metrics.KeyframesRequested;

        // 2. Trigger topology change to G2 where Display 1 is disconnected and generation is 2
        var g2 = CreateTopologyG2(d0);
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "Display 1 disconnected"
        );

        session.HandleDisplayTopologyChanged(changeArgs);

        // 3. Assert generation updates to 2
        session.CurrentTopologyGeneration.Should().Be(2);

        // 4. Assert keyframe was requested for handover
        session.Metrics.KeyframesRequested.Should().Be(initialKeyframes + 1);

        // 5. Assert capture pipeline dynamically reconfigured to remaining Primary Display 0 (4K)
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(0);
        capturePipeline.Source.Width.Should().Be(3840);
        capturePipeline.Source.Height.Should().Be(2160);
        capturePipeline.Source.IsPrimary.Should().BeTrue();

        // 6. Assert session does not fault or throw
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_HeadlessTransition_HandlesGracefullyWithoutCrash()
    {
        var (g1, d0, _) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d0.Descriptor);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 56200 + Random.Shared.Next(0, 500) * 10;
        var config = new HostSessionConfig
        {
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 20000,
            LocalVideoPort = (ushort)basePort,
            LocalAudioPort = (ushort)(basePort + 1),
            LocalControlFeedbackPort = (ushort)(basePort + 2),
            ClientVideoPort = (ushort)(basePort + 3),
            ClientAudioPort = (ushort)(basePort + 4),
            ClientControlFeedbackPort = (ushort)(basePort + 5)
        };

        await using var session = new MoonshineHostStreamingSession(
            config: config,
            capturePipeline: capturePipeline,
            encoderEngine: encoderEngine,
            topologyWatcher: watcher);

        await session.StartAsync();

        session.State.Should().Be(HostSessionState.Streaming);
        session.CurrentTopologyGeneration.Should().Be(1);
        ulong initialKeyframes = session.Metrics.KeyframesRequested;

        // 1. Transition to Headless Topology (0 displays attached, generation 2)
        var gHeadless = CreateHeadlessTopology(generation: 2);
        var headlessArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: gHeadless,
            changeType: DisplayTopologyChangeType.HeadlessStateChanged,
            description: "All physical displays disconnected"
        );

        session.HandleDisplayTopologyChanged(headlessArgs);

        // 2. Assert generation increments to 2 and session remains stable without faulting
        session.CurrentTopologyGeneration.Should().Be(2);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();
        capturePipeline.ReconfigureCount.Should().Be(0);

        // 3. Transition back from headless to active topology (generation 3)
        var gRestored = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d0 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: d0.Bounds,
            IsHeadless: false,
            TimestampQpc: 4000,
            Generation: 3
        );

        var restoreArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: gHeadless,
            newTopology: gRestored,
            changeType: DisplayTopologyChangeType.HeadlessStateChanged,
            description: "Physical display reconnected"
        );

        session.HandleDisplayTopologyChanged(restoreArgs);

        // 4. Assert generation updates to 3, keyframe forced, and capture source reconfigured
        session.CurrentTopologyGeneration.Should().Be(3);
        session.Metrics.KeyframesRequested.Should().Be(initialKeyframes + 1);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(0);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }
}
