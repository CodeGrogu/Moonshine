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

void test_jitter_assembly_abi() {
    std::cout << "[Test] moonshine_jitter_* Multi-Packet Frame Assembly ... ";
    MoonshineJitterBufferHandle jitter = moonshine_jitter_create(8);
    MOONSHINE_TEST_ASSERT(jitter != nullptr);

    uint8_t payload1[] = "Hello ";
    uint8_t payload2[] = "Moonshine ";
    uint8_t payload3[] = "Streaming!";

    MoonshinePacketDesc p1{};
    p1.sequence_number = 1;
    p1.frame_index = 100;
    p1.packet_index = 0;
    p1.total_packets = 3;
    p1.payload_size = sizeof(payload1);
    p1.payload_ptr = payload1;

    MoonshinePacketDesc p2{};
    p2.sequence_number = 2;
    p2.frame_index = 100;
    p2.packet_index = 1;
    p2.total_packets = 3;
    p2.payload_size = sizeof(payload2);
    p2.payload_ptr = payload2;

    MoonshinePacketDesc p3{};
    p3.sequence_number = 3;
    p3.frame_index = 100;
    p3.packet_index = 2;
    p3.total_packets = 3;
    p3.payload_size = sizeof(payload3);
    p3.payload_ptr = payload3;

    // Push packets out of order (p3, p1, p2)
    MOONSHINE_TEST_ASSERT(moonshine_jitter_push_packet(jitter, &p3) == 0);
    MOONSHINE_TEST_ASSERT(moonshine_jitter_push_packet(jitter, &p1) == 0);

    MoonshineFrameDesc frame{};
    MOONSHINE_TEST_ASSERT(moonshine_jitter_pop_frame(jitter, &frame) == 0); // Frame not complete yet

    MOONSHINE_TEST_ASSERT(moonshine_jitter_push_packet(jitter, &p2) == 0);
    MOONSHINE_TEST_ASSERT(moonshine_jitter_pop_frame(jitter, &frame) == 1); // Frame now complete!

    MOONSHINE_TEST_ASSERT(frame.frame_index == 100);
    MOONSHINE_TEST_ASSERT(frame.packet_count == 3);

    moonshine_jitter_destroy(jitter);
    std::cout << "PASSED\n";
}

int main() {
    std::cout << "========================================\n";
    std::cout << "Moonshine Native Jitter Buffer Test Suite\n";
    std::cout << "========================================\n";
    test_jitter_assembly_abi();
    std::cout << "All Jitter Buffer tests PASSED!\n";
    return 0;
}
