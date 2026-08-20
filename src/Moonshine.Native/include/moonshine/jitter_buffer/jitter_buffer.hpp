#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include <cstdint>
#include <cstddef>
#include <vector>
#include <atomic>
#include <memory>

namespace moonshine::jitter {

constexpr size_t MAX_PACKETS_PER_FRAME = 512;
constexpr size_t MAX_FRAME_PAYLOAD_BYTES = 2 * 1024 * 1024; // 2MB frame arena

struct alignas(64) FrameSlot {
    uint32_t frame_index{0};
    uint16_t total_packets{0};
    uint16_t received_packets{0};
    uint32_t total_bytes{0};
    bool is_keyframe{false};
    bool is_complete{false};
    bool is_occupied{false};
    std::unique_ptr<uint8_t[]> payload_buffer;
    bool packet_received[MAX_PACKETS_PER_FRAME];

    FrameSlot() : payload_buffer(std::make_unique<uint8_t[]>(MAX_FRAME_PAYLOAD_BYTES)) {
        Reset();
    }

    void Reset() noexcept {
        frame_index = 0;
        total_packets = 0;
        received_packets = 0;
        total_bytes = 0;
        is_keyframe = false;
        is_complete = false;
        is_occupied = false;
        for (size_t i = 0; i < MAX_PACKETS_PER_FRAME; ++i) {
            packet_received[i] = false;
        }
    }
};

/**
 * @brief Predictive sub-millisecond Jitter Buffer and Frame Assembly Pipeline.
 */
class JitterBuffer {
public:
    explicit JitterBuffer(size_t max_frames = 16);
    ~JitterBuffer() = default;

    int PushPacket(const MoonshinePacketDesc& packet) noexcept;
    int PopFrame(MoonshineFrameDesc& out_frame) noexcept;

private:
    const size_t max_frames_;
    std::vector<FrameSlot> slots_;
    uint32_t last_popped_frame_index_{0};
};

} // namespace moonshine::jitter
