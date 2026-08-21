# Native C++ Standards (Moonshine.Native)

All C++23 native development must adhere to the engineering standards in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

These rules apply to all native C++ code in `src/Moonshine.Native`.

## 1. Modern C++ Standard
- Code is compiled under **C++23** (`/std:c++latest` / `-std=c++23`).
- Use standard concepts, `<span>`, `<ranges>`, `<atomic>`, and `<memory>`.
- Use RAII for all native OS handles (D3D11 devices, WASAPI clients, thread handles).

## 2. Low-Latency Concurrency
- Strict lock-free discipline for real-time streaming paths.
- Use `std::atomic` with explicit memory order parameters:
  - `std::memory_order_relaxed` for local/non-synchronized state.
  - `std::memory_order_acquire` when reading published data.
  - `std::memory_order_release` when publishing newly written data.
- Avoid `std::mutex`, `std::recursive_mutex`, `std::condition_variable` in frame paths.

## 3. Hardware Video & SIMD Best Practices
- Direct3D 11/12 decode surfaces must be presented via DXGI Flip Model with `DXGI_SWAP_EFFECT_FLIP_DISCARD`.
- SIMD Galois Field routines must use 256-bit AVX2 registers (`__m256i`) or 512-bit AVX-512 registers (`__m512i`).
- Compiler warnings are treated as errors (`/WX` / `-Werror`).
