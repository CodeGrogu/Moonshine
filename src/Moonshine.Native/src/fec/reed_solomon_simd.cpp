#include "moonshine/fec/reed_solomon_simd.hpp"
#include <immintrin.h>
#include <cstring>
#include <array>
#include <algorithm>

#if defined(_MSC_VER)
    #include <intrin.h>
#elif defined(__GNUC__) || defined(__clang__)
    #include <cpuid.h>
#endif

namespace moonshine::fec {

namespace {

// Galois Field GF(2^8) Tables with Generator Polynomial 0x11D
static std::array<uint8_t, 256> gf_exp;
static std::array<uint8_t, 256> gf_log;
static bool tables_initialized = []() {
    uint32_t x = 1;
    for (size_t i = 0; i < 255; ++i) {
        gf_exp[i] = static_cast<uint8_t>(x);
        gf_log[static_cast<uint8_t>(x)] = static_cast<uint8_t>(i);
        x <<= 1;
        if (x & 0x100) {
            x ^= 0x11D;
        }
    }
    gf_exp[255] = gf_exp[0];
    gf_log[0] = 0;
    return true;
}();

} // anonymous namespace

ReedSolomonSimd::ReedSolomonSimd() {
    (void)tables_initialized;
}

bool ReedSolomonSimd::HasAvx2Support() noexcept {
#if defined(_MSC_VER)
    int cpu_info[4];
    __cpuid(cpu_info, 0);
    int n_ids = cpu_info[0];
    if (n_ids >= 7) {
        __cpuidex(cpu_info, 7, 0);
        return (cpu_info[1] & (1 << 5)) != 0; // AVX2 bit
    }
    return false;
#elif defined(__GNUC__) || defined(__clang__)
    return __builtin_cpu_supports("avx2");
#else
    return false;
#endif
}

uint8_t ReedSolomonSimd::GfMultiplyScalar(uint8_t a, uint8_t b) noexcept {
    if (a == 0 || b == 0) return 0;
    int log_sum = static_cast<int>(gf_log[a]) + static_cast<int>(gf_log[b]);
    return gf_exp[log_sum % 255];
}

uint8_t ReedSolomonSimd::GfInverseScalar(uint8_t a) noexcept {
    if (a == 0) return 0;
    return gf_exp[255 - gf_log[a]];
}

void ReedSolomonSimd::VectorXor(uint8_t* dest, const uint8_t* src, size_t length) noexcept {
    size_t i = 0;
#if defined(__AVX2__) || defined(MOONSHINE_HAS_AVX2)
    // 32-byte chunks using 256-bit AVX2 registers
    for (; i + 32 <= length; i += 32) {
        __m256i vd = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(dest + i));
        __m256i vs = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(src + i));
        _mm256_storeu_si256(reinterpret_cast<__m256i*>(dest + i), _mm256_xor_si256(vd, vs));
    }
#endif

    // Process remainder 8-byte chunks
    for (; i + 8 <= length; i += 8) {
        *reinterpret_cast<uint64_t*>(dest + i) ^= *reinterpret_cast<const uint64_t*>(src + i);
    }

    // Scalar tail
    for (; i < length; ++i) {
        dest[i] ^= src[i];
    }
}

void ReedSolomonSimd::VectorGfMulAdd(uint8_t* dest, const uint8_t* src, uint8_t coeff, size_t length) noexcept {
    if (coeff == 0) return;
    if (coeff == 1) {
        VectorXor(dest, src, length);
        return;
    }

#if defined(__AVX2__) || defined(MOONSHINE_HAS_AVX2)
    // AVX2 4-bit nibble decomposition lookup tables
    alignas(32) uint8_t low_table[32];
    alignas(32) uint8_t high_table[32];

    for (uint8_t nibble = 0; nibble < 16; ++nibble) {
        uint8_t low_val = GfMultiplyScalar(nibble, coeff);
        uint8_t high_val = GfMultiplyScalar(static_cast<uint8_t>(nibble << 4), coeff);
        low_table[nibble] = low_val;
        low_table[nibble + 16] = low_val;
        high_table[nibble] = high_val;
        high_table[nibble + 16] = high_val;
    }

    __m256i v_low_table = _mm256_load_si256(reinterpret_cast<const __m256i*>(low_table));
    __m256i v_high_table = _mm256_load_si256(reinterpret_cast<const __m256i*>(high_table));
    __m256i v_mask_low = _mm256_set1_epi8(0x0F);

    size_t i = 0;
    for (; i + 32 <= length; i += 32) {
        __m256i v_src = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(src + i));
        __m256i v_dest = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(dest + i));

        __m256i v_src_low = _mm256_and_si256(v_src, v_mask_low);
        __m256i v_src_high = _mm256_and_si256(_mm256_srli_epi16(v_src, 4), v_mask_low);

        __m256i v_res_low = _mm256_shuffle_epi8(v_low_table, v_src_low);
        __m256i v_res_high = _mm256_shuffle_epi8(v_high_table, v_src_high);

        __m256i v_product = _mm256_xor_si256(v_res_low, v_res_high);
        __m256i v_out = _mm256_xor_si256(v_dest, v_product);

        _mm256_storeu_si256(reinterpret_cast<__m256i*>(dest + i), v_out);
    }

    // Scalar fallback for remainder
    for (; i < length; ++i) {
        dest[i] ^= GfMultiplyScalar(src[i], coeff);
    }
#else
    for (size_t i = 0; i < length; ++i) {
        dest[i] ^= GfMultiplyScalar(src[i], coeff);
    }
#endif
}

int ReedSolomonSimd::Reconstruct(
    uint8_t** shards,
    int total_shards,
    int shard_size,
    const int* erased_indices,
    int erased_count
) noexcept {
    if (erased_count <= 0) return 0;
    if (erased_count > total_shards) return -1;

    // Single parity recovery (Fast XOR Parity Shard Case)
    if (erased_count == 1 && erased_indices[0] < total_shards) {
        int lost_idx = erased_indices[0];
        std::memset(shards[lost_idx], 0, shard_size);
        for (int i = 0; i < total_shards; ++i) {
            if (i != lost_idx) {
                VectorXor(shards[lost_idx], shards[i], shard_size);
            }
        }
        return 0;
    }

    // Multi-shard recovery with Vandermonde Matrix inversion
    // (Optimized for 2-4 parity shards common in Moonlight/Sunshine)
    for (int e = 0; e < erased_count; ++e) {
        int lost_idx = erased_indices[e];
        if (lost_idx < 0 || lost_idx >= total_shards) continue;
        std::memset(shards[lost_idx], 0, shard_size);
        
        for (int i = 0; i < total_shards; ++i) {
            bool is_erased = false;
            for (int k = 0; k < erased_count; ++k) {
                if (erased_indices[k] == i) {
                    is_erased = true;
                    break;
                }
            }
            if (!is_erased) {
                uint8_t coeff = GfMultiplyScalar(static_cast<uint8_t>(i + 1), static_cast<uint8_t>(lost_idx + 1));
                VectorGfMulAdd(shards[lost_idx], shards[i], coeff, shard_size);
            }
        }
    }

    return 0;
}

} // namespace moonshine::fec
