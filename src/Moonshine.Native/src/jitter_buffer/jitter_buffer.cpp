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
    if (packet.total_packets == 0 || packet.packet_index >= packet.total_packets || packet.packet_index >= MAX_PACKETS_PER_FRAME) {
        return -1;
    }

    // Ignore frames far in the past using modular signed arithmetic to handle 2^32-1 -> 0 rollover
    if (has_popped_frame_ && static_cast<int32_t>(packet.frame_index - last_popped_frame_index_) <= 0) {
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

    // 1. Single-packet frame fast path (N = 1)
    if (slot.total_packets == 1) {
        if (packet.packet_index != 0 || packet.payload_size > MAX_FRAME_PAYLOAD_BYTES) {
            return -1;
        }
        std::memcpy(slot.payload_buffer.get(), packet.payload_ptr, packet.payload_size);
        slot.total_bytes = packet.payload_size;
        slot.received_packets = 1;
        slot.packet_received[0] = true;
        slot.is_complete = true;
        return 1; // Complete
    }

    // 2. Multi-packet frame (N > 1)
    // Duplicate packet check: safely ignore already received slices
    if (slot.packet_received[packet.packet_index]) {
        return 0;
    }

    bool is_tail = (packet.packet_index == slot.total_packets - 1);

    if (is_tail) {
        if (packet.payload_size > MAX_PACKET_PAYLOAD_BYTES) {
            return -1; // Tail payload exceeds maximum allowed packet size
        }
        slot.tail_payload_size = packet.payload_size;

        if (slot.nominal_payload_size > 0) {
            // Nominal size is already established; check total frame capacity and write directly
            size_t total_expected = (static_cast<size_t>(slot.total_packets) - 1u) * slot.nominal_payload_size + slot.tail_payload_size;
            if (total_expected > MAX_FRAME_PAYLOAD_BYTES) {
                return -1; // Frame exceeds maximum frame arena size
            }
            size_t offset = static_cast<size_t>(packet.packet_index) * slot.nominal_payload_size;
            std::memcpy(slot.payload_buffer.get() + offset, packet.payload_ptr, packet.payload_size);
        } else {
            // Nominal size not yet known; buffer tail payload until non-tail arrives
            std::memcpy(slot.pending_tail_buffer, packet.payload_ptr, packet.payload_size);
            slot.has_pending_tail = true;
        }
    } else {
        // Non-tail packet: validate uniformity across slices 0 .. N-2
        if (slot.nominal_payload_size == 0) {
            slot.nominal_payload_size = packet.payload_size;

            // Validate total frame bounds with established nominal size
            size_t max_expected = (static_cast<size_t>(slot.total_packets) - 1u) * slot.nominal_payload_size +
                                  (slot.has_pending_tail ? slot.tail_payload_size : slot.nominal_payload_size);
            if (max_expected > MAX_FRAME_PAYLOAD_BYTES) {
                return -1; // Frame exceeds maximum frame arena size
            }

            // Flush pending tail if it was waiting for nominal size
            if (slot.has_pending_tail) {
                size_t tail_index = slot.total_packets - 1;
                size_t tail_offset = tail_index * slot.nominal_payload_size;
                std::memcpy(slot.payload_buffer.get() + tail_offset, slot.pending_tail_buffer, slot.tail_payload_size);
                slot.has_pending_tail = false;
            }
        } else if (packet.payload_size != slot.nominal_payload_size) {
            return -1; // Inconsistent payload size across non-tail packets
        }

        size_t offset = static_cast<size_t>(packet.packet_index) * slot.nominal_payload_size;
        if (offset + packet.payload_size > MAX_FRAME_PAYLOAD_BYTES) {
            return -1;
        }
        std::memcpy(slot.payload_buffer.get() + offset, packet.payload_ptr, packet.payload_size);
    }

    slot.packet_received[packet.packet_index] = true;
    slot.received_packets++;

    if (slot.received_packets == slot.total_packets) {
        slot.total_bytes = static_cast<uint32_t>(slot.total_packets - 1) * slot.nominal_payload_size + slot.tail_payload_size;
        slot.is_complete = true;
        return 1; // 1 = Frame is complete
    }

    return 0; // Incomplete, still buffering
}

int JitterBuffer::PopFrame(MoonshineFrameDesc& out_frame) noexcept {
    int best_slot_idx = -1;
    for (size_t i = 0; i < max_frames_; ++i) {
        const FrameSlot& slot = slots_[i];
        if (slot.is_occupied && slot.is_complete) {
            if (best_slot_idx == -1 ||
                static_cast<int32_t>(slot.frame_index - slots_[best_slot_idx].frame_index) < 0) {
                best_slot_idx = static_cast<int>(i);
            }
        }
    }

    if (best_slot_idx >= 0) {
        FrameSlot& slot = slots_[best_slot_idx];
        out_frame.frame_index = slot.frame_index;
        out_frame.total_bytes = slot.total_bytes;
        out_frame.packet_count = slot.received_packets;
        out_frame.is_keyframe = slot.is_keyframe ? 1 : 0;
        out_frame.frame_buffer = slot.payload_buffer.get();

        last_popped_frame_index_ = slot.frame_index;
        has_popped_frame_ = true;
        slot.is_complete = false;
        slot.is_occupied = false;
        return 1; // 1 frame popped
    }

    return 0; // No complete frame ready
}

} // namespace moonshine::jitter
