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

#if defined(_MSC_VER)
static bool CheckCpuFeature(int leaf, int subleaf, int regIndex, int bitIndex) noexcept {
    int cpu_info[4];
    __cpuid(cpu_info, 0);
    if (cpu_info[0] < leaf) return false;
    __cpuidex(cpu_info, leaf, subleaf);
    return (cpu_info[regIndex] & (1 << bitIndex)) != 0;
}
#endif

} // anonymous namespace

ReedSolomonSimd::ReedSolomonSimd() {
    (void)tables_initialized;
}

bool ReedSolomonSimd::HasAvx2Support() noexcept {
#if defined(_MSC_VER)
    return CheckCpuFeature(7, 0, 1, 5); // EBX bit 5: AVX2
#elif defined(__GNUC__) || defined(__clang__)
    return __builtin_cpu_supports("avx2");
#else
    return false;
#endif
}

bool ReedSolomonSimd::HasAvx512Support() noexcept {
#if defined(_MSC_VER)
    return CheckCpuFeature(7, 0, 1, 16) && CheckCpuFeature(7, 0, 1, 30); // EBX bit 16: AVX512F, bit 30: AVX512BW
#elif defined(__GNUC__) || defined(__clang__)
    return __builtin_cpu_supports("avx512f") && __builtin_cpu_supports("avx512bw");
#else
    return false;
#endif
}

SimdArchitecture ReedSolomonSimd::GetDetectedArchitecture() noexcept {
    if (HasAvx512Support()) return SimdArchitecture::Avx512;
    if (HasAvx2Support()) return SimdArchitecture::Avx2;
    return SimdArchitecture::Scalar;
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
    if (!dest || !src || length == 0) return;

    size_t i = 0;

#if (defined(_MSC_VER) && (defined(_M_AMD64) || defined(_M_X64))) || defined(__AVX512F__) || defined(MOONSHINE_HAS_AVX512)
    static const bool s_has_avx512 = HasAvx512Support();
    if (s_has_avx512) {
        for (; i + 64 <= length; i += 64) {
            __m512i vd = _mm512_loadu_si512(reinterpret_cast<const void*>(dest + i));
            __m512i vs = _mm512_loadu_si512(reinterpret_cast<const void*>(src + i));
            _mm512_storeu_si512(reinterpret_cast<void*>(dest + i), _mm512_xor_si512(vd, vs));
        }
    }
#endif

#if defined(__AVX2__) || defined(MOONSHINE_HAS_AVX2)
    static const bool s_has_avx2 = HasAvx2Support();
    if (s_has_avx2) {
        for (; i + 32 <= length; i += 32) {
            __m256i vd = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(dest + i));
            __m256i vs = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(src + i));
            _mm256_storeu_si256(reinterpret_cast<__m256i*>(dest + i), _mm256_xor_si256(vd, vs));
        }
    }
#endif

    // Remainder 8-byte chunks
    for (; i + 8 <= length; i += 8) {
        *reinterpret_cast<uint64_t*>(dest + i) ^= *reinterpret_cast<const uint64_t*>(src + i);
    }

    // Scalar tail
    for (; i < length; ++i) {
        dest[i] ^= src[i];
    }
}

void ReedSolomonSimd::VectorGfMulAdd(uint8_t* dest, const uint8_t* src, uint8_t coeff, size_t length) noexcept {
    if (!dest || !src || length == 0 || coeff == 0) return;
    if (coeff == 1) {
        VectorXor(dest, src, length);
        return;
    }

    size_t i = 0;

#if (defined(_MSC_VER) && (defined(_M_AMD64) || defined(_M_X64))) || defined(__AVX512BW__) || defined(MOONSHINE_HAS_AVX512)
    static const bool s_has_avx512 = HasAvx512Support();
    if (s_has_avx512 && (i + 64 <= length)) {
        alignas(64) uint8_t low_table[64];
        alignas(64) uint8_t high_table[64];
        for (uint8_t nibble = 0; nibble < 16; ++nibble) {
            uint8_t low_val = GfMultiplyScalar(nibble, coeff);
            uint8_t high_val = GfMultiplyScalar(static_cast<uint8_t>(nibble << 4), coeff);
            for (int lane = 0; lane < 4; ++lane) {
                low_table[lane * 16 + nibble] = low_val;
                high_table[lane * 16 + nibble] = high_val;
            }
        }
        __m512i v_low_table = _mm512_load_si512(reinterpret_cast<const void*>(low_table));
        __m512i v_high_table = _mm512_load_si512(reinterpret_cast<const void*>(high_table));
        __m512i v_mask_low = _mm512_set1_epi8(0x0F);

        for (; i + 64 <= length; i += 64) {
            __m512i v_src = _mm512_loadu_si512(reinterpret_cast<const void*>(src + i));
            __m512i v_dest = _mm512_loadu_si512(reinterpret_cast<const void*>(dest + i));

            __m512i v_src_low = _mm512_and_si512(v_src, v_mask_low);
            __m512i v_src_high = _mm512_and_si512(_mm512_srli_epi16(v_src, 4), v_mask_low);

            __m512i v_res_low = _mm512_shuffle_epi8(v_low_table, v_src_low);
            __m512i v_res_high = _mm512_shuffle_epi8(v_high_table, v_src_high);

            __m512i v_product = _mm512_xor_si512(v_res_low, v_res_high);
            __m512i v_out = _mm512_xor_si512(v_dest, v_product);

            _mm512_storeu_si512(reinterpret_cast<void*>(dest + i), v_out);
        }
    }
#endif

#if defined(__AVX2__) || defined(MOONSHINE_HAS_AVX2)
    static const bool s_has_avx2 = HasAvx2Support();
    if (s_has_avx2 && (i + 32 <= length)) {
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
    }
#endif

    // Scalar fallback for remainder
    for (; i < length; ++i) {
        dest[i] ^= GfMultiplyScalar(src[i], coeff);
    }
}

bool ReedSolomonSimd::BuildGeneratorMatrix(uint8_t* matrix, int k, int m) noexcept {
    if (!matrix || k <= 0 || m <= 0 || k > kMaxDataShards || m > kMaxParityShards || (k + m) > 255) {
        return false;
    }

    int total_shards = k + m;
    std::memset(matrix, 0, static_cast<size_t>(total_shards * k));

    // 1. Top K x K is Identity Matrix I_K
    for (int i = 0; i < k; ++i) {
        matrix[i * k + i] = 1;
    }

    // 2. Bottom M x K Parity Generator Matrix
    if (m == 1) {
        // Single parity XOR row (all-1s)
        for (int j = 0; j < k; ++j) {
            matrix[k * k + j] = 1;
        }
    } else {
        // Cauchy Matrix: G[K + p][j] = 1 / (x_p ^ y_j) where x_p = p in [0, M-1], y_j = M + j in [M, M+K-1]
        // Since x_p < M and y_j >= M, {x_p} and {y_j} are strictly disjoint, ensuring x_p ^ y_j != 0.
        for (int p = 0; p < m; ++p) {
            uint8_t xp = static_cast<uint8_t>(p);
            for (int j = 0; j < k; ++j) {
                uint8_t yj = static_cast<uint8_t>(m + j);
                uint8_t diff = xp ^ yj;
                matrix[(k + p) * k + j] = GfInverseScalar(diff);
            }
        }
    }

    return true;
}

bool ReedSolomonSimd::InvertMatrixGf256(const uint8_t* src, uint8_t* dst, int k) noexcept {
    if (!src || !dst || k <= 0 || k > kMaxDataShards) {
        return false;
    }

    // Stack-allocated scratch matrices (zero heap allocations)
    alignas(64) uint8_t a[kMaxDataShards][kMaxDataShards];
    alignas(64) uint8_t b[kMaxDataShards][kMaxDataShards];

    for (int r = 0; r < k; ++r) {
        for (int c = 0; c < k; ++c) {
            a[r][c] = src[r * k + c];
            b[r][c] = (r == c) ? 1 : 0;
        }
    }

    // Gauss-Jordan elimination over GF(2^8)
    for (int c = 0; c < k; ++c) {
        // Find non-zero pivot
        int pivot_row = c;
        while (pivot_row < k && a[pivot_row][c] == 0) {
            pivot_row++;
        }

        if (pivot_row == k) {
            // Matrix is singular
            return false;
        }

        // Swap pivot row with current row
        if (pivot_row != c) {
            for (int j = 0; j < k; ++j) {
                std::swap(a[c][j], a[pivot_row][j]);
                std::swap(b[c][j], b[pivot_row][j]);
            }
        }

        // Scale current row so pivot a[c][c] == 1
        uint8_t pivot_val = a[c][c];
        uint8_t inv_pivot = GfInverseScalar(pivot_val);
        for (int j = 0; j < k; ++j) {
            a[c][j] = GfMultiplyScalar(a[c][j], inv_pivot);
            b[c][j] = GfMultiplyScalar(b[c][j], inv_pivot);
        }

        // Eliminate all other rows
        for (int r = 0; r < k; ++r) {
            if (r != c) {
                uint8_t factor = a[r][c];
                if (factor != 0) {
                    for (int j = 0; j < k; ++j) {
                        a[r][j] ^= GfMultiplyScalar(a[c][j], factor);
                        b[r][j] ^= GfMultiplyScalar(b[c][j], factor);
                    }
                }
            }
        }
    }

    // Copy inverted matrix b into dst
    for (int r = 0; r < k; ++r) {
        for (int c = 0; c < k; ++c) {
            dst[r * k + c] = b[r][c];
        }
    }

    return true;
}

int ReedSolomonSimd::Encode(
    const uint8_t* const* data_shards,
    int data_shards_count,
    uint8_t** parity_shards,
    int parity_shards_count,
    int shard_size
) noexcept {
    if (!data_shards || !parity_shards || shard_size <= 0) return -1;
    if (data_shards_count <= 0 || data_shards_count > kMaxDataShards) return -1;
    if (parity_shards_count <= 0 || parity_shards_count > kMaxParityShards) return -1;
    if ((data_shards_count + parity_shards_count) > 255) return -1;

    for (int i = 0; i < data_shards_count; ++i) {
        if (!data_shards[i]) return -1;
    }
    for (int p = 0; p < parity_shards_count; ++p) {
        if (!parity_shards[p]) return -1;
    }

    // Fast path: Single parity XOR
    if (parity_shards_count == 1) {
        std::memcpy(parity_shards[0], data_shards[0], static_cast<size_t>(shard_size));
        for (int i = 1; i < data_shards_count; ++i) {
            VectorXor(parity_shards[0], data_shards[i], static_cast<size_t>(shard_size));
        }
        return 0;
    }

    // Multi-parity Cauchy Generator Matrix
    int k = data_shards_count;
    int m = parity_shards_count;
    alignas(64) uint8_t generator[kMaxTotalShards * kMaxDataShards];
    if (!BuildGeneratorMatrix(generator, k, m)) {
        return -1;
    }

    for (int p = 0; p < m; ++p) {
        std::memset(parity_shards[p], 0, static_cast<size_t>(shard_size));
        const uint8_t* row = &generator[(k + p) * k];
        for (int j = 0; j < k; ++j) {
            uint8_t coeff = row[j];
            if (coeff != 0) {
                VectorGfMulAdd(parity_shards[p], data_shards[j], coeff, static_cast<size_t>(shard_size));
            }
        }
    }

    return 0;
}

int ReedSolomonSimd::Reconstruct(
    uint8_t** shards,
    int data_shards_count,
    int parity_shards_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
) noexcept {
    if (!shards || !erased_indices) return -1;
    if (shard_size <= 0) return -1;
    if (data_shards_count <= 0 || data_shards_count > kMaxDataShards) return -1;
    if (parity_shards_count <= 0 || parity_shards_count > kMaxParityShards) return -1;
    if ((data_shards_count + parity_shards_count) > 255) return -1;
    if (erased_count <= 0) return -1;

    int total_shards = data_shards_count + parity_shards_count;
    if (erased_count > parity_shards_count) {
        return -2; // Too many erasures (unrecoverable)
    }

    for (int i = 0; i < total_shards; ++i) {
        if (!shards[i]) return -1;
    }

    // Validate unique erased indices within [0, total_shards)
    bool is_erased[kMaxTotalShards] = {false};
    for (int e = 0; e < erased_count; ++e) {
        int idx = erased_indices[e];
        if (idx < 0 || idx >= total_shards) return -1;
        if (is_erased[idx]) return -1; // Duplicate erased index
        is_erased[idx] = true;
    }

    int k = data_shards_count;
    int m = parity_shards_count;

    // Single parity fast path (M = 1, E = 1)
    if (m == 1 && erased_count == 1) {
        int lost_idx = erased_indices[0];
        std::memset(shards[lost_idx], 0, static_cast<size_t>(shard_size));
        for (int i = 0; i < total_shards; ++i) {
            if (i != lost_idx) {
                VectorXor(shards[lost_idx], shards[i], static_cast<size_t>(shard_size));
            }
        }
        return 0;
    }

    // Build generator matrix
    alignas(64) uint8_t generator[kMaxTotalShards * kMaxDataShards];
    if (!BuildGeneratorMatrix(generator, k, m)) {
        return -1;
    }

    // Select the first K non-erased surviving shards
    int survivor_indices[kMaxDataShards];
    int survivor_count = 0;
    for (int i = 0; i < total_shards && survivor_count < k; ++i) {
        if (!is_erased[i]) {
            survivor_indices[survivor_count++] = i;
        }
    }

    if (survivor_count < k) {
        return -2; // Insufficient surviving shards
    }

    // Build survivor matrix A[r][c] = generator[survivor_indices[r]][c]
    alignas(64) uint8_t survivor_matrix[kMaxDataShards * kMaxDataShards];
    for (int r = 0; r < k; ++r) {
        int s_idx = survivor_indices[r];
        for (int c = 0; c < k; ++c) {
            survivor_matrix[r * k + c] = generator[s_idx * k + c];
        }
    }

    // Invert survivor matrix
    alignas(64) uint8_t inverted_matrix[kMaxDataShards * kMaxDataShards];
    if (!InvertMatrixGf256(survivor_matrix, inverted_matrix, k)) {
        return -3; // Singular matrix (internal correctness failure)
    }

    // Phase 1: Reconstruct erased DATA shards (d < k)
    for (int d = 0; d < k; ++d) {
        if (is_erased[d]) {
            std::memset(shards[d], 0, static_cast<size_t>(shard_size));
            for (int r = 0; r < k; ++r) {
                uint8_t coeff = inverted_matrix[d * k + r];
                if (coeff != 0) {
                    int s_idx = survivor_indices[r];
                    VectorGfMulAdd(shards[d], shards[s_idx], coeff, static_cast<size_t>(shard_size));
                }
            }
        }
    }

    // Phase 2: Reconstruct erased PARITY shards (p >= k) from all restored data shards
    for (int p = k; p < total_shards; ++p) {
        if (is_erased[p]) {
            std::memset(shards[p], 0, static_cast<size_t>(shard_size));
            const uint8_t* row = &generator[p * k];
            for (int j = 0; j < k; ++j) {
                uint8_t coeff = row[j];
                if (coeff != 0) {
                    VectorGfMulAdd(shards[p], shards[j], coeff, static_cast<size_t>(shard_size));
                }
            }
        }
    }

    return 0;
}

int ReedSolomonSimd::Reconstruct(
    uint8_t** shards,
    int total_shards,
    int shard_size,
    const int* erased_indices,
    int erased_count
) noexcept {
    if (!shards || !erased_indices || total_shards <= 0 || shard_size <= 0 || erased_count <= 0) {
        return -1;
    }
    // Infer parity count as erased_count (or half if large)
    int parity_shards = erased_count;
    if (parity_shards >= total_shards) return -1;
    int data_shards = total_shards - parity_shards;
    return Reconstruct(shards, data_shards, parity_shards, shard_size, erased_indices, erased_count);
}

} // namespace moonshine::fec
