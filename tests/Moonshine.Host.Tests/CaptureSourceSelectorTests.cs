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
}
