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
    std::vector<uint8_t> payload(1400);
    for (size_t i = 0; i < payload.size(); ++i) {
        payload[i] = static_cast<uint8_t>((i * 7 + 13) & 0xFF);
    }

    MoonshinePacketDesc packet{};
    packet.sequence_number = 1;
    packet.frame_index = 100;
    packet.packet_index = 0;
    packet.total_packets = 1;
    packet.payload_size = static_cast<uint16_t>(payload.size());
    packet.flags = 0x07; // Frame Start | Frame End | Keyframe
    packet.payload_ptr = payload.data();

    int push_res = jb.PushPacket(packet);
    TEST_ASSERT(push_res == 1); // 1 = Frame is complete

    MoonshineFrameDesc completed{};
    int pop_res = jb.PopFrame(completed);
    TEST_ASSERT(pop_res == 1);
    TEST_ASSERT(completed.frame_index == 100);
    TEST_ASSERT(completed.packet_count == 1);
    TEST_ASSERT(completed.is_keyframe == 1);
    TEST_ASSERT(completed.total_bytes == payload.size());
    TEST_ASSERT(std::memcmp(completed.frame_buffer, payload.data(), payload.size()) == 0);
}

void TestMultiSliceVariableTailArrivalOrders()
{
    std::cout << "[Test] JitterBuffer variable trailing slice arrival permutations..." << std::endl;
    constexpr uint16_t kSlices = 5;
    constexpr uint16_t kNominalSize = 1400;
    constexpr uint16_t kTailSize = 350;
    constexpr size_t kTotalBytes = (kSlices - 1) * kNominalSize + kTailSize;

    // Create ground-truth contiguous payload
    std::vector<uint8_t> ground_truth(kTotalBytes);
    for (size_t i = 0; i < kTotalBytes; ++i) {
        ground_truth[i] = static_cast<uint8_t>((i * 31 + 47) & 0xFF);
    }

    std::vector<std::vector<uint8_t>> slice_buffers(kSlices);
    std::vector<MoonshinePacketDesc> packets(kSlices);

    for (uint16_t i = 0; i < kSlices; ++i) {
        uint16_t size = (i == kSlices - 1) ? kTailSize : kNominalSize;
        slice_buffers[i].resize(size);
        size_t offset = i * kNominalSize;
        std::memcpy(slice_buffers[i].data(), ground_truth.data() + offset, size);

        packets[i].sequence_number = 100 + i;
        packets[i].frame_index = 200;
        packets[i].packet_index = i;
        packets[i].total_packets = kSlices;
        packets[i].payload_size = size;
        packets[i].payload_ptr = slice_buffers[i].data();
        packets[i].flags = (i == 0 ? 0x01 : 0) | (i == kSlices - 1 ? 0x02 : 0);
    }

    const std::vector<std::vector<int>> order_permutations = {
        {0, 1, 2, 3, 4}, // Normal in-order
        {4, 3, 2, 1, 0}, // Exact reverse (tail arrives first)
        {4, 1, 0, 3, 2}, // Interleaved with tail first
        {4, 0, 1, 2, 3}, // Tail first then in-order
        {2, 0, 4, 1, 3}  // Mixed random order
    };

    uint32_t frame_idx = 300;
    for (const auto& order : order_permutations) {
        JitterBuffer jb(16);
        frame_idx++;

        for (size_t step = 0; step < order.size(); ++step) {
            int slice_idx = order[step];
            MoonshinePacketDesc p = packets[slice_idx];
            p.frame_index = frame_idx;

            int res = jb.PushPacket(p);
            if (step == order.size() - 1) {
                TEST_ASSERT(res == 1); // Last arrival must complete the frame
            } else {
                TEST_ASSERT(res == 0); // Intermediate arrivals buffer
            }
        }

        MoonshineFrameDesc completed{};
        TEST_ASSERT(jb.PopFrame(completed) == 1);
        TEST_ASSERT(completed.frame_index == frame_idx);
        TEST_ASSERT(completed.packet_count == kSlices);
        TEST_ASSERT(completed.total_bytes == kTotalBytes);
        TEST_ASSERT(std::memcmp(completed.frame_buffer, ground_truth.data(), kTotalBytes) == 0);
    }
}

void TestVariableTailLengths()
{
    std::cout << "[Test] JitterBuffer 1-byte and 1399-byte trailing slice boundaries..." << std::endl;
    const uint16_t tail_sizes[] = {1, 1399};

    for (uint16_t tail_size : tail_sizes) {
        constexpr uint16_t kSlices = 4;
        constexpr uint16_t kNominalSize = 1400;
        size_t total_bytes = (kSlices - 1) * kNominalSize + tail_size;

        std::vector<uint8_t> ground_truth(total_bytes);
        for (size_t i = 0; i < total_bytes; ++i) {
            ground_truth[i] = static_cast<uint8_t>((i * 17 + tail_size) & 0xFF);
        }

        std::vector<std::vector<uint8_t>> slice_buffers(kSlices);
        std::vector<MoonshinePacketDesc> packets(kSlices);

        for (uint16_t i = 0; i < kSlices; ++i) {
            uint16_t size = (i == kSlices - 1) ? tail_size : kNominalSize;
            slice_buffers[i].resize(size);
            size_t offset = i * kNominalSize;
            std::memcpy(slice_buffers[i].data(), ground_truth.data() + offset, size);

            packets[i].sequence_number = 200 + i;
            packets[i].frame_index = 500 + tail_size;
            packets[i].packet_index = i;
            packets[i].total_packets = kSlices;
            packets[i].payload_size = size;
            packets[i].payload_ptr = slice_buffers[i].data();
            packets[i].flags = (i == 0 ? 0x01 : 0) | (i == kSlices - 1 ? 0x02 : 0);
        }

        // Feed in interleaved order with tail first: [3, 1, 0, 2]
        JitterBuffer jb(16);
        int order[] = {3, 1, 0, 2};
        for (int step = 0; step < 4; ++step) {
            int slice_idx = order[step];
            int res = jb.PushPacket(packets[slice_idx]);
            if (step == 3) {
                TEST_ASSERT(res == 1);
            } else {
                TEST_ASSERT(res == 0);
            }
        }

        MoonshineFrameDesc completed{};
        TEST_ASSERT(jb.PopFrame(completed) == 1);
        TEST_ASSERT(completed.total_bytes == total_bytes);
        TEST_ASSERT(std::memcmp(completed.frame_buffer, ground_truth.data(), total_bytes) == 0);
    }
}

void TestDuplicatePacketHandling()
{
    std::cout << "[Test] JitterBuffer duplicate packet arrival..." << std::endl;
    JitterBuffer jb(16);
    constexpr uint16_t kSlices = 3;
    constexpr uint16_t kNominalSize = 1400;
    constexpr uint16_t kTailSize = 500;
    size_t total_bytes = 2 * kNominalSize + kTailSize;

    std::vector<uint8_t> ground_truth(total_bytes, 0x55);
    std::vector<MoonshinePacketDesc> packets(kSlices);
    std::vector<std::vector<uint8_t>> slices(kSlices);

    for (uint16_t i = 0; i < kSlices; ++i) {
        uint16_t size = (i == kSlices - 1) ? kTailSize : kNominalSize;
        slices[i].resize(size, static_cast<uint8_t>(i + 1));
        packets[i].frame_index = 600;
        packets[i].packet_index = i;
        packets[i].total_packets = kSlices;
        packets[i].payload_size = size;
        packets[i].payload_ptr = slices[i].data();
    }

    // Push slice 0
    TEST_ASSERT(jb.PushPacket(packets[0]) == 0);
    // Push duplicate slice 0 (must be safely ignored, returning 0 without double count)
    TEST_ASSERT(jb.PushPacket(packets[0]) == 0);

    // Push slice 2 (tail)
    TEST_ASSERT(jb.PushPacket(packets[2]) == 0);
    // Push duplicate slice 2
    TEST_ASSERT(jb.PushPacket(packets[2]) == 0);

    // Push slice 1 (completes frame)
    TEST_ASSERT(jb.PushPacket(packets[1]) == 1);

    MoonshineFrameDesc completed{};
    TEST_ASSERT(jb.PopFrame(completed) == 1);
    TEST_ASSERT(completed.total_bytes == total_bytes);
    TEST_ASSERT(completed.packet_count == 3);
}

void TestNegativeAndBoundaryValidation()
{
    std::cout << "[Test] JitterBuffer negative and boundary validation..." << std::endl;
    JitterBuffer jb(16);
    uint8_t sample_payload[1400] = {0};

    MoonshinePacketDesc p{};
    p.frame_index = 700;
    p.total_packets = 4;
    p.payload_ptr = sample_payload;

    // 1. Null pointer or 0 payload size
    p.packet_index = 0;
    p.payload_size = 0;
    TEST_ASSERT(jb.PushPacket(p) == -1);

    p.payload_size = 1400;
    p.payload_ptr = nullptr;
    TEST_ASSERT(jb.PushPacket(p) == -1);
    p.payload_ptr = sample_payload;

    // 2. Total packets = 0 or packet_index >= total_packets
    p.total_packets = 0;
    TEST_ASSERT(jb.PushPacket(p) == -1);

    p.total_packets = 4;
    p.packet_index = 4; // Out of bounds
    TEST_ASSERT(jb.PushPacket(p) == -1);

    // 3. Inconsistent non-tail packet payload sizes
    JitterBuffer jb_inconsistent(16);
    p.frame_index = 701;
    p.total_packets = 3;
    p.packet_index = 0;
    p.payload_size = 1400;
    TEST_ASSERT(jb_inconsistent.PushPacket(p) == 0);

    p.packet_index = 1;
    p.payload_size = 1350; // Inconsistent with nominal 1400
    TEST_ASSERT(jb_inconsistent.PushPacket(p) == -1);

    // 4. Oversized tail payload (> 2048)
    JitterBuffer jb_oversized(16);
    p.frame_index = 702;
    p.total_packets = 3;
    p.packet_index = 2; // Tail packet
    p.payload_size = 2500; // Exceeds MAX_PACKET_PAYLOAD_BYTES (2048)
    TEST_ASSERT(jb_oversized.PushPacket(p) == -1);
}

int main()
{
    std::cout << "=== Running Jitter Buffer Test Suite ===" << std::endl;
    TestSingleSliceFrame();
    TestMultiSliceVariableTailArrivalOrders();
    TestVariableTailLengths();
    TestDuplicatePacketHandling();
    TestNegativeAndBoundaryValidation();
    std::cout << "All Jitter Buffer tests passed successfully." << std::endl;
    return 0;
}
