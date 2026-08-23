using FluentAssertions;
using Moonshine.Host.Capture;
using Moonshine.Host.Session;
using Xunit;

namespace Moonshine.Host.Tests;

public class CaptureSourceSessionHandoverTests
{
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

        // When not running/streaming, it should safely ignore without exceptions
        var action = () => session.HandleDisplayTopologyChanged(changeArgs);
        action.Should().NotThrow();
    }
}
