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
}
