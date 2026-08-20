#include "moonshine/jitter_buffer/jitter_buffer.hpp"
#include <cstring>
#include <algorithm>

namespace moonshine::jitter {

JitterBuffer::JitterBuffer(size_t max_frames)
    : max_frames_(max_frames < 4 ? 4 : max_frames),
      slots_(max_frames_) {
}

int JitterBuffer::PushPacket(const MoonshinePacketDesc& packet) noexcept {
    if (!packet.payload_ptr || packet.payload_size == 0) {
        return -1;
    }

    // Ignore frames far in the past
    if (last_popped_frame_index_ > 0 && packet.frame_index <= last_popped_frame_index_) {
        return 0; // Stale dropped packet
    }

    size_t slot_idx = packet.frame_index % max_frames_;
    FrameSlot& slot = slots_[slot_idx];

    // If slot contains older frame, reset it
    if (slot.is_occupied && slot.frame_index != packet.frame_index) {
        slot.Reset();
    }

    if (!slot.is_occupied) {
        slot.Reset();
        slot.frame_index = packet.frame_index;
        slot.total_packets = packet.total_packets;
        slot.is_keyframe = (packet.flags & 0x04) != 0;
        slot.is_occupied = true;
    }

    if (packet.packet_index < MAX_PACKETS_PER_FRAME && !slot.packet_received[packet.packet_index]) {
        slot.packet_received[packet.packet_index] = true;
        slot.received_packets++;

        // Calculate offset into frame buffer
        size_t offset = static_cast<size_t>(packet.packet_index) * packet.payload_size;
        if (offset + packet.payload_size <= MAX_FRAME_PAYLOAD_BYTES) {
            std::memcpy(slot.payload_buffer.get() + offset, packet.payload_ptr, packet.payload_size);
            slot.total_bytes += packet.payload_size;
        }

        if (slot.received_packets >= slot.total_packets && slot.total_packets > 0) {
            slot.is_complete = true;
            return 1; // 1 = Frame is complete
        }
    }

    return 0; // Incomplete, still buffering
}

int JitterBuffer::PopFrame(MoonshineFrameDesc& out_frame) noexcept {
    for (size_t i = 0; i < max_frames_; ++i) {
        FrameSlot& slot = slots_[i];
        if (slot.is_occupied && slot.is_complete) {
            out_frame.frame_index = slot.frame_index;
            out_frame.total_bytes = slot.total_bytes;
            out_frame.packet_count = slot.received_packets;
            out_frame.is_keyframe = slot.is_keyframe ? 1 : 0;
            out_frame.frame_buffer = slot.payload_buffer.get();

            last_popped_frame_index_ = slot.frame_index;
            slot.is_complete = false;
            slot.is_occupied = false;
            return 1; // 1 frame popped
        }
    }
    return 0; // No complete frame ready
}

} // namespace moonshine::jitter
