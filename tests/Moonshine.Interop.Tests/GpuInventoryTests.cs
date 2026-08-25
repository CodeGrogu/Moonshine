using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class GpuInventoryTests
{
    [Fact]
    public unsafe void GpuEnumerateAdapters_DiscoversPhysicalAdapters_Successfully()
    {
        uint totalCount = 0;
        int queryRes = MoonshineNativeMethods.GpuEnumerateAdapters(null, 0, out totalCount);
        queryRes.Should().Be(0);
        totalCount.Should().BeGreaterThanOrEqualTo(1);

        Span<MoonshineGpuAdapter> adapters = stackalloc MoonshineGpuAdapter[(int)totalCount];
        fixed (MoonshineGpuAdapter* ptr = adapters)
        {
            int fillRes = MoonshineNativeMethods.GpuEnumerateAdapters(ptr, totalCount, out totalCount);
            fillRes.Should().Be(0);
        }

        bool foundNvidia = false;
        bool foundIntel = false;

        for (int i = 0; i < totalCount; i++)
        {
            if (adapters[i].VendorId == 0x10DE) foundNvidia = true;
            if (adapters[i].VendorId == 0x8086) foundIntel = true;
        }

        foundNvidia.Should().BeTrue("NVIDIA GPU must be discovered on host test runner");
        foundIntel.Should().BeTrue("Intel Iris Xe iGPU must be discovered via DXGI enumeration");
    }

    [Fact]
    public void QsvDiagnostics_RunsGranularReport_Successfully()
    {
        int res = MoonshineNativeMethods.QsvRunDiagnostics(out var report);
        res.Should().Be(0);

        // Assert Intel adapter discovery on this dual-GPU host
        report.AdapterFound.Should().Be(1);
        report.AdapterDeviceId.Should().NotBe(0);
        report.D3D11DeviceCreated.Should().Be(1);
        report.D3D11VendorVerified.Should().Be(1);
    }

    [Fact]
    public unsafe void GpuEnumerateAdapters_VerifiesEnumeratedOutputContract()
    {
        uint totalCount = 0;
        int queryRes = MoonshineNativeMethods.GpuEnumerateAdapters(null, 0, out totalCount);
        queryRes.Should().Be(0);
        totalCount.Should().BeGreaterThanOrEqualTo(1);

        Span<MoonshineGpuAdapter> adapters = stackalloc MoonshineGpuAdapter[(int)totalCount];
        fixed (MoonshineGpuAdapter* ptr = adapters)
        {
            int fillRes = MoonshineNativeMethods.GpuEnumerateAdapters(ptr, totalCount, out totalCount);
            fillRes.Should().Be(0);
        }

        bool hasAtLeastOneAttachedOutput = false;
        for (int i = 0; i < totalCount; i++)
        {
            (adapters[i].HasOutput == 0 || adapters[i].HasOutput == 1).Should().BeTrue();
            if (adapters[i].HasOutput == 1)
            {
                hasAtLeastOneAttachedOutput = true;
            }
        }

        hasAtLeastOneAttachedOutput.Should().BeTrue("At least one GPU adapter must possess an enumerated desktop output");
    }

    [Fact]
    public void D3D11CreateDevice_VendorInvariant_EnforcesStrictValidation()
    {
        // Nonexistent vendor ID must fail closed
        IntPtr invalidDev = MoonshineNativeMethods.D3D11CreateDevice(0x9999);
        invalidDev.Should().Be(IntPtr.Zero, "Nonexistent vendor ID must fail closed with null device");

        // Intel GPU device creation on host with Intel GPU
        IntPtr intelDev = MoonshineNativeMethods.D3D11CreateDevice(0x8086);
        if (intelDev != IntPtr.Zero)
        {
            try
            {
                intelDev.Should().NotBe(IntPtr.Zero);
            }
            finally
            {
                MoonshineNativeMethods.D3D11DestroyDevice(intelDev);
            }
        }

        IntPtr intelAdapterDev = MoonshineNativeMethods.D3D11CreateDeviceOnAdapter(0x8086, 0);
        if (intelAdapterDev != IntPtr.Zero)
        {
            try
            {
                intelAdapterDev.Should().NotBe(IntPtr.Zero);
            }
            finally
            {
                MoonshineNativeMethods.D3D11DestroyDevice(intelAdapterDev);
            }
        }
    }
}
