#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <span>

namespace moonshine::fec {

/**
 * @brief Ultra-low-latency Galois Field GF(2^8) Reed-Solomon Codec.
 * 
 * Polynomial: x^8 + x^4 + x^3 + x^2 + 1 (0x11D / 0x1D).
 * Implements AVX2 tableless vectorization using 4-bit nibble decomposition
 * and 256-bit SIMD registers.
 */
class ReedSolomonSimd {
public:
    ReedSolomonSimd();
    ~ReedSolomonSimd() = default;

    /**
     * @brief Vectorized XOR of two memory buffers (32-byte chunks with AVX2).
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

private:
    static uint8_t GfMultiplyScalar(uint8_t a, uint8_t b) noexcept;
    static uint8_t GfInverseScalar(uint8_t a) noexcept;

    static bool HasAvx2Support() noexcept;
};

} // namespace moonshine::fec
