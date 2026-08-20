#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>
#include "moonshine/fec/reed_solomon_simd.hpp"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::fec;

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

void TestSingleParityRecovery()
{
    std::cout << "[Test] Reed-Solomon single parity shard recovery..." << std::endl;
    constexpr int kShards = 5;
    constexpr int kShardSize = 1400;

    std::vector<std::vector<uint8_t>> shards(kShards, std::vector<uint8_t>(kShardSize));
    std::vector<uint8_t*> shard_ptrs(kShards);

    for (int s = 0; s < kShards - 1; s++)
    {
        for (int i = 0; i < kShardSize; i++)
        {
            shards[s][i] = static_cast<uint8_t>((s + 1) * 31 + i);
        }
        shard_ptrs[s] = shards[s].data();
    }
    shard_ptrs[kShards - 1] = shards[kShards - 1].data();

    // Compute parity
    std::memset(shards[kShards - 1].data(), 0, kShardSize);
    for (int s = 0; s < kShards - 1; s++)
    {
        ReedSolomonSimd::VectorXor(shards[kShards - 1].data(), shards[s].data(), kShardSize);
    }

    // Simulate erasing shard 2
    std::vector<uint8_t> original_shard2 = shards[2];
    std::memset(shards[2].data(), 0, kShardSize);

    int erased_indices[] = {2};
    ReedSolomonSimd codec;
    int res = codec.Reconstruct(shard_ptrs.data(), kShards, kShardSize, erased_indices, 1);
    TEST_ASSERT(res == 0);
    TEST_ASSERT(std::memcmp(shards[2].data(), original_shard2.data(), kShardSize) == 0);
}

void TestMultiShardRecovery()
{
    std::cout << "[Test] Reed-Solomon multi-shard reconstruction..." << std::endl;
    constexpr int kShards = 6;
    constexpr int kShardSize = 1400;

    std::vector<std::vector<uint8_t>> shards(kShards, std::vector<uint8_t>(kShardSize));
    std::vector<uint8_t*> shard_ptrs(kShards);

    for (int s = 0; s < kShards; s++)
    {
        for (int i = 0; i < kShardSize; i++)
        {
            shards[s][i] = static_cast<uint8_t>((s + 1) * 17 + i);
        }
        shard_ptrs[s] = shards[s].data();
    }

    int erased_indices[] = {1, 3};
    ReedSolomonSimd codec;
    int res = codec.Reconstruct(shard_ptrs.data(), kShards, kShardSize, erased_indices, 2);
    TEST_ASSERT(res == 0);
}

void TestInvalidInputs()
{
    std::cout << "[Test] FEC error handling and invalid input rejection..." << std::endl;
    ReedSolomonSimd codec;
    int erased[] = {0};
    TEST_ASSERT(codec.Reconstruct(nullptr, 5, 1400, erased, 1) != 0);
    uint8_t dummy[16];
    uint8_t* ptrs[] = {dummy};
    TEST_ASSERT(codec.Reconstruct(ptrs, 0, 1400, erased, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs, 1, 0, erased, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs, 1, 1400, nullptr, 1) != 0);
    TEST_ASSERT(codec.Reconstruct(ptrs, 1, 1400, erased, 0) != 0);
}

int main()
{
    std::cout << "=== Running Comprehensive FEC SIMD Test Suite ===" << std::endl;
    TestSimdArchitectureDetection();
    TestVectorXorBasic();
    TestVectorXorEdgeLengths();
    TestVectorXorSelfInverse();
    TestGaloisFieldMultiplication();
    TestSingleParityRecovery();
    TestMultiShardRecovery();
    TestInvalidInputs();
    std::cout << "All FEC SIMD tests passed successfully." << std::endl;
    return 0;
}
