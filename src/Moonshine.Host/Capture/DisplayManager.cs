using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public sealed record DisplayAdapterInfo(
    uint AdapterIndex,
    long AdapterLuid,
    string Description,
    ulong DedicatedVideoMemoryBytes,
    bool IsHardware
);

public sealed record DisplayModeInfo(
    uint Width,
    uint Height,
    uint RefreshRateNumerator,
    uint RefreshRateDenominator,
    uint Format,
    uint Scaling,
    uint ScanlineOrdering,
    bool IsHdr
)
{
    public double RefreshRateHz => RefreshRateDenominator > 0
        ? (double)RefreshRateNumerator / RefreshRateDenominator
        : 60.0;
}

public sealed record DesktopBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public static DesktopBounds Empty => new(0, 0, 0, 0);
}

public sealed record DisplayOutputInfo(
    uint DisplayIndex,
    uint AdapterIndex,
    uint Width,
    uint Height,
    uint RefreshRateNumerator,
    uint RefreshRateDenominator,
    uint Rotation,
    bool IsAttachedToDesktop,
    bool IsHdr,
    byte BitsPerColor,
    string DeviceName = "\\\\.\\DISPLAY1",
    string FriendlyName = "Generic PnP Monitor",
    IntPtr MonitorHandle = default,
    DesktopBounds? DesktopBounds = null,
    uint DpiScale = 100,
    bool IsPrimary = false,
    IReadOnlyList<DisplayModeInfo>? SupportedModes = null
)
{
    public DesktopBounds Bounds { get; } = DesktopBounds ?? new DesktopBounds(0, 0, (int)Width, (int)Height);
    public IReadOnlyList<DisplayModeInfo> Modes { get; } = SupportedModes ?? Array.Empty<DisplayModeInfo>();

    public double RefreshRateHz => RefreshRateDenominator > 0
        ? (double)RefreshRateNumerator / RefreshRateDenominator
        : 60.0;
}

public sealed record DisplayTopology(
    IReadOnlyList<DisplayAdapterInfo> Adapters,
    IReadOnlyList<DisplayOutputInfo> Displays,
    DisplayOutputInfo? PrimaryDisplay,
    DesktopBounds VirtualScreenBounds,
    bool IsHeadless,
    ulong TimestampQpc
);

/// <summary>
/// Physical GPU adapter and display output discovery service for Windows hosts.
/// </summary>
public static class DisplayManager
{
    /// <summary>
    /// Enumerates all physical GPU display adapters discovered on the host system.
    /// </summary>
    public static unsafe IReadOnlyList<DisplayAdapterInfo> GetPhysicalAdapters()
    {
        uint adapterCount = MoonshineNativeMethods.CaptureGetAdapterCount();
        var adapters = new List<DisplayAdapterInfo>((int)adapterCount);

        for (uint a = 0; a < adapterCount; a++)
        {
            if (MoonshineNativeMethods.CaptureGetAdapterInfo(a, out var rawInfo) == 0)
            {
                string description = global::System.Text.Encoding.UTF8.GetString(rawInfo.Description, 128).TrimEnd('\0');
                adapters.Add(new DisplayAdapterInfo(
                    rawInfo.AdapterIndex,
                    rawInfo.AdapterLuid,
                    description,
                    rawInfo.DedicatedVideoMemory,
                    rawInfo.IsHardware != 0
                ));
            }
        }

        return adapters;
    }

    /// <summary>
    /// Enumerates all display modes supported by the specified display output.
    /// </summary>
    public static unsafe IReadOnlyList<DisplayModeInfo> GetSupportedModes(uint adapterIndex, uint displayIndex)
    {
        uint count = MoonshineNativeMethods.CaptureGetDisplayModeCount(adapterIndex, displayIndex);
        if (count == 0)
        {
            return Array.Empty<DisplayModeInfo>();
        }

        uint maxModes = Math.Min(count, 512u);
        var buffer = new MoonshineDisplayModeDesc[maxModes];
        fixed (MoonshineDisplayModeDesc* ptr = buffer)
        {
            if (MoonshineNativeMethods.CaptureGetDisplayModes(adapterIndex, displayIndex, ptr, maxModes, out uint actualCount) == 0 && actualCount > 0)
            {
                var modes = new List<DisplayModeInfo>((int)actualCount);
                for (int i = 0; i < actualCount; i++)
                {
                    modes.Add(new DisplayModeInfo(
                        buffer[i].Width,
                        buffer[i].Height,
                        buffer[i].RefreshRateNumerator,
                        buffer[i].RefreshRateDenominator,
                        buffer[i].Format,
                        buffer[i].Scaling,
                        buffer[i].ScanlineOrdering,
                        buffer[i].IsHdr != 0
                    ));
                }
                return modes;
            }
        }

        return Array.Empty<DisplayModeInfo>();
    }

    /// <summary>
    /// Enumerates all physical display outputs attached to the specified GPU adapter.
    /// </summary>
    public static unsafe IReadOnlyList<DisplayOutputInfo> GetDisplays(uint adapterIndex = 0)
    {
        uint displayCount = MoonshineNativeMethods.CaptureGetDisplayCount(adapterIndex);
        var displays = new List<DisplayOutputInfo>((int)displayCount);

        for (uint d = 0; d < displayCount; d++)
        {
            if (MoonshineNativeMethods.CaptureGetDisplayInfo(adapterIndex, d, out var rawInfo) == 0)
            {
                string deviceName = "\\\\.\\DISPLAY1";
                string friendlyName = "Generic PnP Monitor";
                IntPtr monitorHandle = IntPtr.Zero;
                DesktopBounds bounds = new(0, 0, (int)rawInfo.Width, (int)rawInfo.Height);
                uint dpiScale = 100;
                bool isPrimary = (d == 0);

                if (MoonshineNativeMethods.CaptureGetDisplayExtendedInfo(adapterIndex, d, out var extInfo) == 0)
                {
                    deviceName = global::System.Text.Encoding.UTF8.GetString(extInfo.DeviceName, 32).TrimEnd('\0');
                    friendlyName = global::System.Text.Encoding.UTF8.GetString(extInfo.FriendlyName, 64).TrimEnd('\0');
                    monitorHandle = checked((IntPtr)extInfo.MonitorHandle);
                    bounds = new DesktopBounds(extInfo.DesktopLeft, extInfo.DesktopTop, extInfo.DesktopRight, extInfo.DesktopBottom);
                    dpiScale = extInfo.DpiScale;
                    isPrimary = extInfo.IsPrimary != 0;
                }

                displays.Add(new DisplayOutputInfo(
                    DisplayIndex: rawInfo.DisplayIndex,
                    AdapterIndex: rawInfo.AdapterIndex,
                    Width: rawInfo.Width,
                    Height: rawInfo.Height,
                    RefreshRateNumerator: rawInfo.RefreshRateNumerator,
                    RefreshRateDenominator: rawInfo.RefreshRateDenominator,
                    Rotation: rawInfo.Rotation,
                    IsAttachedToDesktop: rawInfo.IsAttachedToDesktop != 0,
                    IsHdr: rawInfo.IsHdr != 0,
                    BitsPerColor: rawInfo.BitsPerColor,
                    DeviceName: deviceName,
                    FriendlyName: friendlyName,
                    MonitorHandle: monitorHandle,
                    DesktopBounds: bounds,
                    DpiScale: dpiScale,
                    IsPrimary: isPrimary,
                    SupportedModes: null
                ));
            }
        }

        return displays;
    }

    /// <summary>
    /// Resolves an atomic snapshot of the complete Windows display topology across all GPU adapters.
    /// </summary>
    public static DisplayTopology GetDisplayTopology()
    {
        var adapters = GetPhysicalAdapters();
        var allDisplays = new List<DisplayOutputInfo>();

        for (int i = 0; i < adapters.Count; i++)
        {
            var displays = GetDisplays(adapters[i].AdapterIndex);
            allDisplays.AddRange(displays);
        }

        DisplayOutputInfo? primary = null;
        for (int i = 0; i < allDisplays.Count; i++)
        {
            if (allDisplays[i].IsAttachedToDesktop && (allDisplays[i].IsPrimary || primary == null))
            {
                primary = allDisplays[i];
                if (allDisplays[i].IsPrimary) break;
            }
        }

        int minLeft = 0, minTop = 0, maxRight = 0, maxBottom = 0;
        bool hasAttached = false;

        for (int i = 0; i < allDisplays.Count; i++)
        {
            var disp = allDisplays[i];
            if (disp.IsAttachedToDesktop)
            {
                if (!hasAttached)
                {
                    minLeft = disp.Bounds.Left;
                    minTop = disp.Bounds.Top;
                    maxRight = disp.Bounds.Right;
                    maxBottom = disp.Bounds.Bottom;
                    hasAttached = true;
                }
                else
                {
                    minLeft = Math.Min(minLeft, disp.Bounds.Left);
                    minTop = Math.Min(minTop, disp.Bounds.Top);
                    maxRight = Math.Max(maxRight, disp.Bounds.Right);
                    maxBottom = Math.Max(maxBottom, disp.Bounds.Bottom);
                }
            }
        }

        DesktopBounds virtualBounds = hasAttached
            ? new DesktopBounds(minLeft, minTop, maxRight, maxBottom)
            : DesktopBounds.Empty;

        bool isHeadless = !hasAttached;
        ulong timestampQpc = (ulong)Stopwatch.GetTimestamp();

        return new DisplayTopology(
            Adapters: adapters,
            Displays: allDisplays.AsReadOnly(),
            PrimaryDisplay: primary,
            VirtualScreenBounds: virtualBounds,
            IsHeadless: isHeadless,
            TimestampQpc: timestampQpc
        );
    }

    /// <summary>
    /// Resolves the primary active display output for desktop capture.
    /// </summary>
    public static DisplayOutputInfo? GetPrimaryDisplay(uint adapterIndex = 0)
    {
        var displays = GetDisplays(adapterIndex);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsAttachedToDesktop && displays[i].IsPrimary)
            {
                return displays[i];
            }
        }

        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsAttachedToDesktop)
            {
                return displays[i];
            }
        }

        return displays.Count > 0 ? displays[0] : null;
    }

    /// <summary>
    /// Resolves the preferred hardware GPU adapter (prioritising dedicated hardware GPUs over software/WARP).
    /// </summary>
    public static DisplayAdapterInfo? GetPreferredHardwareAdapter()
    {
        var adapters = GetPhysicalAdapters();
        for (int i = 0; i < adapters.Count; i++)
        {
            if (adapters[i].IsHardware && adapters[i].DedicatedVideoMemoryBytes > 0)
            {
                return adapters[i];
            }
        }

        for (int i = 0; i < adapters.Count; i++)
        {
            if (adapters[i].IsHardware)
            {
                return adapters[i];
            }
        }

        return adapters.Count > 0 ? adapters[0] : null;
    }
}
