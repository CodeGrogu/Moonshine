using System.Runtime.InteropServices;
using System.Text;
using Moonshine.Interop;

namespace Moonshine.Core.Hardware;

/// <summary>
/// Managed representation of a physical GPU adapter in the Windows 11 host system.
/// </summary>
public sealed record GpuAdapterInfo(
    uint Index,
    uint VendorId,
    uint DeviceId,
    uint SubsystemId,
    uint Revision,
    bool IsSoftware,
    bool HasOutput,
    ulong AdapterLuid,
    ulong DedicatedVideoMemoryBytes,
    ulong SharedSystemMemoryBytes,
    string Description
)
{
    public bool IsNvidia => VendorId == 0x10DE;
    public bool IsIntel => VendorId == 0x8086;
    public bool IsAmd => VendorId == 0x1002;
}

/// <summary>
/// Hardware inventory engine that enumerates all physical DXGI adapters across the host machine.
/// Decouples system-wide GPU inventory from device-specific encoder suitability.
/// <para>
/// Architectural Selection Chain Invariant:
/// <code>
/// GpuAdapter (Inventory)
///     │
///     ├── Vendor ID (e.g. 0x10DE NVIDIA, 0x8086 Intel, 0x1002 AMD)
///     ├── Adapter LUID / DXGI Index
///     ├── Video Memory &amp; Capabilities
///     │
///     ▼
/// Direct3D 11 Device (Instantiated ON That Specific Adapter via moonshine_d3d11_create_device_on_adapter)
///     │
///     ▼
/// Encoder Backend (NVENC / QSV / AMF initialised WITH That Specific Direct3D 11 Device)
/// </code>
/// Invariant: The encoder backend must always be initialised on the Direct3D 11 device created on its matching
/// vendor adapter, completely decoupled from which adapter owns the desktop display output.
/// </para>
/// </summary>
public static class GpuAdapterInventory
{
    /// <summary>
    /// Enumerates all DXGI adapters present on the Windows 11 system.
    /// </summary>
    public static unsafe IReadOnlyList<GpuAdapterInfo> EnumerateAdapters()
    {
        uint totalCount = 0;
        int queryRes = MoonshineNativeMethods.GpuEnumerateAdapters(null, 0, out totalCount);
        if (queryRes != 0 || totalCount == 0)
        {
            return Array.Empty<GpuAdapterInfo>();
        }

        Span<MoonshineGpuAdapter> rawAdapters = stackalloc MoonshineGpuAdapter[(int)totalCount];
        fixed (MoonshineGpuAdapter* ptr = rawAdapters)
        {
            int fillRes = MoonshineNativeMethods.GpuEnumerateAdapters(ptr, totalCount, out totalCount);
            if (fillRes != 0)
            {
                return Array.Empty<GpuAdapterInfo>();
            }
        }

        var results = new List<GpuAdapterInfo>((int)totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            ref readonly var raw = ref rawAdapters[i];
            string description;
            fixed (byte* descPtr = raw.Description)
            {
                int len = 0;
                while (len < 128 && descPtr[len] != 0) len++;
                description = Encoding.UTF8.GetString(descPtr, len);
            }

            results.Add(new GpuAdapterInfo(
                Index: raw.Index,
                VendorId: raw.VendorId,
                DeviceId: raw.DeviceId,
                SubsystemId: raw.SubsystemId,
                Revision: raw.Revision,
                IsSoftware: raw.IsSoftware != 0,
                HasOutput: raw.HasOutput != 0,
                AdapterLuid: raw.AdapterLuid,
                DedicatedVideoMemoryBytes: raw.DedicatedVideoMemory,
                SharedSystemMemoryBytes: raw.SharedSystemMemory,
                Description: description
            ));
        }

        return results;
    }

    /// <summary>
    /// Gets all adapters matching a specific PCI Vendor ID (e.g. 0x8086 for Intel).
    /// </summary>
    public static IReadOnlyList<GpuAdapterInfo> GetAdaptersByVendor(uint vendorId)
    {
        var all = EnumerateAdapters();
        var matches = new List<GpuAdapterInfo>();
        foreach (var adapter in all)
        {
            if (adapter.VendorId == vendorId)
            {
                matches.Add(adapter);
            }
        }
        return matches;
    }

    /// <summary>
    /// Gets all Intel GPU adapters (e.g. Iris Xe, Arc) in the system.
    /// </summary>
    public static IReadOnlyList<GpuAdapterInfo> GetIntelAdapters() => GetAdaptersByVendor(0x8086);
}
