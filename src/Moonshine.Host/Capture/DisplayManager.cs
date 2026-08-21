using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Capture;

public sealed record DisplayAdapterInfo(
    uint AdapterIndex,
    long AdapterLuid,
    string Description,
    ulong DedicatedVideoMemoryBytes,
    bool IsHardware
);

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
    byte BitsPerColor
)
{
    public double RefreshRateHz => RefreshRateDenominator > 0
        ? (double)RefreshRateNumerator / RefreshRateDenominator
        : 60.0;
}

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
                string description = System.Text.Encoding.UTF8.GetString(rawInfo.Description, 128).TrimEnd('\0');
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
    /// Enumerates all physical display outputs attached to the specified GPU adapter.
    /// </summary>
    public static IReadOnlyList<DisplayOutputInfo> GetDisplays(uint adapterIndex = 0)
    {
        uint displayCount = MoonshineNativeMethods.CaptureGetDisplayCount(adapterIndex);
        var displays = new List<DisplayOutputInfo>((int)displayCount);

        for (uint d = 0; d < displayCount; d++)
        {
            if (MoonshineNativeMethods.CaptureGetDisplayInfo(adapterIndex, d, out var rawInfo) == 0)
            {
                displays.Add(new DisplayOutputInfo(
                    rawInfo.DisplayIndex,
                    rawInfo.AdapterIndex,
                    rawInfo.Width,
                    rawInfo.Height,
                    rawInfo.RefreshRateNumerator,
                    rawInfo.RefreshRateDenominator,
                    rawInfo.Rotation,
                    rawInfo.IsAttachedToDesktop != 0,
                    rawInfo.IsHdr != 0,
                    rawInfo.BitsPerColor
                ));
            }
        }

        return displays;
    }

    /// <summary>
    /// Resolves the primary active display output for desktop capture.
    /// </summary>
    public static DisplayOutputInfo? GetPrimaryDisplay(uint adapterIndex = 0)
    {
        var displays = GetDisplays(adapterIndex);
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
