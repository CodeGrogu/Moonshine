#pragma once

#include <cstdint>
#include <cstddef>
#include <atomic>
#include <vector>
#include <memory>
#include <new>

namespace moonshine::ring_buffer {

/**
 * @brief Ultra-low latency Lock-Free Single-Producer Single-Consumer (SPSC) Ring Buffer.
 * 
 * Features 64-byte cacheline alignment on head and tail atomic counters to completely
 * prevent false sharing between reader and writer threads.
 */
template <typename T>
class alignas(64) SpscRingBuffer {
public:
    explicit SpscRingBuffer(size_t capacity)
        : capacity_(RoundUpPowerOf2(capacity < 4 ? 4 : capacity)),
          mask_(capacity_ - 1),
          buffer_(std::make_unique<T[]>(capacity_)) {
    }

    ~SpscRingBuffer() = default;

    // Non-copyable and non-movable
    SpscRingBuffer(const SpscRingBuffer&) = delete;
    SpscRingBuffer& operator=(const SpscRingBuffer&) = delete;

    /**
     * @brief Enqueues an item into the ring buffer (Producer thread only).
     * @return true if enqueued, false if buffer is full.
     */
    bool TryEnqueue(const T& item) noexcept {
        const size_t current_tail = tail_.load(std::memory_order_relaxed);
        const size_t current_head = head_.load(std::memory_order_acquire);

        if ((current_tail - current_head) >= capacity_) {
            return false; // Queue full
        }

        buffer_[current_tail & mask_] = item;
        tail_.store(current_tail + 1, std::memory_order_release);
        return true;
    }

    /**
     * @brief Dequeues an item from the ring buffer (Consumer thread only).
     * @return true if dequeued, false if buffer is empty.
     */
    bool TryDequeue(T& item) noexcept {
        const size_t current_head = head_.load(std::memory_order_relaxed);
        const size_t current_tail = tail_.load(std::memory_order_acquire);

        if (current_head == current_tail) {
            return false; // Queue empty
        }

        item = buffer_[current_head & mask_];
        head_.store(current_head + 1, std::memory_order_release);
        return true;
    }

    /**
     * @brief Returns approximate number of items in the buffer.
     */
    size_t Size() const noexcept {
        const size_t current_head = head_.load(std::memory_order_relaxed);
        const size_t current_tail = tail_.load(std::memory_order_relaxed);
        return current_tail >= current_head ? (current_tail - current_head) : 0;
    }

    /**
     * @brief Returns total capacity.
     */
    size_t Capacity() const noexcept {
        return capacity_;
    }

private:
    static size_t RoundUpPowerOf2(size_t v) noexcept {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v |= v >> 32;
        return v + 1;
    }

    const size_t capacity_;
    const size_t mask_;
    const std::unique_ptr<T[]> buffer_;

    // Cacheline-isolated indices
    alignas(64) std::atomic<size_t> tail_{0};
    alignas(64) std::atomic<size_t> head_{0};
};

} // namespace moonshine::ring_buffer
