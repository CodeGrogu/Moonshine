using BenchmarkDotNet.Attributes;
using Moonshine.Host.Capture;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class CaptureSourceSelectorBenchmarks
{
    private DisplayTopology _topology = null!;
    private CaptureSourceSelectionCriteria _primaryCriteria = null!;
    private CaptureSourceSelectionCriteria _indexCriteria = null!;
    private CaptureSourceSelectionCriteria _handleCriteria = null!;
    private CaptureSourceSelectionCriteria _deviceCriteria = null!;
    private CaptureSourceSelectionCriteria _matchResolutionCriteria = null!;
    private CaptureSourceSelectionCriteria _fallbackCriteria = null!;
    private CaptureSourceSelectionCriteria _exactResolutionCriteria = null!;

    private DisplayTopology _complexTopology = null!;
    private CaptureSourceSelectionCriteria _complexPrimaryCriteria = null!;
    private CaptureSourceSelectionCriteria _complexIndexCriteria = null!;
    private CaptureSourceSelectionCriteria _complexMatchDiscreteGpuCriteria = null!;
    private CaptureSourceSelectionCriteria _complexMatchStrictHdrCriteria = null!;
    private CaptureSourceSelectionCriteria _complexMatchRotatedPortraitCriteria = null!;
    private CaptureSourceSelectionCriteria _complexMatchNegativeBoundsCriteria = null!;
    private CaptureSourceSelectionCriteria _complexExactResolutionCriteria = null!;
    private CaptureSourceSelectionCriteria _complexFallbackCriteria = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 1. Baseline dual-display topology
        var adapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true),
            new(1, 0x2000, "Intel UHD Graphics 770", 1_000_000_000, true)
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
            DeviceName: "\\\\.\\DISPLAY1",
            FriendlyName: "OLED Gaming Monitor",
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
            DeviceName: "\\\\.\\DISPLAY2",
            FriendlyName: "Secondary Monitor",
            MonitorHandle: (IntPtr)0x1002,
            DesktopBounds: new DesktopBounds(3840, 0, 5760, 1080),
            DpiScale: 100,
            IsPrimary: false
        );

        _topology = new DisplayTopology(
            Adapters: adapters.AsReadOnly(),
            Displays: new[] { d0, d1 },
            PrimaryDisplay: d0,
            VirtualScreenBounds: new DesktopBounds(0, 0, 5760, 2160),
            IsHeadless: false,
            TimestampQpc: 123456789,
            Generation: 1
        );

        _primaryCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.PrimaryDisplay);
        _indexCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDisplayIndex, PreferredAdapterIndex: 0, PreferredDisplayIndex: 1);
        _handleCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificMonitorHandle, PreferredMonitorHandle: (IntPtr)0x1002);
        _deviceCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDeviceName, PreferredDeviceName: "\\\\.\\DISPLAY2");
        _matchResolutionCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.MatchResolution, TargetWidth: 1920, TargetHeight: 1080, TargetFps: 60.0);
        _exactResolutionCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.RequireExactResolution, TargetWidth: 1920, TargetHeight: 1080, TargetFps: 60.0);
        _fallbackCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDisplayIndex, PreferredAdapterIndex: 0, PreferredDisplayIndex: 99, FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary);

        // 2. Heterogeneous 8-display, 3-adapter stress topology
        var complexAdapters = new List<DisplayAdapterInfo>
        {
            new(0, 0x1000, "NVIDIA GeForce RTX 4090", 24_000_000_000, true),
            new(1, 0x2000, "Intel UHD Graphics 770", 1_000_000_000, true),
            new(2, 0x3000, "DisplayLink USB Device", 0, false)
        };

        // Adapter 0: Discrete RTX 4090
        // cd0: Primary 4K 120Hz HDR display at [0, 0]
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

        // cd1: Left 1080p 60Hz SDR display at negative coordinates [-1920, 0]
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

        // cd2: Portrait 90 deg rotated 1080x1920 60Hz SDR display at [3840, 0]
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

        // cd3: Detached VR HMD output (4K 120Hz HDR duplicate)
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

        // Adapter 1: Integrated Intel UHD 770
        // cd4: Overhead 4K 60Hz HDR display at negative coordinates [0, -2160]
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

        // cd5: Inverted 180 deg rotated 1080p 60Hz SDR display at [3840, 1920]
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

        // Adapter 2: DisplayLink USB Adapter
        // cd6: Attached 1080p 60Hz SDR duplicate display at [4920, 0]
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

        // cd7: Detached auxiliary 1080p 60Hz SDR display
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

        _complexTopology = new DisplayTopology(
            Adapters: complexAdapters.AsReadOnly(),
            Displays: new[] { cd0, cd1, cd2, cd3, cd4, cd5, cd6, cd7 },
            PrimaryDisplay: cd0,
            VirtualScreenBounds: new DesktopBounds(-1920, -2160, 6840, 3000),
            IsHeadless: false,
            TimestampQpc: 123456789,
            Generation: 2
        );

        _complexPrimaryCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.PrimaryDisplay);
        _complexIndexCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDisplayIndex, PreferredAdapterIndex: 1, PreferredDisplayIndex: 0);
        _complexMatchDiscreteGpuCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.MatchResolution, TargetWidth: 3840, TargetHeight: 2160, TargetFps: 120.0);
        _complexMatchStrictHdrCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.MatchResolution, TargetWidth: 3840, TargetHeight: 2160, TargetFps: 60.0, RequireHdr: true);
        _complexMatchRotatedPortraitCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.MatchResolution, TargetWidth: 1080, TargetHeight: 1920, TargetFps: 60.0);
        _complexMatchNegativeBoundsCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.MatchResolution, TargetWidth: 1920, TargetHeight: 1080, TargetFps: 60.0);
        _complexExactResolutionCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.RequireExactResolution, TargetWidth: 1920, TargetHeight: 1080, TargetFps: 60.0);
        _complexFallbackCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDisplayIndex, PreferredAdapterIndex: 2, PreferredDisplayIndex: 99, FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary);
    }

    [Benchmark(Baseline = true)]
    public CaptureSourceSelectionResult SelectSource_PrimaryDisplayPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _primaryCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_SpecificIndexPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _indexCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_SpecificHandlePolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _handleCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_SpecificDevicePolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _deviceCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_MatchResolutionPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _matchResolutionCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_RequireExactResolutionPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _exactResolutionCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_FallbackPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _fallbackCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_PrimaryDisplayPolicy()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexPrimaryCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_SpecificIndexPolicy()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexIndexCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_MatchResolution_DiscreteGpu()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexMatchDiscreteGpuCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_MatchResolution_StrictHdr()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexMatchStrictHdrCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_MatchResolution_RotatedPortrait()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexMatchRotatedPortraitCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_MatchResolution_NegativeBounds()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexMatchNegativeBoundsCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_RequireExactResolutionPolicy()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexExactResolutionCriteria);
    }

    [Benchmark]
    public CaptureSourceSelectionResult SelectSource_ComplexTopology_FallbackPolicy()
    {
        return CaptureSourceSelector.SelectSource(_complexTopology, _complexFallbackCriteria);
    }
}
