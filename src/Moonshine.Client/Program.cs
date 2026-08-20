using System;
using Moonshine.Client;
using Moonshine.Interop;

Console.WriteLine("=================================================================");
Console.WriteLine("⚡ Moonshine - Ultra-Low-Latency GameStream Client Engine");
Console.WriteLine("Engineered in C# 13 (.NET Native AOT) & C++23 AVX2/AVX-512 SIMD");
Console.WriteLine("=================================================================\n");

var engine = new MoonshineClientEngine();

Console.WriteLine("[Hardware] Querying Native Video Decoder Capabilities...");
try
{
    var caps = engine.QueryHardwareCaps();
    Console.WriteLine($"  - Max Resolution: {caps.MaxWidth}x{caps.MaxHeight} @ {caps.MaxFps} FPS");
    Console.WriteLine($"  - Hardware AV1 Support:  {(caps.SupportsAv1 != 0 ? "YES" : "NO")}");
    Console.WriteLine($"  - Hardware HEVC Support: {(caps.SupportsHevc != 0 ? "YES" : "NO")}");
    Console.WriteLine($"  - Hardware H.264 Support:{(caps.SupportsH264 != 0 ? "YES" : "NO")}");
    Console.WriteLine($"  - HDR10 & 10-Bit Color:  {(caps.SupportsHdr10 != 0 ? "YES" : "NO")}");
    Console.WriteLine($"  - Direct3D 12 Video:     {(caps.SupportsD3D12 != 0 ? "YES" : "NO")}");
    Console.WriteLine($"  - Vulkan Video Pipeline: {(caps.SupportsVulkan != 0 ? "YES" : "NO")}");
}
catch (Exception ex)
{
    Console.WriteLine($"  [!] Note: Native DLL not yet loaded in path ({ex.Message})");
}

Console.WriteLine("\n[Core] Moonshine Client ready for low-latency streaming sessions.");
