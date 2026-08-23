using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class ComplexDisplayTopologyTests
{
    /// <summary>
    /// Constructs a representative 8-display, 3-adapter heterogeneous Windows display topology.
    /// Includes discrete GPU (NVIDIA RTX 4090), integrated GPU (Intel UHD 770), and USB display (DisplayLink).
    /// Encompasses primary, negative coordinates (left and top), portrait, detached, duplicate 1080p, and inverted displays.
    /// </summary>
    public static DisplayTopology CreateHeterogeneousTopology()
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
        // Display 0: 3840x2160 @ 144Hz, HDR 10-bit, DPI 150%, Primary display, Bounds: [0, 0, 3840, 2160], Rotation: 0
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

        // Display 1: 1920x1080 @ 60Hz, SDR 8-bit, DPI 100%, Bounds: [-1920, 0, 0, 1080] (negative X coordinate left of primary), Rotation: 0
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

        // Display 2: 1440x2560 @ 144Hz, SDR 8-bit, DPI 125%, Bounds: [3840, 0, 5280, 2560], Rotation: 90 (Portrait)
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

        // Display 3: 3840x2160 @ 60Hz, HDR 10-bit, DPI 175%, Bounds: [0, -2160, 3840, 0] (negative Y coordinate above primary), Rotation: 0
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

        // Display 4: 1920x1080 @ 60Hz, SDR 8-bit, IsAttachedToDesktop = false (Detached / Inactive output)
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
        // Display 5: 1920x1080 @ 60Hz, SDR 8-bit, DPI 100%, Bounds: [5280, 0, 7200, 1080], Rotation: 0 (Exact duplicate res/fps as Display 1, but on secondary adapter)
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

        // Display 6: 2560x1440 @ 165Hz, HDR 10-bit, DPI 125%, Bounds: [0, 2160, 2560, 3600], Rotation: 0
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
        // Display 7: 1280x800 @ 60Hz, SDR 8-bit, DPI 100%, Bounds: [7200, 0, 8480, 800], Rotation: 180 (Inverted)
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
            Generation: 1
        );
    }

    [Fact]
    public void HeterogeneousTopology_VirtualScreenBoundsAndAttributes_AreAccuratelyConstructed()
    {
        var topology = CreateHeterogeneousTopology();

        topology.Should().NotBeNull();
        topology.IsHeadless.Should().BeFalse();
        topology.Adapters.Should().HaveCount(3);
        topology.Displays.Should().HaveCount(8);
        topology.PrimaryDisplay.Should().NotBeNull();
        topology.PrimaryDisplay!.DeviceName.Should().Be(@"\\.\DISPLAY1");
        topology.PrimaryDisplay.IsPrimary.Should().BeTrue();

        // Validate virtual screen bounds bounding box spanning negative and positive spans
        topology.VirtualScreenBounds.Left.Should().Be(-1920);
        topology.VirtualScreenBounds.Top.Should().Be(-2160);
        topology.VirtualScreenBounds.Right.Should().Be(8480);
        topology.VirtualScreenBounds.Bottom.Should().Be(3600);
        topology.VirtualScreenBounds.Width.Should().Be(10400);
        topology.VirtualScreenBounds.Height.Should().Be(5760);
    }

    [Fact]
    public void MatchResolution_WithDuplicateResolutionAndRefreshRate_PicksDiscreteGpuFirstViaTieBreaker()
    {
        var topology = CreateHeterogeneousTopology();

        // Request 1920x1080 @ 60Hz SDR. Display 1 (Adapter 0) and Display 5 (Adapter 1) have identical resolution and refresh rate.
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            RequireHdr: false
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();

        // Adapter tie-breaker selects AdapterIndex 0 (discrete NVIDIA RTX 4090) over AdapterIndex 1 (integrated Intel UHD 770)
        result.Source!.AdapterIndex.Should().Be(0);
        result.Source.OutputIndex.Should().Be(1);
        result.Source.DeviceName.Should().Be(@"\\.\DISPLAY2");
        result.Source.FriendlyName.Should().Be("NVIDIA RTX 4090 Left 1080p 60Hz SDR");
        result.Source.Width.Should().Be(1920);
        result.Source.Height.Should().Be(1080);
        result.Source.RefreshRateHz.Should().Be(60.0);
        result.Source.IsHdr.Should().BeFalse();
    }

    [Fact]
    public void MatchResolution_WithNegativeCoordinates_CalculatesCorrectlyWithoutOverflow()
    {
        var topology = CreateHeterogeneousTopology();

        // 1. Monitor positioned at negative X coordinates (Display 1: [-1920, 0, 0, 1080])
        var criteriaNegativeX = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            RequireHdr: false
        );

        var resultNegativeX = CaptureSourceSelector.SelectSource(topology, criteriaNegativeX);

        resultNegativeX.IsSuccess.Should().BeTrue();
        resultNegativeX.Source.Should().NotBeNull();
        resultNegativeX.Source!.OutputIndex.Should().Be(1);
        resultNegativeX.Source.DesktopBounds.Left.Should().Be(-1920);
        resultNegativeX.Source.DesktopBounds.Top.Should().Be(0);
        resultNegativeX.Source.DesktopBounds.Right.Should().Be(0);
        resultNegativeX.Source.DesktopBounds.Bottom.Should().Be(1080);
        resultNegativeX.Source.DesktopBounds.Width.Should().Be(1920);
        resultNegativeX.Source.DesktopBounds.Height.Should().Be(1080);

        // 2. Monitor positioned at negative Y coordinates (Display 3: [0, -2160, 3840, 0])
        var criteriaNegativeY = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 3840,
            TargetHeight: 2160,
            TargetFps: 60.0,
            RequireHdr: true
        );

        var resultNegativeY = CaptureSourceSelector.SelectSource(topology, criteriaNegativeY);

        resultNegativeY.IsSuccess.Should().BeTrue();
        resultNegativeY.Source.Should().NotBeNull();
        resultNegativeY.Source!.OutputIndex.Should().Be(3);
        resultNegativeY.Source.DesktopBounds.Left.Should().Be(0);
        resultNegativeY.Source.DesktopBounds.Top.Should().Be(-2160);
        resultNegativeY.Source.DesktopBounds.Right.Should().Be(3840);
        resultNegativeY.Source.DesktopBounds.Bottom.Should().Be(0);
        resultNegativeY.Source.DesktopBounds.Width.Should().Be(3840);
        resultNegativeY.Source.DesktopBounds.Height.Should().Be(2160);
    }

    [Fact]
    public void MatchResolution_WithRotatedDisplays_EvaluatesOrientations()
    {
        var topology = CreateHeterogeneousTopology();

        // 1. Request Portrait resolution (1440x2560 @ 144Hz) matching Display 2 (Rotation: 90)
        var portraitCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1440,
            TargetHeight: 2560,
            TargetFps: 144.0
        );

        var portraitResult = CaptureSourceSelector.SelectSource(topology, portraitCriteria);

        portraitResult.IsSuccess.Should().BeTrue();
        portraitResult.Source.Should().NotBeNull();
        portraitResult.Source!.OutputIndex.Should().Be(2);
        portraitResult.Source.AdapterIndex.Should().Be(0);
        portraitResult.Source.Width.Should().Be(1440);
        portraitResult.Source.Height.Should().Be(2560);
        portraitResult.Source.RefreshRateHz.Should().Be(144.0);
        portraitResult.Source.DeviceName.Should().Be(@"\\.\DISPLAY3");
        portraitResult.Source.DesktopBounds.Left.Should().Be(3840);
        portraitResult.Source.DesktopBounds.Right.Should().Be(5280);

        // 2. Request 1280x800 @ 60Hz matching Display 7 on Adapter 2 (Rotation: 180)
        var invertedCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1280,
            TargetHeight: 800,
            TargetFps: 60.0
        );

        var invertedResult = CaptureSourceSelector.SelectSource(topology, invertedCriteria);

        invertedResult.IsSuccess.Should().BeTrue();
        invertedResult.Source.Should().NotBeNull();
        invertedResult.Source!.AdapterIndex.Should().Be(2);
        invertedResult.Source.OutputIndex.Should().Be(0);
        invertedResult.Source.Width.Should().Be(1280);
        invertedResult.Source.Height.Should().Be(800);
        invertedResult.Source.DeviceName.Should().Be(@"\\.\DISPLAY8");
    }

    [Fact]
    public void MatchResolution_WithStrictHdrOnDuplicateResolution_PicksHdrCandidateAcrossAdapters()
    {
        var topology = CreateHeterogeneousTopology();

        // 1. Request 4K 144Hz HDR: matches Display 0 (Adapter 0, Primary)
        var criteria4k144 = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 3840,
            TargetHeight: 2160,
            TargetFps: 144.0,
            RequireHdr: true
        );

        var result4k144 = CaptureSourceSelector.SelectSource(topology, criteria4k144);
        result4k144.IsSuccess.Should().BeTrue();
        result4k144.Source.Should().NotBeNull();
        result4k144.Source!.AdapterIndex.Should().Be(0);
        result4k144.Source.OutputIndex.Should().Be(0);
        result4k144.Source.IsHdr.Should().BeTrue();
        result4k144.Source.Width.Should().Be(3840);
        result4k144.Source.Height.Should().Be(2160);

        // 2. Request 1440p 165Hz HDR: matches Display 6 on Adapter 1 (Intel UHD 770)
        var criteria1440p165 = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 2560,
            TargetHeight: 1440,
            TargetFps: 165.0,
            RequireHdr: true
        );

        var result1440p165 = CaptureSourceSelector.SelectSource(topology, criteria1440p165);
        result1440p165.IsSuccess.Should().BeTrue();
        result1440p165.Source.Should().NotBeNull();
        result1440p165.Source!.AdapterIndex.Should().Be(1);
        result1440p165.Source.OutputIndex.Should().Be(1);
        result1440p165.Source.IsHdr.Should().BeTrue();
        result1440p165.Source.Width.Should().Be(2560);
        result1440p165.Source.Height.Should().Be(1440);

        // 3. Request 1080p 60Hz with RequireHdr = true.
        // Both 1080p displays (Display 1 on Adapter 0 and Display 5 on Adapter 1) are SDR, so both must be strictly skipped.
        // The selector should choose the closest HDR display (Display 6 on Adapter 1: 2560x1440 @ 165Hz).
        var criteria1080pHdr = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            RequireHdr: true
        );

        var result1080pHdr = CaptureSourceSelector.SelectSource(topology, criteria1080pHdr);
        result1080pHdr.IsSuccess.Should().BeTrue();
        result1080pHdr.Source.Should().NotBeNull();
        result1080pHdr.Source!.IsHdr.Should().BeTrue();
        result1080pHdr.Source.AdapterIndex.Should().Be(1);
        result1080pHdr.Source.OutputIndex.Should().Be(1);
    }

    [Fact]
    public void DetachedDisplays_AreExcludedFromAutomaticSelection()
    {
        var topology = CreateHeterogeneousTopology();

        // 1. Direct query targeting detached Display 4 (Adapter 0, Display 4) with FailClosed policy must fail
        var detachedCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 0,
            PreferredDisplayIndex: 4,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var detachedResult = CaptureSourceSelector.SelectSource(topology, detachedCriteria);
        detachedResult.IsSuccess.Should().BeFalse();
        detachedResult.Source.Should().BeNull();
        detachedResult.FailureReason.Should().Contain("No attached display output matching criteria");

        // 2. Specific device name targeting detached Display 5 ("\\.\DISPLAY5") with FailClosed must fail
        var deviceCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDeviceName,
            PreferredDeviceName: @"\\.\DISPLAY5",
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var deviceResult = CaptureSourceSelector.SelectSource(topology, deviceCriteria);
        deviceResult.IsSuccess.Should().BeFalse();
        deviceResult.Source.Should().BeNull();

        // 3. Topology where all displays are detached returns Headless result
        var allDetachedDisplays = new[]
        {
            new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: false,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY1"
            )
        };

        var allDetachedTopology = new DisplayTopology(
            Adapters: topology.Adapters,
            Displays: allDetachedDisplays,
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: false,
            TimestampQpc: 1000
        );

        var allDetachedResult = CaptureSourceSelector.SelectSource(allDetachedTopology);
        allDetachedResult.IsSuccess.Should().BeFalse();
        allDetachedResult.IsHeadless.Should().BeTrue();
        allDetachedResult.Source.Should().BeNull();
        allDetachedResult.FailureReason.Should().Contain("detached");
    }

    [Fact]
    public void SelectSource_LargeComplexTopology_ZeroGCAllocations()
    {
        var topology = CreateHeterogeneousTopology();

        var matchCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        var primaryCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.PrimaryDisplay
        );

        var specificIndexCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 0,
            PreferredDisplayIndex: 2
        );

        var specificDeviceCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDeviceName,
            PreferredDeviceName: @"\\.\DISPLAY3"
        );

        var specificHandleCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificMonitorHandle,
            PreferredMonitorHandle: (IntPtr)0x10003
        );

        // Warm up JIT compiler
        for (int i = 0; i < 200; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, matchCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, primaryCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificIndexCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificDeviceCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificHandleCriteria);
        }

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, matchCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, primaryCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificIndexCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificDeviceCriteria);
            _ = CaptureSourceSelector.SelectSource(topology, specificHandleCriteria);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        long allocated = bytesAfter - bytesBefore;

        allocated.Should().Be(0, "Hot path capture source resolution across complex topologies must not incur heap allocations.");
    }

    [Fact]
    public void MatchResolution_TieBreaking_CompleteHierarchyValidation()
    {
        var adapters = new[]
        {
            new DisplayAdapterInfo(0, 0x1000, "Adapter 0", 8_000_000_000, true),
            new DisplayAdapterInfo(1, 0x2000, "Adapter 1", 8_000_000_000, true)
        };

        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        // Stage 1: Lower total distance score takes precedence
        {
            var dispExact = new DisplayOutputInfo(
                DisplayIndex: 1,
                AdapterIndex: 1,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_EXACT",
                IsPrimary: false
            );

            var dispSlightlyOff = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 59,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_OFF",
                IsPrimary: false
            );

            // Even though dispSlightlyOff is on Adapter 0 and has lower DisplayIndex, dispExact has lower total score
            var top1 = new DisplayTopology(adapters, new[] { dispSlightlyOff, dispExact }, null, DesktopBounds.Empty, false, 1);
            var res1 = CaptureSourceSelector.SelectSource(top1, criteria);
            res1.IsSuccess.Should().BeTrue();
            res1.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_EXACT");

            // Verify order independence
            var top1Rev = new DisplayTopology(adapters, new[] { dispExact, dispSlightlyOff }, null, DesktopBounds.Empty, false, 1);
            var res1Rev = CaptureSourceSelector.SelectSource(top1Rev, criteria);
            res1Rev.IsSuccess.Should().BeTrue();
            res1Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_EXACT");
        }

        // Stage 2: Primary display preferred under identical score
        {
            var dispPrimary = new DisplayOutputInfo(
                DisplayIndex: 1,
                AdapterIndex: 1,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_PRIMARY",
                IsPrimary: true
            );

            var dispNonPrimary = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_NONPRIMARY",
                IsPrimary: false
            );

            var top2 = new DisplayTopology(adapters, new[] { dispNonPrimary, dispPrimary }, dispPrimary, DesktopBounds.Empty, false, 2);
            var res2 = CaptureSourceSelector.SelectSource(top2, criteria);
            res2.IsSuccess.Should().BeTrue();
            res2.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_PRIMARY");
            res2.Source.IsPrimary.Should().BeTrue();

            var top2Rev = new DisplayTopology(adapters, new[] { dispPrimary, dispNonPrimary }, dispPrimary, DesktopBounds.Empty, false, 2);
            var res2Rev = CaptureSourceSelector.SelectSource(top2Rev, criteria);
            res2Rev.IsSuccess.Should().BeTrue();
            res2Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_PRIMARY");
        }

        // Stage 3: Lower AdapterIndex under identical score and non-primary status
        {
            var dispAdapter0 = new DisplayOutputInfo(
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
                DeviceName: @"\\.\DISPLAY_ADAPTER0",
                IsPrimary: false
            );

            var dispAdapter1 = new DisplayOutputInfo(
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
                DeviceName: @"\\.\DISPLAY_ADAPTER1",
                IsPrimary: false
            );

            var top3 = new DisplayTopology(adapters, new[] { dispAdapter1, dispAdapter0 }, null, DesktopBounds.Empty, false, 3);
            var res3 = CaptureSourceSelector.SelectSource(top3, criteria);
            res3.IsSuccess.Should().BeTrue();
            res3.Source!.AdapterIndex.Should().Be(0);
            res3.Source.DeviceName.Should().Be(@"\\.\DISPLAY_ADAPTER0");

            var top3Rev = new DisplayTopology(adapters, new[] { dispAdapter0, dispAdapter1 }, null, DesktopBounds.Empty, false, 3);
            var res3Rev = CaptureSourceSelector.SelectSource(top3Rev, criteria);
            res3Rev.IsSuccess.Should().BeTrue();
            res3Rev.Source!.AdapterIndex.Should().Be(0);
            res3Rev.Source.DeviceName.Should().Be(@"\\.\DISPLAY_ADAPTER0");
        }

        // Stage 4: Lower DisplayIndex under identical score, adapter index, and non-primary status
        {
            var dispIndex0 = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_INDEX0",
                IsPrimary: false
            );

            var dispIndex1 = new DisplayOutputInfo(
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
                DeviceName: @"\\.\DISPLAY_INDEX1",
                IsPrimary: false
            );

            var top4 = new DisplayTopology(adapters, new[] { dispIndex1, dispIndex0 }, null, DesktopBounds.Empty, false, 4);
            var res4 = CaptureSourceSelector.SelectSource(top4, criteria);
            res4.IsSuccess.Should().BeTrue();
            res4.Source!.OutputIndex.Should().Be(0);
            res4.Source.DeviceName.Should().Be(@"\\.\DISPLAY_INDEX0");

            var top4Rev = new DisplayTopology(adapters, new[] { dispIndex0, dispIndex1 }, null, DesktopBounds.Empty, false, 4);
            var res4Rev = CaptureSourceSelector.SelectSource(top4Rev, criteria);
            res4Rev.IsSuccess.Should().BeTrue();
            res4Rev.Source!.OutputIndex.Should().Be(0);
            res4Rev.Source.DeviceName.Should().Be(@"\\.\DISPLAY_INDEX0");
        }

        // Stage 5: Ordinal DeviceName comparison under identical score, adapter, display index, and non-primary status
        {
            var dispAlphaA = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_ALPHA_A",
                IsPrimary: false
            );

            var dispAlphaB = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 60,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_ALPHA_B",
                IsPrimary: false
            );

            var top5 = new DisplayTopology(adapters, new[] { dispAlphaB, dispAlphaA }, null, DesktopBounds.Empty, false, 5);
            var res5 = CaptureSourceSelector.SelectSource(top5, criteria);
            res5.IsSuccess.Should().BeTrue();
            res5.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_ALPHA_A");

            var top5Rev = new DisplayTopology(adapters, new[] { dispAlphaA, dispAlphaB }, null, DesktopBounds.Empty, false, 5);
            var res5Rev = CaptureSourceSelector.SelectSource(top5Rev, criteria);
            res5Rev.IsSuccess.Should().BeTrue();
            res5Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_ALPHA_A");
        }
    }
}
