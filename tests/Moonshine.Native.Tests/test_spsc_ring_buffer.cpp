#include <iostream>
#include <thread>
#include <vector>
#include <cstdlib>
#include "moonshine/ring_buffer/spsc_ring_buffer.hpp"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::ring_buffer;

void TestSingleItemPushPop()
{
    std::cout << "[Test] SPSC Single enqueue and dequeue..." << std::endl;
    SpscRingBuffer<uint64_t> ring(16);
    TEST_ASSERT(ring.Size() == 0);

    TEST_ASSERT(ring.TryEnqueue(42));
    TEST_ASSERT(ring.Size() == 1);

    uint64_t val = 0;
    TEST_ASSERT(ring.TryDequeue(val));
    TEST_ASSERT(val == 42);
    TEST_ASSERT(ring.Size() == 0);
}

void TestFullAndEmptyBoundaries()
{
    std::cout << "[Test] SPSC Full capacity and empty boundary rejection..." << std::endl;
    SpscRingBuffer<int> ring(4);

    int out = -1;
    TEST_ASSERT(!ring.TryDequeue(out));

    for (int i = 0; i < 4; i++)
    {
        TEST_ASSERT(ring.TryEnqueue(i + 100));
    }

    TEST_ASSERT(!ring.TryEnqueue(999));
    TEST_ASSERT(ring.Size() == 4);

    for (int i = 0; i < 4; i++)
    {
        TEST_ASSERT(ring.TryDequeue(out));
        TEST_ASSERT(out == i + 100);
    }

    TEST_ASSERT(!ring.TryDequeue(out));
    TEST_ASSERT(ring.Size() == 0);
}

void TestWrapAroundContinuity()
{
    std::cout << "[Test] SPSC Ring buffer index wraparound..." << std::endl;
    SpscRingBuffer<int> ring(4);

    for (int cycle = 0; cycle < 10000; cycle++)
    {
        TEST_ASSERT(ring.TryEnqueue(cycle));
        int out = -1;
        TEST_ASSERT(ring.TryDequeue(out));
        TEST_ASSERT(out == cycle);
    }
    TEST_ASSERT(ring.Size() == 0);
}

void TestMultiThreadedStress()
{
    std::cout << "[Test] SPSC 100,000 items multi-threaded lock-free stress test..." << std::endl;
    constexpr size_t kItems = 100000;
    auto ring = std::make_unique<SpscRingBuffer<uint64_t>>(1024);

    std::thread producer([&]() {
        for (uint64_t i = 1; i <= kItems; i++)
        {
            while (!ring->TryEnqueue(i))
            {
                std::this_thread::yield();
            }
        }
    });

    std::thread consumer([&]() {
        uint64_t expected = 1;
        uint64_t val = 0;
        while (expected <= kItems)
        {
            if (ring->TryDequeue(val))
            {
                TEST_ASSERT(val == expected);
                expected++;
            }
            else
            {
                std::this_thread::yield();
            }
        }
    });

    producer.join();
    consumer.join();
    TEST_ASSERT(ring->Size() == 0);
}

void TestInterleavedSizeTracking()
{
    std::cout << "[Test] SPSC Size() consistency during interleaved operations..." << std::endl;
    SpscRingBuffer<int> ring(8);
    TEST_ASSERT(ring.Size() == 0);

    for (int cycle = 0; cycle < 5000; cycle++)
    {
        TEST_ASSERT(ring.TryEnqueue(cycle));
        TEST_ASSERT(ring.TryEnqueue(cycle + 1));
        TEST_ASSERT(ring.Size() == 2);

        int out1 = 0, out2 = 0;
        TEST_ASSERT(ring.TryDequeue(out1));
        TEST_ASSERT(ring.Size() == 1);
        TEST_ASSERT(ring.TryDequeue(out2));
        TEST_ASSERT(ring.Size() == 0);

        TEST_ASSERT(out1 == cycle);
        TEST_ASSERT(out2 == cycle + 1);
    }
    TEST_ASSERT(ring.Size() == 0);
}

void TestMaxUint64Rollover()
{
    std::cout << "[Test] SPSC Monotonic counter overflow and wrapping across SIZE_MAX boundary..." << std::endl;
    constexpr size_t kMax = std::numeric_limits<size_t>::max();
    SpscRingBuffer<int> ring(8);

    // Initialise head and tail near wrap boundary (kMax - 3)
    ring.SetHeadTailForTesting(kMax - 3, kMax - 3);
    TEST_ASSERT(ring.Size() == 0);

    // Enqueue 8 items across the wrap boundary
    for (int i = 0; i < 8; i++)
    {
        TEST_ASSERT(ring.TryEnqueue(100 + i));
    }
    TEST_ASSERT(ring.Size() == 8);
    TEST_ASSERT(!ring.TryEnqueue(999)); // Full

    // Dequeue all items across the boundary
    for (int i = 0; i < 8; i++)
    {
        int val = -1;
        TEST_ASSERT(ring.TryDequeue(val));
        TEST_ASSERT(val == 100 + i);
    }
    TEST_ASSERT(ring.Size() == 0);
    int emptyVal = -1;
    TEST_ASSERT(!ring.TryDequeue(emptyVal));
}

int main()
{
    std::cout << "=== Running SPSC Ring Buffer Test Suite ===" << std::endl;
    TestSingleItemPushPop();
    TestFullAndEmptyBoundaries();
    TestWrapAroundContinuity();
    TestInterleavedSizeTracking();
    TestMaxUint64Rollover();
    TestMultiThreadedStress();
    std::cout << "All SPSC Ring Buffer tests passed successfully." << std::endl;
    return 0;
}
