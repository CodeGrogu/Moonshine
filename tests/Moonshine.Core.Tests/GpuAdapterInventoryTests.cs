using FluentAssertions;
using Moonshine.Core.Hardware;
using Xunit;

namespace Moonshine.Core.Tests;

public class GpuAdapterInventoryTests
{
    [Fact]
    public void GpuAdapterInventory_EnumeratesPhysicalAdapters_Successfully()
    {
        var adapters = GpuAdapterInventory.EnumerateAdapters();
        adapters.Should().NotBeEmpty();

        bool hasNvidia = adapters.Any(a => a.IsNvidia);
        bool hasIntel = adapters.Any(a => a.IsIntel);

        // Host system has NVIDIA RTX 2060 and Intel Iris Xe
        hasNvidia.Should().BeTrue("NVIDIA GPU must be discovered on host test runner");
        hasIntel.Should().BeTrue("Intel Iris Xe iGPU must be discovered via DXGI enumeration");

        foreach (var adapter in adapters)
        {
            adapter.Description.Should().NotBeNullOrWhiteSpace();
            adapter.VendorId.Should().NotBe(0);
        }
    }
}
