#include "moonshine/ring_buffer/spsc_ring_buffer.hpp"
#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <thread>
#include <vector>
#include <cstdlib>

#define MOONSHINE_TEST_ASSERT(cond) \
    do { \
        if (!(cond)) { \
            std::cerr << "Assertion failed: " #cond << " at " << __FILE__ << ":" << __LINE__ << "\n"; \
            std::exit(1); \
        } \
    } while (0)

using namespace moonshine::ring_buffer;

void test_spsc_basic() {
    std::cout << "[Test] SPSC Ring Buffer Basic Enqueue/Dequeue ... ";
    SpscRingBuffer<int> ring(8);

    MOONSHINE_TEST_ASSERT(ring.Size() == 0);
    MOONSHINE_TEST_ASSERT(ring.TryEnqueue(100));
    MOONSHINE_TEST_ASSERT(ring.TryEnqueue(200));
    MOONSHINE_TEST_ASSERT(ring.TryEnqueue(300));
    MOONSHINE_TEST_ASSERT(ring.Size() == 3);

    int val = 0;
    MOONSHINE_TEST_ASSERT(ring.TryDequeue(val));
    MOONSHINE_TEST_ASSERT(val == 100);
    MOONSHINE_TEST_ASSERT(ring.TryDequeue(val));
    MOONSHINE_TEST_ASSERT(val == 200);
    MOONSHINE_TEST_ASSERT(ring.Size() == 1);
    MOONSHINE_TEST_ASSERT(ring.TryDequeue(val));
    MOONSHINE_TEST_ASSERT(val == 300);
    MOONSHINE_TEST_ASSERT(ring.Size() == 0);
    MOONSHINE_TEST_ASSERT(!ring.TryDequeue(val));

    std::cout << "PASSED\n";
}

void test_spsc_threaded_stress() {
    std::cout << "[Test] SPSC Threaded Stress (1,000,000 items) ... ";
    constexpr size_t total_items = 1000000;
    SpscRingBuffer<size_t> ring(1024);

    std::thread producer([&]() {
        for (size_t i = 1; i <= total_items; ++i) {
            while (!ring.TryEnqueue(i)) {
                std::this_thread::yield();
            }
        }
    });

    std::thread consumer([&]() {
        size_t expected = 1;
        while (expected <= total_items) {
            size_t val = 0;
            if (ring.TryDequeue(val)) {
                MOONSHINE_TEST_ASSERT(val == expected);
                expected++;
            } else {
                std::this_thread::yield();
            }
        }
    });

    producer.join();
    consumer.join();
    std::cout << "PASSED\n";
}

int main() {
    std::cout << "========================================\n";
    std::cout << "Moonshine Native SPSC Test Suite\n";
    std::cout << "========================================\n";
    test_spsc_basic();
    test_spsc_threaded_stress();
    std::cout << "All SPSC tests PASSED!\n";
    return 0;
}
