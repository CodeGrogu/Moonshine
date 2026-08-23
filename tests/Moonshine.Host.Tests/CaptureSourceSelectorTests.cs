using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class CaptureSourceSelectorTests
{
    private static DisplayTopology CreateMockTopology(bool isHeadless = false)
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true),
            new(1, 0x2000, "Intel UHD Graphics 770", 1_000_000_000, true)
        };

        if (isHeadless)
        {
            return new DisplayTopology(
                Adapters: adapters.AsReadOnly(),
                Displays: Array.Empty<DisplayOutputInfo>(),
                PrimaryDisplay: null,
                VirtualScreenBounds: DesktopBounds.Empty,
                IsHeadless: true,
                TimestampQpc: 123456789
            );
        }

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
            DeviceName: "\\\\.\\DISPLAY1",
            FriendlyName: "OLED Gaming Monitor",
            MonitorHandle: (IntPtr)0x1001,
            DesktopBounds: new DesktopBounds(0, 0, 3840, 2160),
            DpiScale: 150,
            IsPrimary: true,
            SupportedModes: null
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
            DeviceName: "\\\\.\\DISPLAY2",
            FriendlyName: "Secondary Monitor",
            MonitorHandle: (IntPtr)0x1002,
            DesktopBounds: new DesktopBounds(3840, 0, 5760, 1080),
            DpiScale: 100,
            IsPrimary: false,
            SupportedModes: null
        );

        return new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d0, d1 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 5760, 2160),
            IsHeadless: false,
            TimestampQpc: 123456789
        );
    }

    [Fact]
    public void SelectSource_WithNullTopology_ThrowsArgumentNullException()
    {
        var action = () => CaptureSourceSelector.SelectSource(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectSource_WhenTopologyIsHeadless_ReturnsHeadlessResultWithoutFakeDisplays()
    {
        var topology = CreateMockTopology(isHeadless: true);
        var result = CaptureSourceSelector.SelectSource(topology);

        result.IsSuccess.Should().BeFalse();
        result.IsHeadless.Should().BeTrue();
        result.Source.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectSource_WithPrimaryDisplayPolicy_SelectsPrimaryDisplay()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.PrimaryDisplay);

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(0);
        result.Source.Width.Should().Be(3840);
        result.Source.Height.Should().Be(2160);
        result.Source.IsHdr.Should().BeTrue();
        result.Source.IsPrimary.Should().BeTrue();
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY1");
    }

    [Fact]
    public void SelectSource_WithSpecificDisplayIndex_SelectsMatchingOutput()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 0,
            PreferredDisplayIndex: 1
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(1);
        result.Source.Width.Should().Be(1920);
        result.Source.Height.Should().Be(1080);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY2");
    }

    [Fact]
    public void SelectSource_WithSpecificMonitorHandle_SelectsMatchingOutput()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificMonitorHandle,
            PreferredMonitorHandle: (IntPtr)0x1002
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(1);
        result.Source.MonitorHandle.Should().Be((IntPtr)0x1002);
    }

    [Fact]
    public void SelectSource_WithSpecificDeviceName_SelectsMatchingOutput()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDeviceName,
            PreferredDeviceName: "\\\\.\\DISPLAY2"
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.DeviceName.Should().Be("\\\\.\\DISPLAY2");
    }

    [Fact]
    public void SelectSource_WithMatchResolution_SelectsClosestMatch()
    {
        var topology = CreateMockTopology();
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
        result.Source!.Width.Should().Be(1920);
        result.Source.Height.Should().Be(1080);
        result.Source.OutputIndex.Should().Be(1);
    }

    [Fact]
    public void SelectSource_WhenNotFoundAndFallbackPolicyIsFallbackToPrimary_ReturnsPrimaryWithFallbackFlag()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 0,
            PreferredDisplayIndex: 99,
            FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(0);
        result.Source.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void SelectSource_WhenNotFoundAndFallbackPolicyIsFailClosed_ReturnsFailure()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 0,
            PreferredDisplayIndex: 99,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeFalse();
        result.Source.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectSource_WithMatchResolution_RequireHdr_StrictlySkipsNonHdrDisplays()
    {
        var topology = CreateMockTopology();
        // Target is 1080p, which matches d1 (SDR, 1080p), but RequireHdr = true
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            RequireHdr: true,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        // Since only d0 is HDR (4K 120Hz), d0 must be selected even though d1 is 1080p
        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.IsHdr.Should().BeTrue();
        result.Source.OutputIndex.Should().Be(0);
    }

    [Fact]
    public void SelectSource_WithMatchResolution_RequireHdr_WhenNoHdrDisplaysExist_FailsClosed()
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "GPU", 8_000_000_000, true)
        };

        var d0 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false, // SDR
            BitsPerColor: 8,
            DeviceName: "\\\\.\\DISPLAY1",
            FriendlyName: "SDR Monitor",
            IsPrimary: true
        );

        var topology = new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d0 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 123456789
        );

        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            RequireHdr: true,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeFalse();
        result.Source.Should().BeNull();
    }

    [Fact]
    public void SelectSource_WithMatchResolution_DeterministicTieBreaking_EnforcesDocumentedOrder()
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "GPU 0", 8_000_000_000, true),
            new(1, 0x2000, "GPU 1", 8_000_000_000, true)
        };

        // Two displays with identical resolution and refresh rate, but d0 is on GPU 0 and d1 is on GPU 1
        var d0 = new DisplayOutputInfo(
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
            DeviceName: "\\\\.\\DISPLAY1",
            IsPrimary: false
        );

        var d1 = new DisplayOutputInfo(
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
            DeviceName: "\\\\.\\DISPLAY2",
            IsPrimary: false
        );

        // Put d1 first in array to test that ordering is NOT array-order dependent
        var topology = new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d1, d0 },
            PrimaryDisplay: null,
            VirtualScreenBounds: new DesktopBounds(0, 0, 3840, 1080),
            IsHeadless: false,
            TimestampQpc: 123456789
        );

        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        // Lower adapter index (AdapterIndex: 0) wins tie-break over AdapterIndex: 1
        result.Source!.AdapterIndex.Should().Be(0);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY1");
    }

    [Fact]
    public void SelectSource_ZeroGCAllocations_SteadyStateHotPath()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.PrimaryDisplay);

        // Warmup JIT
        for (int i = 0; i < 50; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        (bytesAfter - bytesBefore).Should().Be(0);
    }

    private static DisplayTopology CreateComplexMockTopology()
    {
        var complexAdapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true),
            new(1, 0x2000, "Intel UHD Graphics 770", 1_000_000_000, true),
            new(2, 0x3000, "DisplayLink USB Device", 0, false)
        };

        var cd0 = new DisplayOutputInfo(
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
            DeviceName: "\\\\.\\DISPLAY1",
            FriendlyName: "RTX 4090 Primary OLED",
            MonitorHandle: (IntPtr)0x2001,
            DesktopBounds: new DesktopBounds(0, 0, 3840, 2160),
            DpiScale: 150,
            IsPrimary: true
        );

        var cd1 = new DisplayOutputInfo(
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
            DeviceName: "\\\\.\\DISPLAY2",
            FriendlyName: "RTX 4090 Left Monitor",
            MonitorHandle: (IntPtr)0x2002,
            DesktopBounds: new DesktopBounds(-1920, 0, 0, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        var cd2 = new DisplayOutputInfo(
            DisplayIndex: 2,
            AdapterIndex: 0,
            Width: 1080,
            Height: 1920,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 1,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: "\\\\.\\DISPLAY3",
            FriendlyName: "RTX 4090 Portrait Monitor",
            MonitorHandle: (IntPtr)0x2003,
            DesktopBounds: new DesktopBounds(3840, 0, 4920, 1920),
            DpiScale: 100,
            IsPrimary: false
        );

        var cd3 = new DisplayOutputInfo(
            DisplayIndex: 3,
            AdapterIndex: 0,
            Width: 3840,
            Height: 2160,
            RefreshRateNumerator: 120,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: false,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: "\\\\.\\DISPLAY4",
            FriendlyName: "RTX 4090 Detached VR HMD",
            MonitorHandle: (IntPtr)0x2004,
            DesktopBounds: DesktopBounds.Empty,
            DpiScale: 100,
            IsPrimary: false
        );

        var cd4 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 1,
            Width: 3840,
            Height: 2160,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: "\\\\.\\DISPLAY5",
            FriendlyName: "Intel UHD 770 Overhead 4K",
            MonitorHandle: (IntPtr)0x2005,
            DesktopBounds: new DesktopBounds(0, -2160, 3840, 0),
            DpiScale: 150,
            IsPrimary: false
        );

        var cd5 = new DisplayOutputInfo(
            DisplayIndex: 1,
            AdapterIndex: 1,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 2,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: "\\\\.\\DISPLAY6",
            FriendlyName: "Intel UHD 770 Inverted Prompter",
            MonitorHandle: (IntPtr)0x2006,
            DesktopBounds: new DesktopBounds(3840, 1920, 5760, 3000),
            DpiScale: 100,
            IsPrimary: false
        );

        var cd6 = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 2,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: "\\\\.\\DISPLAY7",
            FriendlyName: "DisplayLink USB Dock Monitor",
            MonitorHandle: (IntPtr)0x2007,
            DesktopBounds: new DesktopBounds(4920, 0, 6840, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        var cd7 = new DisplayOutputInfo(
            DisplayIndex: 1,
            AdapterIndex: 2,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: false,
            IsHdr: false,
            BitsPerColor: 8,
            DeviceName: "\\\\.\\DISPLAY8",
            FriendlyName: "DisplayLink USB Aux (Detached)",
            MonitorHandle: (IntPtr)0x2008,
            DesktopBounds: DesktopBounds.Empty,
            DpiScale: 100,
            IsPrimary: false
        );

        return new DisplayTopology(
            Adapters: complexAdapters.AsReadOnly(),
            Displays: new[] { cd0, cd1, cd2, cd3, cd4, cd5, cd6, cd7 },
            PrimaryDisplay: cd0,
            VirtualScreenBounds: new DesktopBounds(-1920, -2160, 6840, 3000),
            IsHeadless: false,
            TimestampQpc: 123456789,
            Generation: 2
        );
    }

    [Fact]
    public void SelectSource_ComplexTopology_PrimaryDisplayPolicy_SelectsPrimaryOled()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.PrimaryDisplay);

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.AdapterIndex.Should().Be(0);
        result.Source.OutputIndex.Should().Be(0);
        result.Source.IsPrimary.Should().BeTrue();
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY1");
    }

    [Fact]
    public void SelectSource_ComplexTopology_SpecificIndexPolicy_SelectsIntelOutput()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 1,
            PreferredDisplayIndex: 0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.AdapterIndex.Should().Be(1);
        result.Source.OutputIndex.Should().Be(0);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY5");
    }

    [Fact]
    public void SelectSource_ComplexTopology_MatchResolution_DiscreteGpu_Selects4K120HzDisplay()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 3840,
            TargetHeight: 2160,
            TargetFps: 120.0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.AdapterIndex.Should().Be(0);
        result.Source.OutputIndex.Should().Be(0);
        result.Source.Width.Should().Be(3840);
        result.Source.Height.Should().Be(2160);
        result.Source.RefreshRateHz.Should().Be(120.0);
    }

    [Fact]
    public void SelectSource_ComplexTopology_MatchResolution_StrictHdr_SelectsHdrDisplay()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 3840,
            TargetHeight: 2160,
            TargetFps: 60.0,
            RequireHdr: true
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.IsHdr.Should().BeTrue();
        // TargetFps 60.0 matches cd4 (Overhead 4K at 60Hz HDR) with exact score
        result.Source.AdapterIndex.Should().Be(1);
        result.Source.OutputIndex.Should().Be(0);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY5");
    }

    [Fact]
    public void SelectSource_ComplexTopology_MatchResolution_RotatedPortrait_SelectsPortraitDisplay()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1080,
            TargetHeight: 1920,
            TargetFps: 60.0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.Width.Should().Be(1080);
        result.Source.Height.Should().Be(1920);
        result.Source.OutputIndex.Should().Be(2);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY3");
    }

    [Fact]
    public void SelectSource_ComplexTopology_MatchResolution_NegativeBounds_SelectsNegativeCoordinatesDisplay()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        // cd1 is at [-1920, 0] on Adapter 0, Display 1
        result.Source!.AdapterIndex.Should().Be(0);
        result.Source.OutputIndex.Should().Be(1);
        result.Source.DesktopBounds.Left.Should().Be(-1920);
        result.Source.DesktopBounds.Top.Should().Be(0);
    }

    [Fact]
    public void SelectSource_ComplexTopology_FallbackPolicy_ReturnsPrimaryDisplayWithFallbackFlag()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.SpecificDisplayIndex,
            PreferredAdapterIndex: 2,
            PreferredDisplayIndex: 99,
            FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.IsPrimary.Should().BeTrue();
        result.Source.OutputIndex.Should().Be(0);
        result.Source.AdapterIndex.Should().Be(0);
    }

    [Fact]
    public void SelectSource_ComplexTopology_ZeroGCAllocations_SteadyStateHotPath()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.MatchResolution,
            TargetWidth: 1080,
            TargetHeight: 1920,
            TargetFps: 60.0
        );

        for (int i = 0; i < 50; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10000; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        (bytesAfter - bytesBefore).Should().Be(0);
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_SelectsExactMatchingDisplay()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.Width.Should().Be(1920);
        result.Source.Height.Should().Be(1080);
        result.Source.OutputIndex.Should().Be(1);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY2");
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_WhenNoExactMatch_WithFallbackToPrimary_ReturnsFallback()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 2560,
            TargetHeight: 1440,
            FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(0);
        result.Source.IsPrimary.Should().BeTrue();
        result.Source.Width.Should().Be(3840);
        result.Source.Height.Should().Be(2160);
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_WhenNoExactMatch_WithFailClosed_ReturnsFailure()
    {
        var topology = CreateMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 2560,
            TargetHeight: 1440,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeFalse();
        result.Source.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_RequireHdr_SkipsNonHdrExactMatches()
    {
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "GPU", 8_000_000_000, true)
        };

        var d0Sdr = new DisplayOutputInfo(
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
            DeviceName: "\\\\.\\DISPLAY1",
            FriendlyName: "SDR 1080p",
            IsPrimary: true
        );

        var d1Hdr = new DisplayOutputInfo(
            DisplayIndex: 1,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 60,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10,
            DeviceName: "\\\\.\\DISPLAY2",
            FriendlyName: "HDR 1080p",
            IsPrimary: false
        );

        var topology = new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d0Sdr, d1Hdr },
            PrimaryDisplay: d0Sdr,
            VirtualScreenBounds: new DesktopBounds(0, 0, 3840, 1080),
            IsHeadless: false,
            TimestampQpc: 123456789
        );

        // Require exact 1080p and HDR
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            RequireHdr: true,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(1);
        result.Source.IsHdr.Should().BeTrue();
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY2");
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_RequireHdr_WhenNoHdrExactMatchExists_FailsClosed()
    {
        var topology = CreateMockTopology();
        // Mock topology has d0 (4K HDR) and d1 (1080p SDR). Target is 1080p exact + RequireHdr.
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            RequireHdr: true,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, criteria);

        result.IsSuccess.Should().BeFalse();
        result.Source.Should().BeNull();
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_5StageDeterministicTieBreaker_EnforcesAllStages()
    {
        var adapters = new[]
        {
            new DisplayAdapterInfo(0, 0x1000, "Adapter 0", 8_000_000_000, true),
            new DisplayAdapterInfo(1, 0x2000, "Adapter 1", 8_000_000_000, true)
        };

        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        // Stage 1: FPS proximity
        {
            var d60Hz = new DisplayOutputInfo(
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
                DeviceName: @"\\.\DISPLAY_60HZ",
                IsPrimary: false
            );

            var d144Hz = new DisplayOutputInfo(
                DisplayIndex: 0,
                AdapterIndex: 0,
                Width: 1920,
                Height: 1080,
                RefreshRateNumerator: 144,
                RefreshRateDenominator: 1,
                Rotation: 0,
                IsAttachedToDesktop: true,
                IsHdr: false,
                BitsPerColor: 8,
                DeviceName: @"\\.\DISPLAY_144HZ",
                IsPrimary: false
            );

            var top1 = new DisplayTopology(adapters, new[] { d144Hz, d60Hz }, null, DesktopBounds.Empty, false, 1);
            var res1 = CaptureSourceSelector.SelectSource(top1, criteria);
            res1.IsSuccess.Should().BeTrue();
            res1.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_60HZ");

            var top1Rev = new DisplayTopology(adapters, new[] { d60Hz, d144Hz }, null, DesktopBounds.Empty, false, 1);
            var res1Rev = CaptureSourceSelector.SelectSource(top1Rev, criteria);
            res1Rev.IsSuccess.Should().BeTrue();
            res1Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_60HZ");
        }

        // Stage 2: Primary display preferred
        {
            var dPrimary = new DisplayOutputInfo(
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

            var dNonPrimary = new DisplayOutputInfo(
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

            var top2 = new DisplayTopology(adapters, new[] { dNonPrimary, dPrimary }, dPrimary, DesktopBounds.Empty, false, 2);
            var res2 = CaptureSourceSelector.SelectSource(top2, criteria);
            res2.IsSuccess.Should().BeTrue();
            res2.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_PRIMARY");
            res2.Source.IsPrimary.Should().BeTrue();

            var top2Rev = new DisplayTopology(adapters, new[] { dPrimary, dNonPrimary }, dPrimary, DesktopBounds.Empty, false, 2);
            var res2Rev = CaptureSourceSelector.SelectSource(top2Rev, criteria);
            res2Rev.IsSuccess.Should().BeTrue();
            res2Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_PRIMARY");
        }

        // Stage 3: Lower AdapterIndex
        {
            var dAdapter0 = new DisplayOutputInfo(
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

            var dAdapter1 = new DisplayOutputInfo(
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

            var top3 = new DisplayTopology(adapters, new[] { dAdapter1, dAdapter0 }, null, DesktopBounds.Empty, false, 3);
            var res3 = CaptureSourceSelector.SelectSource(top3, criteria);
            res3.IsSuccess.Should().BeTrue();
            res3.Source!.AdapterIndex.Should().Be(0);
            res3.Source.DeviceName.Should().Be(@"\\.\DISPLAY_ADAPTER0");

            var top3Rev = new DisplayTopology(adapters, new[] { dAdapter0, dAdapter1 }, null, DesktopBounds.Empty, false, 3);
            var res3Rev = CaptureSourceSelector.SelectSource(top3Rev, criteria);
            res3Rev.IsSuccess.Should().BeTrue();
            res3Rev.Source!.AdapterIndex.Should().Be(0);
            res3Rev.Source.DeviceName.Should().Be(@"\\.\DISPLAY_ADAPTER0");
        }

        // Stage 4: Lower DisplayIndex
        {
            var dIndex0 = new DisplayOutputInfo(
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

            var dIndex1 = new DisplayOutputInfo(
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

            var top4 = new DisplayTopology(adapters, new[] { dIndex1, dIndex0 }, null, DesktopBounds.Empty, false, 4);
            var res4 = CaptureSourceSelector.SelectSource(top4, criteria);
            res4.IsSuccess.Should().BeTrue();
            res4.Source!.OutputIndex.Should().Be(0);
            res4.Source.DeviceName.Should().Be(@"\\.\DISPLAY_INDEX0");

            var top4Rev = new DisplayTopology(adapters, new[] { dIndex0, dIndex1 }, null, DesktopBounds.Empty, false, 4);
            var res4Rev = CaptureSourceSelector.SelectSource(top4Rev, criteria);
            res4Rev.IsSuccess.Should().BeTrue();
            res4Rev.Source!.OutputIndex.Should().Be(0);
            res4Rev.Source.DeviceName.Should().Be(@"\\.\DISPLAY_INDEX0");
        }

        // Stage 5: Ordinal DeviceName comparison
        {
            var dAlphaA = new DisplayOutputInfo(
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

            var dAlphaB = new DisplayOutputInfo(
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

            var top5 = new DisplayTopology(adapters, new[] { dAlphaB, dAlphaA }, null, DesktopBounds.Empty, false, 5);
            var res5 = CaptureSourceSelector.SelectSource(top5, criteria);
            res5.IsSuccess.Should().BeTrue();
            res5.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_ALPHA_A");

            var top5Rev = new DisplayTopology(adapters, new[] { dAlphaA, dAlphaB }, null, DesktopBounds.Empty, false, 5);
            var res5Rev = CaptureSourceSelector.SelectSource(top5Rev, criteria);
            res5Rev.IsSuccess.Should().BeTrue();
            res5Rev.Source!.DeviceName.Should().Be(@"\\.\DISPLAY_ALPHA_A");
        }
    }

    [Fact]
    public void SelectSource_WithRequireExactResolution_ZeroGCAllocations_SteadyStateHotPath()
    {
        var topology = CreateComplexMockTopology();
        var criteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0
        );

        for (int i = 0; i < 50; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10000; i++)
        {
            _ = CaptureSourceSelector.SelectSource(topology, criteria);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        (bytesAfter - bytesBefore).Should().Be(0);
    }

    [Fact]
    public void SelectSource_RequireExactResolution_MatchesExactCandidateOnly()
    {
        var topology = CreateMockTopology();

        var exactCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 1920,
            TargetHeight: 1080,
            TargetFps: 60.0,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var result = CaptureSourceSelector.SelectSource(topology, exactCriteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.Source.Should().NotBeNull();
        result.Source!.Width.Should().Be(1920);
        result.Source.Height.Should().Be(1080);
        result.Source.OutputIndex.Should().Be(1);
        result.Source.DeviceName.Should().Be("\\\\.\\DISPLAY2");

        var nonMatchingCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 2560,
            TargetHeight: 1440,
            FallbackPolicy: CaptureSourceFallbackPolicy.FailClosed
        );

        var failResult = CaptureSourceSelector.SelectSource(topology, nonMatchingCriteria);

        failResult.IsSuccess.Should().BeFalse();
        failResult.Source.Should().BeNull();
        failResult.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectSource_RequireExactResolution_NoMatch_FallsBackToPrimaryWhenConfigured()
    {
        var topology = CreateMockTopology();

        var fallbackCriteria = new CaptureSourceSelectionCriteria(
            Policy: CaptureSelectionPolicy.RequireExactResolution,
            TargetWidth: 2560,
            TargetHeight: 1440,
            FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary
        );

        var result = CaptureSourceSelector.SelectSource(topology, fallbackCriteria);

        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeTrue();
        result.Source.Should().NotBeNull();
        result.Source!.OutputIndex.Should().Be(0);
        result.Source.IsPrimary.Should().BeTrue();
        result.Source.Width.Should().Be(3840);
        result.Source.Height.Should().Be(2160);
    }
}
