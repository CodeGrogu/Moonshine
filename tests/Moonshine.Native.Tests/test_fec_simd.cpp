#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>

#define MOONSHINE_TEST_ASSERT(cond) \
    do { \
        if (!(cond)) { \
            std::cerr << "Assertion failed: " #cond << " at " << __FILE__ << ":" << __LINE__ << "\n"; \
            std::exit(1); \
        } \
    } while (0)

void test_vector_xor_abi() {
    std::cout << "[Test] moonshine_vector_xor AVX2 / SIMD ... ";
    constexpr size_t size = 1024;
    std::vector<uint8_t> a(size, 0xAA);
    std::vector<uint8_t> b(size, 0x55);

    moonshine_vector_xor(a.data(), b.data(), size);

    for (size_t i = 0; i < size; ++i) {
        MOONSHINE_TEST_ASSERT(a[i] == 0xFF);
    }
    std::cout << "PASSED\n";
}

void test_fec_single_parity_recovery_abi() {
    std::cout << "[Test] moonshine_fec_recover_simd Single Parity Recovery ... ";
    constexpr int shard_count = 5;
    constexpr int shard_size = 1400;

    std::vector<std::vector<uint8_t>> shards_mem(shard_count, std::vector<uint8_t>(shard_size));
    std::vector<uint8_t*> shards(shard_count);

    for (int i = 0; i < shard_count; ++i) {
        shards[i] = shards_mem[i].data();
        std::fill(shards_mem[i].begin(), shards_mem[i].end(), static_cast<uint8_t>(i + 1));
    }

    // Compute parity shard at index 4 = XOR(0, 1, 2, 3)
    std::memset(shards[4], 0, shard_size);
    for (int i = 0; i < 4; ++i) {
        moonshine_vector_xor(shards[4], shards[i], shard_size);
    }

    // Simulate loss of shard index 1 (originally filled with 2)
    std::memset(shards[1], 0, shard_size);
    int erased[] = { 1 };

    int result = moonshine_fec_recover_simd(shards.data(), shard_count, shard_size, erased, 1);
    MOONSHINE_TEST_ASSERT(result == 0);

    // Verify recovered shard 1 matches original value (2)
    for (int i = 0; i < shard_size; ++i) {
        MOONSHINE_TEST_ASSERT(shards[1][i] == 2);
    }
    std::cout << "PASSED\n";
}

int main() {
    std::cout << "========================================\n";
    std::cout << "Moonshine Native FEC Test Suite\n";
    std::cout << "========================================\n";
    test_vector_xor_abi();
    test_fec_single_parity_recovery_abi();
    std::cout << "All Native FEC tests PASSED!\n";
    return 0;
}
