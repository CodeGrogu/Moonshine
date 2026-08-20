---
name: moonshine-fec-simd-optimizer
description: >-
  Expert guide and runbook for tuning, testing, and verifying the SIMD Galois Field GF(2^8)
  Reed-Solomon Forward Error Correction (FEC) engine in Moonshine.
  Use when modifying FEC algorithms, adding AVX-512/GFNI or NEON kernels, or analyzing packet recovery.
---

# Moonshine FEC SIMD Optimizer Skill

## Reed-Solomon Galois Field Arithmetic
Moonshine uses the irreducible polynomial $p(x) = x^8 + x^4 + x^3 + x^2 + 1$ (0x11D / 0x1D).

### Vectorization Approach
1. **AVX2 256-Bit Implementation**:
   - Decomposes each byte into high and low 4-bit nibbles.
   - Computes tableless multiplication in parallel for 32 bytes using `_mm256_shuffle_epi8` with pre-computed 16-byte multiplication LUTs broadcast across registers.
   - Adds product into destination with `_mm256_xor_si256`.
2. **AVX-512 / GFNI Implementation**:
   - Single-instruction affine transformation `_mm512_gf2p8affine_epi64_epi8` processing 64 bytes simultaneously.

### Verification Routine
1. Verify single parity shard XOR recovery with `test_fec_simd.exe`.
2. Verify multi-erasure matrix recovery with BenchmarkDotNet suite.
