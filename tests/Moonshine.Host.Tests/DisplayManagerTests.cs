using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class DisplayManagerTests
{
    [Fact]
    public void DisplayManager_GetPhysicalAdapters_ReturnsAdaptersAndValidProperties()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeNull();
        adapters.Should().NotBeEmpty();

        foreach (var adapter in adapters)
        {
            adapter.Description.Should().NotBeNullOrWhiteSpace();
            adapter.DedicatedVideoMemoryBytes.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void DisplayManager_GetDisplays_DiscoversDisplaysWithExtendedInfo()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeEmpty();

        var primaryAdapter = adapters[0];
        var displays = DisplayManager.GetDisplays(primaryAdapter.AdapterIndex);
        displays.Should().NotBeNull();

        foreach (var d in displays)
        {
            d.DeviceName.Should().NotBeNullOrWhiteSpace();
            d.FriendlyName.Should().NotBeNullOrWhiteSpace();
            d.Width.Should().BeGreaterThan(0);
            d.Height.Should().BeGreaterThan(0);
            d.RefreshRateHz.Should().BeGreaterThan(0);
            d.Bounds.Should().NotBeNull();
            d.Bounds.Width.Should().BeGreaterThanOrEqualTo(0);
            d.Bounds.Height.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void DisplayManager_GetSupportedModes_ReturnsModesList()
    {
        var adapters = DisplayManager.GetPhysicalAdapters();
        adapters.Should().NotBeEmpty();

        var primaryAdapter = adapters[0];
        var displays = DisplayManager.GetDisplays(primaryAdapter.AdapterIndex);

        if (displays.Count > 0)
        {
            var modes = DisplayManager.GetSupportedModes(primaryAdapter.AdapterIndex, displays[0].DisplayIndex);
            modes.Should().NotBeNull();

            foreach (var m in modes)
            {
                m.Width.Should().BeGreaterThan(0);
                m.Height.Should().BeGreaterThan(0);
                m.RefreshRateNumerator.Should().BeGreaterThan(0);
                m.RefreshRateDenominator.Should().BeGreaterThan(0);
                m.RefreshRateHz.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void DisplayManager_GetDisplayTopology_ProducesAccurateAtomicSnapshot()
    {
        var topology = DisplayManager.GetDisplayTopology();
        topology.Should().NotBeNull();
        topology.Adapters.Should().NotBeNull();
        topology.Displays.Should().NotBeNull();
        topology.VirtualScreenBounds.Should().NotBeNull();
        topology.TimestampQpc.Should().BeGreaterThan(0);

        if (!topology.IsHeadless)
        {
            topology.Displays.Should().NotBeEmpty();
            topology.PrimaryDisplay.Should().NotBeNull();
            topology.VirtualScreenBounds.Width.Should().BeGreaterThan(0);
            topology.VirtualScreenBounds.Height.Should().BeGreaterThan(0);
        }
        else
        {
            topology.PrimaryDisplay.Should().BeNull();
        }
    }

    [Fact]
    public void DisplayManager_GetPrimaryDisplay_ResolvesPrimaryDisplayCorrectly()
    {
        var primary = DisplayManager.GetPrimaryDisplay();
        var topology = DisplayManager.GetDisplayTopology();

        if (!topology.IsHeadless && topology.PrimaryDisplay != null)
        {
            primary.Should().NotBeNull();
            primary!.IsAttachedToDesktop.Should().BeTrue();
            primary.Width.Should().BeGreaterThan(0);
            primary.Height.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void DisplayManager_GetPreferredHardwareAdapter_ReturnsValidAdapter()
    {
        var preferred = DisplayManager.GetPreferredHardwareAdapter();
        preferred.Should().NotBeNull();
        preferred!.Description.Should().NotBeNullOrWhiteSpace();
    }
}
