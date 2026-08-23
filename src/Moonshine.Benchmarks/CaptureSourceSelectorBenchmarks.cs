using BenchmarkDotNet.Attributes;
using Moonshine.Host.Capture;

namespace Moonshine.Benchmarks;

[InProcess]
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

    [GlobalSetup]
    public void Setup()
    {
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
        _fallbackCriteria = new CaptureSourceSelectionCriteria(CaptureSelectionPolicy.SpecificDisplayIndex, PreferredAdapterIndex: 0, PreferredDisplayIndex: 99, FallbackPolicy: CaptureSourceFallbackPolicy.FallbackToPrimary);
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
    public CaptureSourceSelectionResult SelectSource_FallbackPolicy()
    {
        return CaptureSourceSelector.SelectSource(_topology, _fallbackCriteria);
    }
}
