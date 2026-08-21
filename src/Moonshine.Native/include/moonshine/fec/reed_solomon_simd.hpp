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

constexpr int kMaxDataShards = 64;
constexpr int kMaxParityShards = 32;
constexpr int kMaxTotalShards = 96;

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
     * @brief Encodes parity shards from data shards using the Cauchy systematic generator matrix.
     * @param data_shards Array of pointers to data shards.
     * @param data_shards_count Number of data shards (K <= 64).
     * @param parity_shards Array of pointers to parity shards.
     * @param parity_shards_count Number of parity shards (M <= 32).
     * @param shard_size Size in bytes of each shard.
     * @return 0 on success, non-zero on invalid arguments.
     */
    int Encode(
        const uint8_t* const* data_shards,
        int data_shards_count,
        uint8_t** parity_shards,
        int parity_shards_count,
        int shard_size
    ) noexcept;

    /**
     * @brief Reconstructs lost data and parity shards using genuine GF(2^8) Gauss-Jordan matrix inversion.
     * @param shards Array of pointers to all shards (K data followed by M parity shards).
     * @param data_shards_count Number of data shards (K <= 64).
     * @param parity_shards_count Number of parity shards (M <= 32).
     * @param shard_size Size in bytes of each shard.
     * @param erased_indices Indices of lost shards to reconstruct.
     * @param erased_count Number of lost shards (must be <= M).
     * @return 0 on success, -1 on invalid argument, -2 on unrecoverable/too many erasures, -3 on singular matrix.
     */
    int Reconstruct(
        uint8_t** shards,
        int data_shards_count,
        int parity_shards_count,
        int shard_size,
        const int* erased_indices,
        int erased_count
    ) noexcept;

    /**
     * @brief Backward compatibility overload assuming data_shards = total_shards - erased_count when E <= total_shards/2.
     */
    int Reconstruct(
        uint8_t** shards,
        int total_shards,
        int shard_size,
        const int* erased_indices,
        int erased_count
    ) noexcept;

    /**
     * @brief Builds the (K+M) x K systematic Cauchy generator matrix in GF(2^8).
     * Top K x K is Identity matrix. Bottom M x K is Cauchy matrix: 1 / (p ^ (M + j)).
     */
    static bool BuildGeneratorMatrix(uint8_t* matrix, int k, int m) noexcept;

    /**
     * @brief Inverts a K x K matrix in GF(2^8) using Gauss-Jordan elimination.
     * Returns false if matrix is singular.
     */
    static bool InvertMatrixGf256(const uint8_t* src, uint8_t* dst, int k) noexcept;

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
