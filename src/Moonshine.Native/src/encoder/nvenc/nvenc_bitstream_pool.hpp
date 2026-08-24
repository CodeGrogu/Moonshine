#pragma once

#include "encoder/nvenc/nvenc_types.hpp"
#include <cstdint>
#include <cstddef>
#include <mutex>
#include <vector>

namespace moonshine::encoder::nvenc {

class NvencBitstreamPool {
public:
    NvencBitstreamPool();
    ~NvencBitstreamPool();

    NvencBitstreamPool(const NvencBitstreamPool&) = delete;
    NvencBitstreamPool& operator=(const NvencBitstreamPool&) = delete;

    NvencBitstreamPool(NvencBitstreamPool&& other) noexcept;
    NvencBitstreamPool& operator=(NvencBitstreamPool&& other) noexcept;

    void* acquire_buffer(void* session, const NVENC_FN_LIST& fn, uint32_t size = 4 * 1024 * 1024);
    void release_buffer(void* buffer);
    void clear(void* session, const NVENC_FN_LIST& fn);

    [[nodiscard]] size_t total_count() const noexcept;
    [[nodiscard]] size_t free_count() const noexcept;
    [[nodiscard]] bool empty() const noexcept;

private:
    mutable std::mutex _mutex;
    std::vector<void*> _free_buffers;
    std::vector<void*> _allocated_buffers;
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencBitstreamPool;
} // namespace moonshine::encoder
