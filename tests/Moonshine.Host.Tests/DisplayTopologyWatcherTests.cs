using FluentAssertions;
using Moonshine.Host.Capture;
using Xunit;

namespace Moonshine.Host.Tests;

public class DisplayTopologyWatcherTests
{
    [Fact]
    public void DisplayTopologyWatcher_InitialSnapshot_ExposesCurrentTopology()
    {
        using var watcher = new DisplayTopologyWatcher();
        var topology = watcher.CurrentTopology;

        topology.Should().NotBeNull();
        topology.Adapters.Should().NotBeNull();
        topology.Displays.Should().NotBeNull();
        topology.VirtualScreenBounds.Should().NotBeNull();
        topology.TimestampQpc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DisplayTopologyWatcher_Refresh_DetectsChangesAndDispatchesEvents()
    {
        var previousTopology = new DisplayTopology(
            Adapters: Array.Empty<DisplayAdapterInfo>(),
            Displays: Array.Empty<DisplayOutputInfo>(),
            PrimaryDisplay: null,
            VirtualScreenBounds: DesktopBounds.Empty,
            IsHeadless: true,
            TimestampQpc: 100
        );

        using var watcher = new DisplayTopologyWatcher(previousTopology);

        DisplayTopologyChangedEventArgs? receivedArgs = null;
        watcher.TopologyChanged += (sender, args) =>
        {
            receivedArgs = args;
        };

        watcher.Refresh();

        var current = watcher.CurrentTopology;
        current.Should().NotBeNull();

        if (current.IsHeadless != previousTopology.IsHeadless || current.Displays.Count != previousTopology.Displays.Count)
        {
            receivedArgs.Should().NotBeNull();
            receivedArgs!.OldTopology.Should().Be(previousTopology);
            receivedArgs.NewTopology.Should().Be(current);
            receivedArgs.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void DisplayTopologyWatcher_Dispose_UnhooksCleanly()
    {
        var watcher = new DisplayTopologyWatcher();
        watcher.Dispose();
        watcher.Dispose(); // Multi-dispose safety check
    }
}
