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
        public bool FailReconfigure { get; set; }
        public bool FailRecovery { get; set; }
        public bool ThrowOnReconfigure { get; set; }
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
            if (FailRecovery) return false;
            IsAvailable = true;
            return true;
        }

        public bool TryReconfigureSource(CaptureSourceDescriptor source)
        {
            ReconfigureCount++;
            if (ThrowOnReconfigure)
            {
                // SIMULATED: Test mock exception reproducing D3D11 capture device loss during reconfiguration.
                throw new InvalidOperationException("Simulated D3D11 capture device lost during reconfiguration.");
            }
            if (FailReconfigure)
            {
                return false;
            }
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
        public EncoderImplementationKind ImplementationKind { get; set; } = EncoderImplementationKind.SyntheticTest;
        public bool IsHardwareAccelerated { get; set; }
        public bool HasProducedValidOutput { get; set; } = true;
        public Type ImplementationType => GetType();
        public EncoderRuntimeState RuntimeState => IsActive ? EncoderRuntimeState.Ready : EncoderRuntimeState.Disposed;
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

        public EncodeSubmissionResult SubmitFrame(
            IntPtr d3dTexture,
            bool forceIdr,
            Span<byte> outBitstream,
            out int bytesWritten)
        {
            if (!TryEncodeFrame(d3dTexture, forceIdr, out var desc, outBitstream, out bytesWritten))
            {
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: IsActive ? EncoderResult.EncoderFailure : EncoderResult.DeviceLost
                );
            }

            return new EncodeSubmissionResult(
                Submitted: true,
                OutputAvailable: true,
                KeyFrame: desc.IsKeyframe != 0,
                BytesWritten: bytesWritten,
                PacketDesc: desc,
                Result: EncoderResult.Success
            );
        }

        public bool TryPollPacket(
            Span<byte> outBitstream,
            out MoonshineEncodedPacketDesc desc,
            out int bytesWritten)
        {
            desc = default;
            bytesWritten = 0;
            return false;
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

    private static DisplayTopology CreateHeterogeneousTopology(ulong generation = 2)
    {
        var adapter0 = new DisplayAdapterInfo(
            AdapterIndex: 0,
            AdapterLuid: 0x1000,
            Description: "NVIDIA GeForce RTX 4090",
            DedicatedVideoMemoryBytes: 24UL * 1024 * 1024 * 1024,
            IsHardware: true
        );

        var adapter1 = new DisplayAdapterInfo(
            AdapterIndex: 1,
            AdapterLuid: 0x2000,
            Description: "Intel UHD Graphics 770",
            DedicatedVideoMemoryBytes: 128UL * 1024 * 1024,
            IsHardware: true
        );

        var adapter2 = new DisplayAdapterInfo(
            AdapterIndex: 2,
            AdapterLuid: 0x3000,
            Description: "DisplayLink USB Graphics Device",
            DedicatedVideoMemoryBytes: 0UL,
            IsHardware: false
        );

        var adapters = new[] { adapter0, adapter1, adapter2 };

        // Adapter 0: NVIDIA RTX 4090
        var d0 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 3840,
            Height: 2160,
            RefreshRateNumerator: 144,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: @"\\.\DISPLAY1",
            FriendlyName: "NVIDIA RTX 4090 Primary 4K 144Hz HDR",
            MonitorHandle: (IntPtr)0x10001,
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
            FriendlyName: "NVIDIA RTX 4090 Left 1080p 60Hz SDR",
            MonitorHandle: (IntPtr)0x10002,
            DesktopBounds: new DesktopBounds(-1920, 0, 0, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        var d2 = new DisplayOutputInfo(
            DisplayIndex: 2,
            AdapterIndex: 0,
            Width: 1440,
            Height: 2560,
            RefreshRateNumerator: 144,
            RefreshRateDenominator: 1,
            Rotation: 90,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY3",
            FriendlyName: "NVIDIA RTX 4090 Right Portrait 1440p 144Hz SDR",
            MonitorHandle: (IntPtr)0x10003,
            DesktopBounds: new DesktopBounds(3840, 0, 5280, 2560),
            DpiScale: 125,
            IsPrimary: false
        );

        var d3 = new DisplayOutputInfo(
            DisplayIndex: 3,
            AdapterIndex: 0,
            Width: 3840,
            Height: 2160,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: @"\\.\DISPLAY4",
            FriendlyName: "NVIDIA RTX 4090 Top 4K 60Hz HDR",
            MonitorHandle: (IntPtr)0x10004,
            DesktopBounds: new DesktopBounds(0, -2160, 3840, 0),
            DpiScale: 175,
            IsPrimary: false
        );

        var d4 = new DisplayOutputInfo(
            DisplayIndex: 4,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: false,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY5",
            FriendlyName: "NVIDIA RTX 4090 Detached Output SDR",
            MonitorHandle: IntPtr.Zero,
            DesktopBounds: new DesktopBounds(0, 0, 0, 0),
            DpiScale: 100,
            IsPrimary: false
        );

        // Adapter 1: Intel UHD Graphics 770
        var d5 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 1,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY6",
            FriendlyName: "Intel UHD 770 Far Right 1080p 60Hz SDR",
            MonitorHandle: (IntPtr)0x20001,
            DesktopBounds: new DesktopBounds(5280, 0, 7200, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        var d6 = new DisplayOutputInfo(
            DisplayIndex: 1,
            AdapterIndex: 1,
            Width: 2560,
            Height: 1440,
            RefreshRateNumerator: 165,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: @"\\.\DISPLAY7",
            FriendlyName: "Intel UHD 770 Bottom 1440p 165Hz HDR",
            MonitorHandle: (IntPtr)0x20002,
            DesktopBounds: new DesktopBounds(0, 2160, 2560, 3600),
            DpiScale: 125,
            IsPrimary: false
        );

        // Adapter 2: DisplayLink USB Graphics
        var d7 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 2,
            Width: 1280,
            Height: 800,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 180,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY8",
            FriendlyName: "DisplayLink Inverted 800p 60Hz SDR",
            MonitorHandle: (IntPtr)0x30001,
            DesktopBounds: new DesktopBounds(7200, 0, 8480, 800),
            DpiScale: 100,
            IsPrimary: false
        );

        var displays = new[] { d0, d1, d2, d3, d4, d5, d6, d7 };
        var virtualBounds = new DesktopBounds(-1920, -2160, 8480, 3600);

        return new DisplayTopology(
            Adapters: adapters,
            Displays: displays,
            PrimaryDisplay: d0,
            VirtualScreenBounds: virtualBounds,
            IsHeadless: false,
            TimestampQpc: 987654321,
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

    [Fact]
    public async Task MoonshineHostStreamingSession_DisplayTopologyChanged_SemanticKeyframeDeliveryVerified()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 57200 + Random.Shared.Next(0, 500) * 10;
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

        var stopwatch = Stopwatch.StartNew();
        while (session.Metrics.TotalFramesEncoded == 0 && stopwatch.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(10);
        }
        session.Metrics.TotalFramesEncoded.Should().BeGreaterThan(0);
        int initialForceIdrCount = encoderPipeline.ForceIdrCallCount;
        initialForceIdrCount.Should().BeGreaterThan(0);

        var g2 = CreateTopologyG2(d0);
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "Display 1 disconnected"
        );

        session.HandleDisplayTopologyChanged(changeArgs);

        stopwatch.Restart();
        while (encoderPipeline.ForceIdrCallCount <= initialForceIdrCount && stopwatch.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(10);
        }

        encoderPipeline.ForceIdrCallCount.Should().BeGreaterThan(initialForceIdrCount);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_ReconfigurationFailure_TriggersCaptureRecovery()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor)
        {
            FailReconfigure = true
        };
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 58200 + Random.Shared.Next(0, 500) * 10;
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
        capturePipeline.ReconfigureCount.Should().Be(0);
        capturePipeline.RecoverCount.Should().Be(0);

        var g2 = CreateTopologyG2(d0);
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "Display 1 disconnected with failing reconfigure"
        );

        session.HandleDisplayTopologyChanged(changeArgs);

        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.RecoverCount.Should().BeGreaterThan(0);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_FullHeadlessLifecycle_PausesAndResumesSeamlessly()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 59200 + Random.Shared.Next(0, 500) * 10;
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

        // Phase 1: G1 Active (Streaming)
        session.State.Should().Be(HostSessionState.Streaming);
        session.IsStreaming.Should().BeTrue();
        session.CurrentTopologyGeneration.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(1);
        capturePipeline.ReconfigureCount.Should().Be(0);
        ulong g1Keyframes = session.Metrics.KeyframesRequested;

        // Phase 2: G2 Headless (Streaming / uncorrupted)
        var gHeadless = CreateHeadlessTopology(generation: 2);
        var headlessArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: gHeadless,
            changeType: DisplayTopologyChangeType.HeadlessStateChanged,
            description: "All physical displays disconnected"
        );

        session.HandleDisplayTopologyChanged(headlessArgs);

        session.CurrentTopologyGeneration.Should().Be(2);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();
        capturePipeline.ReconfigureCount.Should().Be(0);
        session.Metrics.KeyframesRequested.Should().Be(g1Keyframes);

        // Phase 3: G3 Display Reconnected (Reconfigured, IDR keyframe emitted, Generation 3)
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

        session.CurrentTopologyGeneration.Should().Be(3);
        session.Metrics.KeyframesRequested.Should().Be(g1Keyframes + 1);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(0);
        capturePipeline.Source.Width.Should().Be(3840);
        capturePipeline.Source.Height.Should().Be(2160);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_ReconfigurationFailure_WithRecoveryFailure_MaintainsSessionLiveness()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor)
        {
            FailReconfigure = true,
            FailRecovery = true
        };
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 60200 + Random.Shared.Next(0, 500) * 10;
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
        capturePipeline.ReconfigureCount.Should().Be(0);
        capturePipeline.RecoverCount.Should().Be(0);

        var g2 = CreateTopologyG2(d0);
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "Display 1 disconnected with failing reconfigure and failing recovery"
        );

        session.HandleDisplayTopologyChanged(changeArgs);

        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.RecoverCount.Should().Be(1);
        session.Metrics.KeyframesRequested.Should().Be(0);
        session.CurrentTopologyGeneration.Should().Be(2);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_ReconfigurationThrowsException_RecoversCleanlyWithoutCrashingSession()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor)
        {
            ThrowOnReconfigure = true
        };
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 61200 + Random.Shared.Next(0, 500) * 10;
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
        capturePipeline.ReconfigureCount.Should().Be(0);
        capturePipeline.RecoverCount.Should().Be(0);

        var g2 = CreateTopologyG2(d0);
        var changeArgs = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "Display 1 disconnected with throwing reconfigure"
        );

        session.HandleDisplayTopologyChanged(changeArgs);

        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.RecoverCount.Should().Be(1);
        capturePipeline.IsAvailable.Should().BeTrue();
        session.CurrentTopologyGeneration.Should().Be(2);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_SequentialReconfigurationFailures_RecoversOnSubsequentSuccess()
    {
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 62200 + Random.Shared.Next(0, 500) * 10;
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

        // Step 1: G1 -> G2 (reconfigure fails)
        capturePipeline.FailReconfigure = true;
        var g2 = CreateTopologyG2(d0);
        var changeArgs2 = new DisplayTopologyChangedEventArgs(
            oldTopology: g1,
            newTopology: g2,
            changeType: DisplayTopologyChangeType.DisplayDisconnected,
            description: "First failure: Display 1 disconnected"
        );
        session.HandleDisplayTopologyChanged(changeArgs2);

        session.CurrentTopologyGeneration.Should().Be(2);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.RecoverCount.Should().Be(1);
        session.State.Should().Be(HostSessionState.Streaming);

        // Step 2: G2 -> G3 (reconfigure fails again)
        var d2_1440p = new DisplayOutputInfo(
            DisplayIndex: 2,
            AdapterIndex: 0,
            Width: 2560,
            Height: 1440,
            RefreshRateNumerator: 144,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: @"\\.\DISPLAY3",
            FriendlyName: "Tertiary 1440p Monitor",
            MonitorHandle: (IntPtr)0x1003,
            DesktopBounds: new DesktopBounds(3840, 0, 6400, 1440),
            DpiScale: 100,
            IsPrimary: false
        );
        var g3 = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d0, d2_1440p },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 6400, 2160),
            IsHeadless: false,
            TimestampQpc: 3000,
            Generation: 3
        );
        var changeArgs3 = new DisplayTopologyChangedEventArgs(
            oldTopology: g2,
            newTopology: g3,
            changeType: DisplayTopologyChangeType.DisplayConnected,
            description: "Second failure: 1440p monitor attached"
        );
        session.HandleDisplayTopologyChanged(changeArgs3);

        session.CurrentTopologyGeneration.Should().Be(3);
        capturePipeline.ReconfigureCount.Should().Be(2);
        capturePipeline.RecoverCount.Should().Be(2);
        session.State.Should().Be(HostSessionState.Streaming);

        // Step 3: G3 -> G4 (reconfigure succeeds)
        capturePipeline.FailReconfigure = false;
        var g4 = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d0, d1 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 5760, 2160),
            IsHeadless: false,
            TimestampQpc: 4000,
            Generation: 4
        );
        var changeArgs4 = new DisplayTopologyChangedEventArgs(
            oldTopology: g3,
            newTopology: g4,
            changeType: DisplayTopologyChangeType.DisplayConnected,
            description: "Third transition: 1080p display restored, reconfigure succeeds"
        );
        session.HandleDisplayTopologyChanged(changeArgs4);

        session.CurrentTopologyGeneration.Should().Be(4);
        capturePipeline.ReconfigureCount.Should().Be(3);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(1);
        capturePipeline.Source.Width.Should().Be(1920);
        capturePipeline.Source.Height.Should().Be(1080);
        session.Metrics.KeyframesRequested.Should().Be(initialKeyframes + 3);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_HeadlessWithActiveFrameLoop_DoesNotFaultAndResumesStreamingOnDisplayRestoration()
    {
        var (g1, _, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline(d1.Descriptor);
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(g1);

        int basePort = 63200 + Random.Shared.Next(0, 500) * 10;
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

        // Phase 1: Wait for frame loop to stream initial frames under G1
        var stopwatch = Stopwatch.StartNew();
        while (session.Metrics.TotalFramesEncoded == 0 && stopwatch.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(10);
        }
        session.Metrics.TotalFramesEncoded.Should().BeGreaterThan(0);
        ulong g1FramesEncoded = session.Metrics.TotalFramesEncoded;
        ulong g1Keyframes = session.Metrics.KeyframesRequested;

        // Phase 2: Transition G1 -> G2 (Headless blackout)
        capturePipeline.IsAvailable = false;
        var gHeadless = CreateHeadlessTopology(generation: 2);
        watcher.RaiseTopologyChanged(
            gHeadless,
            DisplayTopologyChangeType.HeadlessStateChanged,
            "All physical displays disconnected"
        );

        // Frame loop continues spinning during headless blackout without faulting
        await Task.Delay(100);

        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();
        session.CurrentTopologyGeneration.Should().Be(2);
        ulong framesDuringBlackout = session.Metrics.TotalFramesEncoded;

        // Phase 3: Transition G2 -> G3 (Display restored)
        capturePipeline.IsAvailable = true;
        var gRestored = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d1 },
            PrimaryDisplay: d1,
            VirtualScreenBounds: d1.Bounds,
            IsHeadless: false,
            TimestampQpc: 5000,
            Generation: 3
        );
        watcher.RaiseTopologyChanged(
            gRestored,
            DisplayTopologyChangeType.HeadlessStateChanged,
            "Display restored"
        );

        stopwatch.Restart();
        while (session.Metrics.TotalFramesEncoded <= framesDuringBlackout && stopwatch.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(10);
        }

        session.Metrics.TotalFramesEncoded.Should().BeGreaterThan(framesDuringBlackout);
        session.CurrentTopologyGeneration.Should().Be(3);
        session.Metrics.KeyframesRequested.Should().BeGreaterThan(g1Keyframes);
        capturePipeline.ReconfigureCount.Should().Be(1);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_HeadlessToMultiAdapterComplexTopology_SelectsDiscreteGpuAndEmitsIdr()
    {
        var gHeadless = CreateHeadlessTopology(generation: 1);

        var capturePipeline = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(gHeadless);

        int basePort = 64200 + Random.Shared.Next(0, 500) * 10;
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

        var gHeterogeneous = CreateHeterogeneousTopology(generation: 2);
        watcher.RaiseTopologyChanged(
            gHeterogeneous,
            DisplayTopologyChangeType.DisplayConnected,
            "Restored into 8-display 3-adapter heterogeneous topology"
        );

        session.CurrentTopologyGeneration.Should().Be(2);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.AdapterIndex.Should().Be(0);
        capturePipeline.Source.OutputIndex.Should().Be(1);
        capturePipeline.Source.Width.Should().Be(1920);
        capturePipeline.Source.Height.Should().Be(1080);
        session.Metrics.KeyframesRequested.Should().Be(initialKeyframes + 1);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_HeadlessToDisplay_WithExactResolutionMatch()
    {
        var gHeadless = CreateHeadlessTopology(generation: 1);
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline();
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(gHeadless);

        int basePort = 65200 + Random.Shared.Next(0, 500) * 10;
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

        var gDualDisplay = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d0, d1 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 5760, 2160),
            IsHeadless: false,
            TimestampQpc: 2000,
            Generation: 2
        );

        watcher.RaiseTopologyChanged(
            gDualDisplay,
            DisplayTopologyChangeType.DisplayConnected,
            "Dual display connected (4K Primary + 1080p Secondary)"
        );

        session.CurrentTopologyGeneration.Should().Be(2);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(1);
        capturePipeline.Source.Width.Should().Be(1920);
        capturePipeline.Source.Height.Should().Be(1080);
        capturePipeline.Source.IsPrimary.Should().BeFalse();
        session.Metrics.KeyframesRequested.Should().Be(initialKeyframes + 1);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }

    [Fact]
    public async Task MoonshineHostStreamingSession_HeadlessToDisplay_WithReconfigureAndRecoveryFailure_MaintainsSessionLiveness()
    {
        var gHeadless = CreateHeadlessTopology(generation: 1);
        var (g1, d0, d1) = CreateTopologyG1();

        var capturePipeline = new TestDesktopCapturePipeline
        {
            FailReconfigure = true,
            FailRecovery = true
        };
        var encoderPipeline = new TestVideoEncoderPipeline(1920, 1080, 60);
        using var encoderEngine = new UnifiedHardwareEncoderEngine(encoderPipeline);
        using var watcher = new TestDisplayTopologyWatcher(gHeadless);

        int basePort = 66200 + Random.Shared.Next(0, 500) * 10;
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
        capturePipeline.ReconfigureCount.Should().Be(0);
        capturePipeline.RecoverCount.Should().Be(0);
        session.Metrics.KeyframesRequested.Should().Be(0);

        // Transition: Headless (G1) -> Display attached (G2) with failing capture reconfigure and recovery
        var gDisplay = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d1 },
            PrimaryDisplay: d1,
            VirtualScreenBounds: d1.Bounds,
            IsHeadless: false,
            TimestampQpc: 2000,
            Generation: 2
        );

        watcher.RaiseTopologyChanged(
            gDisplay,
            DisplayTopologyChangeType.DisplayConnected,
            "Display connected but capture hardware device lost"
        );

        // Asserts session remains alive and streaming, generation advances, and keyframe is not emitted on failure
        session.CurrentTopologyGeneration.Should().Be(2);
        capturePipeline.ReconfigureCount.Should().Be(1);
        capturePipeline.RecoverCount.Should().Be(1);
        session.Metrics.KeyframesRequested.Should().Be(0);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        // Secondary transition: Display topology changes (G3) with successful reconfigure
        capturePipeline.FailReconfigure = false;
        capturePipeline.FailRecovery = false;

        var gDisplayG3 = new DisplayTopology(
            Adapters: g1.Adapters,
            Displays: new[] { d1 },
            PrimaryDisplay: d1,
            VirtualScreenBounds: d1.Bounds,
            IsHeadless: false,
            TimestampQpc: 3000,
            Generation: 3
        );

        watcher.RaiseTopologyChanged(
            gDisplayG3,
            DisplayTopologyChangeType.DisplayModeChanged,
            "Capture driver reset recovered and reconfigured successfully"
        );

        session.CurrentTopologyGeneration.Should().Be(3);
        capturePipeline.ReconfigureCount.Should().Be(2);
        capturePipeline.RecoverCount.Should().Be(1);
        capturePipeline.Source.Should().NotBeNull();
        capturePipeline.Source!.OutputIndex.Should().Be(1);
        session.Metrics.KeyframesRequested.Should().Be(1);
        session.State.Should().Be(HostSessionState.Streaming);
        session.LastError.Should().BeNull();

        await session.StopAsync();
        session.State.Should().Be(HostSessionState.Terminated);
    }
}
