#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>
#include "moonshine/export/moonshine_native_api.h"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

void TestExportVectorXor()
{
    std::cout << "[Test] C-ABI moonshine_vector_xor..." << std::endl;
    uint8_t dest[32] = {0xFF};
    uint8_t src[32] = {0x0F};
    moonshine_vector_xor(dest, src, 32);
    TEST_ASSERT(dest[0] == 0xF0);

    // Null safety
    moonshine_vector_xor(nullptr, src, 32);
    moonshine_vector_xor(dest, nullptr, 32);
}

void TestExportSpscLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_spsc lifecycle..." << std::endl;
    MoonshineRingBufferHandle handle = moonshine_spsc_create(128);
    TEST_ASSERT(handle != nullptr);

    TEST_ASSERT(moonshine_spsc_size(handle) == 0);

    MoonshinePacketDesc packet{};
    packet.frame_index = 55;
    packet.packet_index = 1;
    packet.payload_size = 100;

    TEST_ASSERT(moonshine_spsc_enqueue(handle, &packet) == 1);
    TEST_ASSERT(moonshine_spsc_size(handle) == 1);

    MoonshinePacketDesc popped{};
    TEST_ASSERT(moonshine_spsc_dequeue(handle, &popped) == 1);
    TEST_ASSERT(popped.frame_index == 55);
    TEST_ASSERT(popped.packet_index == 1);
    TEST_ASSERT(moonshine_spsc_size(handle) == 0);

    moonshine_spsc_destroy(handle);
}

void TestExportJitterLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_jitter lifecycle..." << std::endl;
    MoonshineJitterBufferHandle handle = moonshine_jitter_create(16);
    TEST_ASSERT(handle != nullptr);

    uint8_t payload[50] = {1, 2, 3};
    MoonshinePacketDesc packet{};
    packet.sequence_number = 1;
    packet.frame_index = 88;
    packet.packet_index = 0;
    packet.total_packets = 1;
    packet.payload_size = sizeof(payload);
    packet.flags = 0x03;
    packet.payload_ptr = payload;

    int push_res = moonshine_jitter_push_packet(handle, &packet);
    TEST_ASSERT(push_res == 1);

    MoonshineFrameDesc frame{};
    int pop_res = moonshine_jitter_pop_frame(handle, &frame);
    TEST_ASSERT(pop_res == 1);
    TEST_ASSERT(frame.frame_index == 88);
    TEST_ASSERT(frame.packet_count == 1);

    moonshine_jitter_destroy(handle);
}

void TestExportVideoCaps()
{
    std::cout << "[Test] C-ABI moonshine_video_query_caps..." << std::endl;
    MoonshineDecoderCaps caps{};
    int res = moonshine_video_query_caps(&caps);
    TEST_ASSERT(res == 0);
    TEST_ASSERT(caps.max_width >= 1920);
    TEST_ASSERT(caps.max_height >= 1080);
    TEST_ASSERT(caps.max_fps >= 60);
    TEST_ASSERT(caps.supports_hevc == 1);
    TEST_ASSERT(caps.supports_h264 == 1);

    // Null safety
    TEST_ASSERT(moonshine_video_query_caps(nullptr) != 0);
}

void TestExportSlotReturnLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_slot_return lifecycle and robustness..." << std::endl;

    // 1. Null handle robustness
    TEST_ASSERT(moonshine_slot_return_enqueue(nullptr, 42) == 0);
    int32_t out_slot = -999;
    TEST_ASSERT(moonshine_slot_return_dequeue(nullptr, &out_slot) == 0);
    TEST_ASSERT(moonshine_slot_return_size(nullptr) == 0);
    moonshine_slot_return_destroy(nullptr); // Safe no-op

    // 2. Normal lifecycle
    MoonshineRingBufferHandle handle = moonshine_slot_return_create(4);
    TEST_ASSERT(handle != nullptr);
    TEST_ASSERT(moonshine_slot_return_size(handle) == 0);

    // Empty dequeue
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 0);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, nullptr) == 0);

    // Enqueue items
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 101) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 102) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 103) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 104) == 1);
    TEST_ASSERT(moonshine_slot_return_size(handle) == 4);

    // Full queue rejection
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 105) == 0);

    // Dequeue in FIFO order
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 101);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 102);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 103);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 104);

    TEST_ASSERT(moonshine_slot_return_size(handle) == 0);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 0);

    moonshine_slot_return_destroy(handle);
}

int main()
{
    std::cout << "=== Running C-ABI Export Test Suite ===" << std::endl;
    TestExportVectorXor();
    TestExportSpscLifecycle();
    TestExportSlotReturnLifecycle();
    TestExportJitterLifecycle();
    TestExportVideoCaps();
    std::cout << "All C-ABI Export tests passed successfully." << std::endl;
    return 0;
}
