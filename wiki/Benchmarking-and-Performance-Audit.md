# Benchmarking and Performance Audit Methodology

## 1. Zero-Allocation Verification

To ensure zero GC allocations in streaming hot paths, Moonshine runs automated BenchmarkDotNet memory diagnoser audits (`[MemoryDiagnoser]`):

```
| Method                      | Mean      | Error     | StdDev    | Gen0   | Allocated |
|---------------------------- |----------:|----------:|----------:|-------:|----------:|
| SimdVectorXor               |  84.22 ns |  0.412 ns |  0.385 ns |      - |       0 B |
| SimdReedSolomonFecRecovery  | 1,120.4 ns|  8.210 ns |  7.680 ns |      - |       0 B |
| RtpHeaderSpanParsing        |  12.18 ns |  0.084 ns |  0.078 ns |      - |       0 B |
| SpscRingBufferPushPop       |   3.12 ns |  0.021 ns |  0.019 ns |      - |       0 B |
```

Key metric: **Allocated column must strictly read 0 B**. Any non-zero allocation triggers a build failure in automated continuous integration.

---

## 2. Micro-Benchmark Execution

To execute micro-benchmarks on your local machine:

```powershell
./scripts/run_benchmarks.ps1
```

To filter for a specific component benchmark:
```powershell
./scripts/run_benchmarks.ps1 -Filter *Fec*
./scripts/run_benchmarks.ps1 -Filter *RingBuffer*
./scripts/run_benchmarks.ps1 -Filter *RtpParsing*
```

---

## 3. End-to-End Latency Profiling

Moonshine instruments four distinct latency intervals:
1. Network Ingestion Time: Duration between UDP socket arrival and SPSC queue insertion ($< 0.15\,\text{ms}$).
2. FEC Recovery and Jitter Time: Duration for parity verification and frame reassembly ($< 0.25\,\text{ms}$).
3. Hardware Decode Time: Duration for Direct3D 11/12 GPU video slice decompression ($< 1.80\,\text{ms}$).
4. Presentation Time: Duration for DXGI swap chain flip to scanout ($< 0.80\,\text{ms}$).

Total Target Frame Latency: **$< 3.0\,\text{ms}$ at 1080p 120 FPS / 4K 60 FPS**.
