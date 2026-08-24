#pragma once

#include "encoder/nvenc/nvenc_types.hpp"
#include <cstdint>
#include <mutex>
#include <vector>

namespace moonshine::encoder::nvenc {

struct SurfaceRegistrationEntry {
    void* d3d_texture{nullptr};
    void* registered_resource{nullptr};
    uint32_t width{0};
    uint32_t height{0};
    uint32_t buffer_format{0};
};

class NvencSurfacePool {
public:
    NvencSurfacePool();
    ~NvencSurfacePool();

    NvencSurfacePool(const NvencSurfacePool&) = delete;
    NvencSurfacePool& operator=(const NvencSurfacePool&) = delete;

    NvencSurfacePool(NvencSurfacePool&& other) noexcept;
    NvencSurfacePool& operator=(NvencSurfacePool&& other) noexcept;

    void* get_or_register_surface(
        void* session,
        const NVENC_FN_LIST& fn,
        void* d3d_texture,
        uint32_t width,
        uint32_t height,
        uint32_t buffer_format
    );

    void clear(void* session, const NVENC_FN_LIST& fn);

    [[nodiscard]] size_t size() const noexcept;
    [[nodiscard]] bool empty() const noexcept;

private:
    mutable std::mutex _mutex;
    std::vector<SurfaceRegistrationEntry> _entries;
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencSurfacePool;
} // namespace moonshine::encoder
