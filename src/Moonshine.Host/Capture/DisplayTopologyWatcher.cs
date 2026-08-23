using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Moonshine.Host.Capture;

public enum DisplayTopologyChangeType
{
    InitialSnapshot = 0,
    DisplayConnected = 1,
    DisplayDisconnected = 2,
    DisplayModeChanged = 3,
    PrimaryDisplayChanged = 4,
    HeadlessStateChanged = 5,
    GenericChange = 6
}

public sealed class DisplayTopologyChangedEventArgs : EventArgs
{
    public DisplayTopology OldTopology { get; }
    public DisplayTopology NewTopology { get; }
    public DisplayTopologyChangeType ChangeType { get; }
    public string Description { get; }

    public DisplayTopologyChangedEventArgs(
        DisplayTopology oldTopology,
        DisplayTopology newTopology,
        DisplayTopologyChangeType changeType,
        string description)
    {
        OldTopology = oldTopology;
        NewTopology = newTopology;
        ChangeType = changeType;
        Description = description;
    }
}

/// <summary>
/// Universal interface for monitoring physical display topology and hot-plug notifications.
/// </summary>
public interface IDisplayTopologyWatcher : IDisposable
{
    DisplayTopology CurrentTopology { get; }
    event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;
    void Refresh();
}

/// <summary>
/// Real-time Windows physical display hot-plug and resolution mode change watcher.
/// Dispatches asynchronous topology change notifications without adding overhead to the frame acquisition hot path.
/// </summary>
public sealed class DisplayTopologyWatcher : IDisplayTopologyWatcher
{
    private DisplayTopology _currentTopology;
    private bool _systemEventsHooked;
    private bool _disposed;
    private readonly Lock _lock = new();

    public event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;

    public DisplayTopology CurrentTopology => Volatile.Read(ref _currentTopology);

    public DisplayTopologyWatcher(DisplayTopology? initialTopology = null)
    {
        _currentTopology = initialTopology ?? DisplayManager.GetDisplayTopology();
        HookSystemEvents();
    }

    private void HookSystemEvents()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _systemEventsHooked = true;
        }
        // ALLOWED_EXCEPTION: SystemEvents may be unavailable in non-interactive Windows service contexts.
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or ExternalException)
        {
            _systemEventsHooked = false;
        }
    }

    private void UnhookSystemEvents()
    {
        if (_systemEventsHooked)
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            // ALLOWED_EXCEPTION: Defensive cleanup on process teardown.
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
            }
            _systemEventsHooked = false;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    /// <summary>
    /// Evaluates the current Windows display topology, detects transitions, and dispatches change events.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            if (_disposed) return;

            var oldTopology = _currentTopology;
            var newTopology = DisplayManager.GetDisplayTopology();

            var changeType = ClassifyChange(oldTopology, newTopology, out string description);
            Volatile.Write(ref _currentTopology, newTopology);

            if (changeType != null)
            {
                try
                {
                    TopologyChanged?.Invoke(this, new DisplayTopologyChangedEventArgs(
                        oldTopology: oldTopology,
                        newTopology: newTopology,
                        changeType: changeType.Value,
                        description: description
                    ));
                }
                // ALLOWED_EXCEPTION: Event subscriber exceptions must not fault the topology watcher or crash notification loop.
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                }
            }
        }
    }

    private static DisplayTopologyChangeType? ClassifyChange(
        DisplayTopology oldTop,
        DisplayTopology newTop,
        out string description)
    {
        if (oldTop.IsHeadless != newTop.IsHeadless)
        {
            description = newTop.IsHeadless
                ? "All physical displays disconnected (transitioned to headless state)."
                : "Physical display connected (restored from headless state).";
            return DisplayTopologyChangeType.HeadlessStateChanged;
        }

        if (newTop.Displays.Count > oldTop.Displays.Count)
        {
            description = $"Display monitor connected (count increased from {oldTop.Displays.Count} to {newTop.Displays.Count}).";
            return DisplayTopologyChangeType.DisplayConnected;
        }

        if (newTop.Displays.Count < oldTop.Displays.Count)
        {
            description = $"Display monitor disconnected (count decreased from {oldTop.Displays.Count} to {newTop.Displays.Count}).";
            return DisplayTopologyChangeType.DisplayDisconnected;
        }

        if (oldTop.PrimaryDisplay?.DeviceName != newTop.PrimaryDisplay?.DeviceName)
        {
            description = $"Primary display changed from {oldTop.PrimaryDisplay?.DeviceName ?? "none"} to {newTop.PrimaryDisplay?.DeviceName ?? "none"}.";
            return DisplayTopologyChangeType.PrimaryDisplayChanged;
        }

        for (int i = 0; i < Math.Min(oldTop.Displays.Count, newTop.Displays.Count); i++)
        {
            var o = oldTop.Displays[i];
            var n = newTop.Displays[i];

            if (o.Width != n.Width || o.Height != n.Height || Math.Abs(o.RefreshRateHz - n.RefreshRateHz) > 0.01 || o.IsHdr != n.IsHdr)
            {
                description = $"Display '{n.DeviceName}' mode changed: {o.Width}x{o.Height}@{o.RefreshRateHz:F1}Hz (HDR: {o.IsHdr}) -> {n.Width}x{n.Height}@{n.RefreshRateHz:F1}Hz (HDR: {n.IsHdr}).";
                return DisplayTopologyChangeType.DisplayModeChanged;
            }
        }

        description = "Display topology layout updated.";
        return DisplayTopologyChangeType.GenericChange;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            UnhookSystemEvents();
        }
    }
}
