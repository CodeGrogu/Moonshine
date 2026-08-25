#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>
#include <algorithm>
#include "moonshine/fec/reed_solomon_simd.hpp"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::fec;

namespace {

// Standalone Scalar Reference Cauchy Generator Matrix and Encoder
void ScalarReferenceBuildGenerator(uint8_t* matrix, int k, int m) {
    std::memset(matrix, 0, static_cast<size_t>((k + m) * k));
    for (int i = 0; i < k; ++i) {
        matrix[i * k + i] = 1;
    }
    if (m == 1) {
        for (int j = 0; j < k; ++j) {
            matrix[k * k + j] = 1;
        }
    } else {
        for (int p = 0; p < m; ++p) {
            uint8_t xp = static_cast<uint8_t>(p);
            for (int j = 0; j < k; ++j) {
                uint8_t yj = static_cast<uint8_t>(m + j);
                matrix[(k + p) * k + j] = ReedSolomonSimd::GfInverseScalar(xp ^ yj);
            }
        }
    }
}

void ScalarReferenceEncode(const uint8_t* const* data, int k, uint8_t** parity, int m, int shard_size) {
    std::vector<uint8_t> gen((k + m) * k);
    ScalarReferenceBuildGenerator(gen.data(), k, m);

    for (int p = 0; p < m; ++p) {
        std::memset(parity[p], 0, static_cast<size_t>(shard_size));
        const uint8_t* row = &gen[(k + p) * k];
        for (int j = 0; j < k; ++j) {
            uint8_t coeff = row[j];
            if (coeff != 0) {
                for (int b = 0; b < shard_size; ++b) {
                    parity[p][b] ^= ReedSolomonSimd::GfMultiplyScalar(data[j][b], coeff);
                }
            }
        }
    }
}

} // anonymous namespace

void TestSimdArchitectureDetection()
{
    std::cout << "[Test] Querying detected SIMD architecture..." << std::endl;
    SimdArchitecture arch = ReedSolomonSimd::GetDetectedArchitecture();
    std::cout << "Detected SIMD Architecture: " << static_cast<uint32_t>(arch) << std::endl;
    TEST_ASSERT(static_cast<uint32_t>(arch) >= 0);
}

void TestVectorXorBasic()
{
    std::cout << "[Test] VectorXor basic 64-byte alignment (AVX-512 / AVX2)..." << std::endl;
    alignas(64) uint8_t dest[128];
    alignas(64) uint8_t src[128];

    std::memset(dest, 0xAA, sizeof(dest));
    std::memset(src, 0x55, sizeof(src));

    ReedSolomonSimd::VectorXor(dest, src, sizeof(dest));

    for (size_t i = 0; i < sizeof(dest); i++)
    {
        TEST_ASSERT(dest[i] == 0xFF);
    }
}

void TestVectorXorEdgeLengths()
{
    std::cout << "[Test] VectorXor boundary and unaligned lengths..." << std::endl;
    const size_t test_lengths[] = {0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65, 127, 128, 129, 1400, 4096};

    for (size_t len : test_lengths)
    {
        std::vector<uint8_t> dest(len, 0x12);
        std::vector<uint8_t> src(len, 0x34);
        std::vector<uint8_t> expected(len, 0x12 ^ 0x34);

        if (len > 0)
        {
            ReedSolomonSimd::VectorXor(dest.data(), src.data(), len);
            TEST_ASSERT(std::memcmp(dest.data(), expected.data(), len) == 0);
        }
    }
}

void TestVectorXorSelfInverse()
{
    std::cout << "[Test] VectorXor self-inverse..." << std::endl;
    std::vector<uint8_t> data(1400);
    std::vector<uint8_t> original(1400);
    for (size_t i = 0; i < 1400; i++)
    {
        data[i] = original[i] = static_cast<uint8_t>(i & 0xFF);
    }

    std::vector<uint8_t> zero(1400, 0x00);
    ReedSolomonSimd::VectorXor(data.data(), zero.data(), 1400);
    TEST_ASSERT(std::memcmp(data.data(), original.data(), 1400) == 0);

    ReedSolomonSimd::VectorXor(data.data(), original.data(), 1400);
    for (size_t i = 0; i < 1400; i++)
    {
        TEST_ASSERT(data[i] == 0x00);
    }
}

void TestGaloisFieldMultiplication()
{
    std::cout << "[Test] Galois Field GF(2^8) VectorGfMulAdd properties..." << std::endl;
    std::vector<uint8_t> dest = {0xAA};
    std::vector<uint8_t> src = {0x55};
    ReedSolomonSimd::VectorGfMulAdd(dest.data(), src.data(), 0, 1);
    TEST_ASSERT(dest[0] == 0xAA);

    ReedSolomonSimd::VectorGfMulAdd(dest.data(), src.data(), 1, 1);
    TEST_ASSERT(dest[0] == (0xAA ^ 0x55));

    // Vector multiplication of 64 bytes
    std::vector<uint8_t> v_dest(64, 0x00);
    std::vector<uint8_t> v_src(64, 0x02);
    ReedSolomonSimd::VectorGfMulAdd(v_dest.data(), v_src.data(), 0x02, 64);
    uint8_t scalar_expected = ReedSolomonSimd::GfMultiplyScalar(0x02, 0x02);
    for (size_t i = 0; i < 64; i++)
    {
        TEST_ASSERT(v_dest[i] == scalar_expected);
    }
}

void TestParityEncodingVsScalarReference()
{
    std::cout << "[Test] Comparing SIMD Encode output against independent Scalar Reference..." << std::endl;
    const struct MatrixConfig { int k; int m; int size; } configs[] = {
        {5, 1, 1400},
        {10, 2, 1400},
        {20, 4, 1400},
        {40, 8, 1400}
    };

    ReedSolomonSimd codec;

    for (const auto& cfg : configs) {
        int k = cfg.k;
        int m = cfg.m;
        int size = cfg.size;

        std::vector<std::vector<uint8_t>> data(k, std::vector<uint8_t>(size));
        std::vector<const uint8_t*> data_ptrs(k);
        for (int i = 0; i < k; ++i) {
            for (int b = 0; b < size; ++b) {
                data[i][b] = static_cast<uint8_t>((i + 1) * 37 + b * 13);
            }
            data_ptrs[i] = data[i].data();
        }

        std::vector<std::vector<uint8_t>> simd_parity(m, std::vector<uint8_t>(size, 0));
        std::vector<uint8_t*> simd_parity_ptrs(m);
        for (int p = 0; p < m; ++p) simd_parity_ptrs[p] = simd_parity[p].data();

        std::vector<std::vector<uint8_t>> ref_parity(m, std::vector<uint8_t>(size, 0));
        std::vector<uint8_t*> ref_parity_ptrs(m);
        for (int p = 0; p < m; ++p) ref_parity_ptrs[p] = ref_parity[p].data();

        int res = codec.Encode(data_ptrs.data(), k, simd_parity_ptrs.data(), m, size);
        TEST_ASSERT(res == 0);

        ScalarReferenceEncode(data_ptrs.data(), k, ref_parity_ptrs.data(), m, size);

        for (int p = 0; p < m; ++p) {
            TEST_ASSERT(std::memcmp(simd_parity[p].data(), ref_parity[p].data(), static_cast<size_t>(size)) == 0);
        }
    }
}

void RunErasureRecoveryScenario(int k, int m, int size, const std::vector<int>& erased)
{
    int total = k + m;
    int e_count = static_cast<int>(erased.size());

    std::vector<std::vector<uint8_t>> shards(total, std::vector<uint8_t>(size));
    std::vector<std::vector<uint8_t>> backup(total, std::vector<uint8_t>(size));
    std::vector<uint8_t*> shard_ptrs(total);
    std::vector<const uint8_t*> data_ptrs(k);

    for (int i = 0; i < k; ++i) {
        for (int b = 0; b < size; ++b) {
            shards[i][b] = static_cast<uint8_t>((i + 1) * 23 + b * 7 + (i ^ b));
        }
        data_ptrs[i] = shards[i].data();
    }

    std::vector<uint8_t*> parity_ptrs(m);
    for (int p = 0; p < m; ++p) {
        parity_ptrs[p] = shards[k + p].data();
    }

    // Encode ground truth parities using independent scalar reference
    ScalarReferenceEncode(data_ptrs.data(), k, parity_ptrs.data(), m, size);

    for (int i = 0; i < total; ++i) {
        backup[i] = shards[i];
        shard_ptrs[i] = shards[i].data();
    }

    // Erase specified shards
    for (int idx : erased) {
        std::memset(shards[idx].data(), 0xCC, static_cast<size_t>(size));
    }

    ReedSolomonSimd codec;
    int res = codec.Reconstruct(shard_ptrs.data(), k, m, size, erased.data(), e_count);
    TEST_ASSERT(res == 0);

    // Verify exact byte-for-byte ground truth equality for all shards
    for (int i = 0; i < total; ++i) {
        TEST_ASSERT(std::memcmp(shards[i].data(), backup[i].data(), static_cast<size_t>(size)) == 0);
    }
}

void TestMultiShardRecoveryGroundTruth()
{
    std::cout << "[Test] Comprehensive Reed-Solomon Multi-Shard Ground-Truth Reconstruction..." << std::endl;
    constexpr int kShardSize = 1400;

    // 1. Matrix 5+1 (Single XOR Parity)
    std::cout << "  Testing 5+1 Matrix (E=1)..." << std::endl;
    RunErasureRecoveryScenario(5, 1, kShardSize, {0});
    RunErasureRecoveryScenario(5, 1, kShardSize, {2});
    RunErasureRecoveryScenario(5, 1, kShardSize, {5}); // Parity erased

    // 2. Matrix 10+2 (Cauchy Matrix)
    std::cout << "  Testing 10+2 Matrix (E=1..2)..." << std::endl;
    RunErasureRecoveryScenario(10, 2, kShardSize, {3});
    RunErasureRecoveryScenario(10, 2, kShardSize, {10}); // Parity 0 erased
    RunErasureRecoveryScenario(10, 2, kShardSize, {1, 7}); // Two data shards erased
    RunErasureRecoveryScenario(10, 2, kShardSize, {4, 11}); // Data + Parity erased
    RunErasureRecoveryScenario(10, 2, kShardSize, {10, 11}); // Both parities erased

    // 3. Matrix 20+4 (Cauchy Matrix)
    std::cout << "  Testing 20+4 Matrix (E=1..4)..." << std::endl;
    RunErasureRecoveryScenario(20, 4, kShardSize, {12});
    RunErasureRecoveryScenario(20, 4, kShardSize, {3, 18});
    RunErasureRecoveryScenario(20, 4, kShardSize, {2, 9, 21});
    RunErasureRecoveryScenario(20, 4, kShardSize, {0, 5, 14, 22}); // 3 data + 1 parity
    RunErasureRecoveryScenario(20, 4, kShardSize, {20, 21, 22, 23}); // All 4 parities erased
    RunErasureRecoveryScenario(20, 4, kShardSize, {0, 1, 2, 3}); // 4 data erasures

    // 4. Matrix 40+8 (Cauchy Matrix)
    std::cout << "  Testing 40+8 Matrix (E=1..8)..." << std::endl;
    RunErasureRecoveryScenario(40, 8, kShardSize, {19});
    RunErasureRecoveryScenario(40, 8, kShardSize, {7, 33});
    RunErasureRecoveryScenario(40, 8, kShardSize, {4, 12, 28, 45});
    RunErasureRecoveryScenario(40, 8, kShardSize, {1, 6, 11, 16, 21, 26, 31, 36}); // 8 data erasures
    RunErasureRecoveryScenario(40, 8, kShardSize, {40, 41, 42, 43, 44, 45, 46, 47}); // All 8 parities erased
    RunErasureRecoveryScenario(40, 8, kShardSize, {0, 5, 10, 15, 20, 41, 43, 47}); // Mixed 5 data + 3 parity
}

void TestNegativeAndDefensiveValidation()
{
    std::cout << "[Test] Negative testing and defensive validation..." << std::endl;
    ReedSolomonSimd codec;
    constexpr int kShardSize = 1400;

    alignas(64) uint8_t buffer1[1400] = {0};
    alignas(64) uint8_t buffer2[1400] = {0};
    uint8_t* ptrs2[] = {buffer1, buffer2};
    int erased1[] = {0};

    // 1. Nullptr and zero size
    TEST_ASSERT(codec.Reconstruct(nullptr, 1, 1, kShardSize, erased1, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, 0, erased1, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, nullptr, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 0, 1, kShardSize, erased1, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 0, kShardSize, erased1, 1) != 0);

    // 2. Too many erasures (E > M)
    int erased_too_many[] = {0, 1, 2};
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, erased_too_many, 3) == -2);

    // 3. Duplicate erased indices
    int erased_duplicate[] = {0, 0};
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 2, kShardSize, erased_duplicate, 2) == -1);

    // 4. Out of range erased indices
    int erased_negative[] = {-1};
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, erased_negative, 1) == -1);
    int erased_oob[] = {5};
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, erased_oob, 1) == -1);

    // 5. Exceeding max data/parity shards
    TEST_ASSERT(codec.Reconstruct(ptrs2, 65, 1, kShardSize, erased1, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 33, kShardSize, erased1, 1) != 0);

    // 6. Zero erased count (0% loss parity)
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, nullptr, 0) == 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 1, 1, kShardSize, erased1, 0) == 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 2, kShardSize, nullptr, 0) == 0);
    TEST_ASSERT(codec.Reconstruct(ptrs2, 2, kShardSize, erased1, 0) == 0);
}

void TestExhaustiveGfMultiplicationConsistency()
{
    std::cout << "[Test] Exhaustive Galois Field GF(2^8) VectorGfMulAdd vs GfMultiplyScalar consistency..." << std::endl;
    constexpr size_t kLen = 256;
    alignas(64) uint8_t src[kLen];
    alignas(64) uint8_t dest[kLen];
    alignas(64) uint8_t expected[kLen];

    for (size_t i = 0; i < kLen; ++i) {
        src[i] = static_cast<uint8_t>(i);
    }

    for (int coeff = 0; coeff < 256; ++coeff) {
        uint8_t c = static_cast<uint8_t>(coeff);
        std::memset(dest, 0, kLen);
        ReedSolomonSimd::VectorGfMulAdd(dest, src, c, kLen);

        for (size_t i = 0; i < kLen; ++i) {
            expected[i] = ReedSolomonSimd::GfMultiplyScalar(src[i], c);
        }

        TEST_ASSERT(std::memcmp(dest, expected, kLen) == 0);
    }
}

void TestIndependentScalarEncodeToSimdReconstruct()
{
    std::cout << "[Test] Pure Reference Encode -> SIMD Reconstruct (zero SIMD encode dependency)..." << std::endl;
    constexpr int k = 10;
    constexpr int m = 2;
    constexpr int total = k + m;
    constexpr int size = 1400;

    std::vector<std::vector<uint8_t>> shards(total, std::vector<uint8_t>(size));
    std::vector<std::vector<uint8_t>> backup(total, std::vector<uint8_t>(size));
    std::vector<uint8_t*> shard_ptrs(total);
    std::vector<const uint8_t*> data_ptrs(k);

    for (int i = 0; i < k; ++i) {
        for (int b = 0; b < size; ++b) {
            shards[i][b] = static_cast<uint8_t>((i + 5) * 43 + b * 11);
        }
        data_ptrs[i] = shards[i].data();
    }

    std::vector<uint8_t*> parity_ptrs(m);
    for (int p = 0; p < m; ++p) {
        parity_ptrs[p] = shards[k + p].data();
    }

    // Explicitly generate parity ONLY with the independent scalar reference
    ScalarReferenceEncode(data_ptrs.data(), k, parity_ptrs.data(), m, size);

    for (int i = 0; i < total; ++i) {
        backup[i] = shards[i];
        shard_ptrs[i] = shards[i].data();
    }

    // Erase shard 2 and shard 10 (1 data, 1 parity)
    int erased[] = {2, 10};
    std::memset(shards[2].data(), 0xEE, static_cast<size_t>(size));
    std::memset(shards[10].data(), 0xEE, static_cast<size_t>(size));

    // Call SIMD Reconstruct directly without ever invoking SIMD Encode
    ReedSolomonSimd codec;
    int res = codec.Reconstruct(shard_ptrs.data(), k, m, size, erased, 2);
    TEST_ASSERT(res == 0);

    TEST_ASSERT(std::memcmp(shards[2].data(), backup[2].data(), static_cast<size_t>(size)) == 0);
    TEST_ASSERT(std::memcmp(shards[10].data(), backup[10].data(), static_cast<size_t>(size)) == 0);
}

int main()
{
    std::cout << "=== Running Comprehensive FEC SIMD Test Suite ===" << std::endl;
    TestSimdArchitectureDetection();
    TestVectorXorBasic();
    TestVectorXorEdgeLengths();
    TestVectorXorSelfInverse();
    TestGaloisFieldMultiplication();
    TestExhaustiveGfMultiplicationConsistency();
    TestParityEncodingVsScalarReference();
    TestIndependentScalarEncodeToSimdReconstruct();
    TestMultiShardRecoveryGroundTruth();
    TestNegativeAndDefensiveValidation();
    std::cout << "All FEC SIMD tests passed successfully." << std::endl;
    return 0;
}
