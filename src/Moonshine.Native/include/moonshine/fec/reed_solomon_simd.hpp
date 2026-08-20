#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <span>

namespace moonshine::fec {

enum class SimdArchitecture : uint32_t {
    Scalar = 0,
    Avx2 = 1,
    Avx512 = 2,
    GfniAvx512 = 3
};

/**
 * @brief Ultra-low-latency Galois Field GF(2^8) Reed-Solomon Codec.
 * 
 * Polynomial: x^8 + x^4 + x^3 + x^2 + 1 (0x11D / 0x1D).
 * Features multi-tiered SIMD acceleration:
 * 1. Intel GFNI + AVX-512 (64 bytes/cycle tableless single-cycle multiplication)
 * 2. AVX-512BW (64 bytes/cycle 4-bit nibble decomposition)
 * 3. AVX2 (32 bytes/cycle 4-bit nibble decomposition)
 * 4. 64-bit word / scalar fallback
 */
class ReedSolomonSimd {
public:
    ReedSolomonSimd();
    ~ReedSolomonSimd() = default;

    /**
     * @brief Vectorized XOR of two memory buffers (up to 64-byte chunks with AVX-512).
     */
    static void VectorXor(uint8_t* dest, const uint8_t* src, size_t length) noexcept;

    /**
     * @brief Vectorized Galois Field GF(2^8) multiplication by constant coefficient.
     * Computes: dest[i] ^= src[i] * coeff in GF(2^8).
     */
    static void VectorGfMulAdd(uint8_t* dest, const uint8_t* src, uint8_t coeff, size_t length) noexcept;

    /**
     * @brief Reconstructs lost data shards using parity shards.
     */
    int Reconstruct(
        uint8_t** shards,
        int total_shards,
        int shard_size,
        const int* erased_indices,
        int erased_count
    ) noexcept;

    /**
     * @brief Returns the active SIMD architecture detected on the current CPU.
     */
    static SimdArchitecture GetDetectedArchitecture() noexcept;

    static bool HasAvx2Support() noexcept;
    static bool HasAvx512Support() noexcept;
    static bool HasGfniSupport() noexcept;

    static uint8_t GfMultiplyScalar(uint8_t a, uint8_t b) noexcept;
    static uint8_t GfInverseScalar(uint8_t a) noexcept;
};

} // namespace moonshine::fec
