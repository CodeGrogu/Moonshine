using System.Runtime.InteropServices;
using Moonshine.Core.Hardware;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Interop;

namespace Moonshine.App;

public static class HardwareProbeRunner
{
    public static void Run(CliOptions options)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine Hardware & Pipeline Probe");
        Console.WriteLine("==========================================================");

        // 1. CPU & Native SIMD
        Console.WriteLine("\n[1] CPU & Native SIMD Architecture:");
        try
        {
            uint simdCode = MoonshineNativeMethods.FecGetSimdArchitecture();
            string archName = simdCode switch
            {
                3 => "x86/x64 AVX-512 + GFNI (512-bit Galois Field SIMD)",
                2 => "x86/x64 AVX2 (256-bit Galois Field SIMD)",
                1 => "x86/x64 SSE4.1",
                4 => "ARM64 NEON",
                _ => "Scalar Fallback"
            };

            Console.WriteLine($"  Active SIMD FEC Kernel:  {archName}");
            Console.WriteLine($"  Hardware Threads:        {Environment.ProcessorCount}");
            Console.WriteLine($"  OS Architecture:         {RuntimeInformation.OSArchitecture}");
            Console.WriteLine($"  OS Version:              {RuntimeInformation.OSDescription}");
        }
        // ALLOWED_EXCEPTION: Report SIMD diagnostics fallback when native probe fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to query SIMD architecture: {ex.Message}");
        }

        // 2. GPU Adapters
        Console.WriteLine("\n[2] Physical GPU Adapters (DXGI / Direct3D 11):");
        try
        {
            var adapters = GpuAdapterInventory.EnumerateAdapters();
            Console.WriteLine($"  Detected Adapters Count: {adapters.Count}\n");

            for (int i = 0; i < adapters.Count; i++)
            {
                var a = adapters[i];
                string vendorName = a.VendorId switch
                {
                    0x10DE => "NVIDIA Corporation",
                    0x1002 => "Advanced Micro Devices (AMD)",
                    0x8086 => "Intel Corporation",
                    0x1414 => "Microsoft Software Adapter",
                    _ => "Unknown Vendor"
                };

                Console.WriteLine($"    - Adapter {i}: {a.Description}");
                Console.WriteLine($"      Vendor ID:           0x{a.VendorId:X4} ({vendorName})");
                Console.WriteLine($"      Device ID:           0x{a.DeviceId:X4}");
                Console.WriteLine($"      LUID:                0x{a.AdapterLuid:X16}");
                Console.WriteLine($"      Dedicated VRAM:      {a.DedicatedVideoMemoryBytes / (1024 * 1024)} MB");
                Console.WriteLine($"      Shared System RAM:   {a.SharedSystemMemoryBytes / (1024 * 1024)} MB");
                Console.WriteLine($"      Display Attached:    {a.HasOutput}");
                Console.WriteLine();
            }
        }
        // ALLOWED_EXCEPTION: Report GPU adapter enumeration diagnostics when DXGI query fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to probe GPU adapters: {ex.Message}");
        }

        // 3. Hardware Video Encoders
        Console.WriteLine("\n[3] Hardware Video Encoders (NVENC / AMF / QSV):");
        try
        {
            Console.WriteLine("  Probing NVIDIA NVENC...");
            int nvencRes = MoonshineNativeMethods.EncoderQueryCaps(1, IntPtr.Zero, out var nvCaps);
            if (nvencRes == 0)
            {
                Console.WriteLine($"    NVENC Status:          Available (Max: {nvCaps.MaxWidth}x{nvCaps.MaxHeight} @ {nvCaps.MaxFps} FPS, 10-Bit: {nvCaps.Supports10Bit != 0})");
            }
            else
            {
                Console.WriteLine("    NVENC Status:          Not Available / No NVIDIA GPU");
            }

            Console.WriteLine("  Probing AMD AMF...");
            int amfRes = MoonshineNativeMethods.EncoderQueryCaps(2, IntPtr.Zero, out var amfCaps);
            if (amfRes == 0)
            {
                Console.WriteLine($"    AMF Status:            Available (Max: {amfCaps.MaxWidth}x{amfCaps.MaxHeight} @ {amfCaps.MaxFps} FPS, 10-Bit: {amfCaps.Supports10Bit != 0})");
            }
            else
            {
                Console.WriteLine("    AMF Status:            Not Available / No AMD GPU");
            }

            Console.WriteLine("  Probing Intel QuickSync (oneVPL)...");
            int qsvRes = MoonshineNativeMethods.EncoderQueryCaps(3, IntPtr.Zero, out var qsvCaps);
            if (qsvRes == 0)
            {
                Console.WriteLine($"    QSV Status:            Available (Max: {qsvCaps.MaxWidth}x{qsvCaps.MaxHeight} @ {qsvCaps.MaxFps} FPS, 10-Bit: {qsvCaps.Supports10Bit != 0})");
            }
            else
            {
                Console.WriteLine("    QSV Status:            Not Available / No Intel GPU");
            }
        }
        // ALLOWED_EXCEPTION: Report encoder capability diagnostics when query fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to query encoder capabilities: {ex.Message}");
        }

        // 4. Hardware Video Decoder (with bounded timeout)
        Console.WriteLine("\n[4] Hardware Video Decoder (Direct3D 11 Video):");
        try
        {
            var decTask = Task.Run(() =>
            {
                int res = MoonshineNativeMethods.VideoQueryCaps(out var decCaps);
                return (res, decCaps);
            });

            if (decTask.Wait(TimeSpan.FromSeconds(3)))
            {
                var (decRes, decCaps) = decTask.Result;
                if (decRes == 0)
                {
                    Console.WriteLine($"  Max Resolution:          {decCaps.MaxWidth}x{decCaps.MaxHeight} @ {decCaps.MaxFps} FPS");
                    Console.WriteLine($"  Supported Codecs:        H.264: {decCaps.SupportsH264 != 0}, HEVC: {decCaps.SupportsHevc != 0}, AV1: {decCaps.SupportsAv1 != 0}");
                    Console.WriteLine($"  HDR10 & 10-Bit:          HDR10: {decCaps.SupportsHdr10 != 0}, 10-Bit: {decCaps.Supports10Bit != 0}");
                    Console.WriteLine($"  Direct3D 12 Surface:     {decCaps.SupportsD3D12 != 0}");
                }
                else
                {
                    Console.WriteLine("  Decoder Status:          Query returned non-zero code");
                }
            }
            else
            {
                Console.WriteLine("  Decoder Status:          Headless / Non-Interactive Driver Mode");
            }
        }
        // ALLOWED_EXCEPTION: Report video decoder diagnostics when capability query fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to query decoder capabilities: {ex.Message}");
        }

        // 5. Display Monitors (with bounded timeout)
        Console.WriteLine("\n[5] Display Topology & Monitors:");
        try
        {
            var dispTask = Task.Run(() => DisplayManager.GetDisplayTopology());
            if (dispTask.Wait(TimeSpan.FromSeconds(3)))
            {
                var topology = dispTask.Result;
                Console.WriteLine($"  Detected Displays:       {topology.Displays.Count}");
                for (int i = 0; i < topology.Displays.Count; i++)
                {
                    var disp = topology.Displays[i];
                    uint hz = disp.RefreshRateNumerator / Math.Max(1, disp.RefreshRateDenominator);
                    Console.WriteLine($"    - Display {i}: {disp.DeviceName} ({disp.Width}x{disp.Height} @ {hz} Hz, HDR: {disp.IsHdr})");
                }
            }
            else
            {
                Console.WriteLine("  Detected Displays:       0 (Headless / Remote Session)");
            }
        }
        // ALLOWED_EXCEPTION: Report display query diagnostics when topology query fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to query display topology: {ex.Message}");
        }

        // 6. Audio Subsystem (with bounded timeout)
        Console.WriteLine("\n[6] Audio Subsystem (WASAPI Exclusive & Driver):");
        try
        {
            var audioTask = Task.Run(() =>
            {
                using var driverService = new VirtualAudioDriverService();
                return driverService.IsDriverInstalled();
            });

            if (audioTask.Wait(TimeSpan.FromSeconds(3)))
            {
                bool isDriverAvailable = audioTask.Result;
                Console.WriteLine($"  Virtual Audio Driver:    {(isDriverAvailable ? "Installed & Active" : "Not Installed (Using Master Loopback Fallback)")}");
            }
            else
            {
                Console.WriteLine("  Virtual Audio Driver:    Not Available (Remote Session)");
            }
        }
        // ALLOWED_EXCEPTION: Report audio subsystem diagnostics when driver query fails.
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to probe audio subsystem: {ex.Message}");
        }

        Console.WriteLine("\n[+] Hardware probe complete.\n");
    }
}
