#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>
#include "moonshine/jitter_buffer/jitter_buffer.hpp"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::jitter;

void TestSingleSliceFrame()
{
    std::cout << "[Test] JitterBuffer single-slice frame completion..." << std::endl;
    JitterBuffer jb(16);
    uint8_t payload[100] = {1, 2, 3};

    MoonshinePacketDesc packet{};
    packet.sequence_number = 1;
    packet.frame_index = 100;
    packet.packet_index = 0;
    packet.total_packets = 1;
    packet.payload_size = sizeof(payload);
    packet.flags = 0x03; // Frame Start (0x01) | Frame End (0x02)
    packet.payload_ptr = payload;

    int push_res = jb.PushPacket(packet);
    TEST_ASSERT(push_res == 1); // 1 = Frame is complete

    MoonshineFrameDesc completed{};
    int pop_res = jb.PopFrame(completed);
    TEST_ASSERT(pop_res == 1);
    TEST_ASSERT(completed.frame_index == 100);
    TEST_ASSERT(completed.packet_count == 1);
    TEST_ASSERT(completed.total_bytes == sizeof(payload));
}

void TestMultiSliceReverseOrder()
{
    std::cout << "[Test] JitterBuffer multi-slice out-of-order reverse arrival..." << std::endl;
    JitterBuffer jb(16);
    constexpr uint16_t kSlices = 4;
    constexpr uint16_t kSliceSize = 1000;

    std::vector<uint8_t> payload(kSliceSize, 0xAB);
    std::vector<MoonshinePacketDesc> packets(kSlices);

    for (uint16_t i = 0; i < kSlices; i++)
    {
        packets[i].sequence_number = 50 + i;
        packets[i].frame_index = 200;
        packets[i].packet_index = i;
        packets[i].total_packets = kSlices;
        packets[i].payload_size = kSliceSize;
        packets[i].payload_ptr = payload.data();
        packets[i].flags = (i == 0 ? 0x01 : 0) | (i == kSlices - 1 ? 0x02 : 0);
    }

    // Push in reverse: slice 3, 2, 1, 0
    for (int i = kSlices - 1; i >= 0; i--)
    {
        int res = jb.PushPacket(packets[i]);
        if (i == 0)
        {
            TEST_ASSERT(res == 1); // Completed
        }
        else
        {
            TEST_ASSERT(res == 0); // Still assembling
        }
    }

    MoonshineFrameDesc completed{};
    TEST_ASSERT(jb.PopFrame(completed) == 1);
    TEST_ASSERT(completed.frame_index == 200);
    TEST_ASSERT(completed.packet_count == kSlices);
    TEST_ASSERT(completed.total_bytes == kSlices * kSliceSize);
}

int main()
{
    std::cout << "=== Running Jitter Buffer Test Suite ===" << std::endl;
    TestSingleSliceFrame();
    TestMultiSliceReverseOrder();
    std::cout << "All Jitter Buffer tests passed successfully." << std::endl;
    return 0;
}
