#include "encoder/nvenc/nvenc_surface_pool.hpp"
#include <algorithm>
#include <mutex>

namespace moonshine::encoder::nvenc {

NvencSurfacePool::NvencSurfacePool() = default;

NvencSurfacePool::~NvencSurfacePool() = default;

NvencSurfacePool::NvencSurfacePool(NvencSurfacePool&& other) noexcept {
    std::lock_guard<std::mutex> lock(other._mutex);
    _entries = std::move(other._entries);
}

NvencSurfacePool& NvencSurfacePool::operator=(NvencSurfacePool&& other) noexcept {
    if (this != &other) {
        std::scoped_lock lock(_mutex, other._mutex);
        _entries = std::move(other._entries);
    }
    return *this;
}

void* NvencSurfacePool::get_or_register_surface(
    void* session,
    const NVENC_FN_LIST& fn,
    void* d3d_texture,
    uint32_t width,
    uint32_t height,
    uint32_t buffer_format
) {
    if (!session || !d3d_texture || !fn.nvEncRegisterResource) {
        return nullptr;
    }

    std::lock_guard<std::mutex> lock(_mutex);

    // Check if texture is already registered with matching parameters
    auto it = std::find_if(_entries.begin(), _entries.end(), [d3d_texture](const SurfaceRegistrationEntry& e) {
        return e.d3d_texture == d3d_texture;
    });

    if (it != _entries.end()) {
        if (it->width == width && it->height == height && it->buffer_format == buffer_format) {
            return it->registered_resource;
        }

        // Parameters changed: unregister old resource
        if (fn.nvEncUnregisterResource && it->registered_resource) {
            auto pfn_unreg = reinterpret_cast<PNVENCUNREGISTERRESOURCE>(fn.nvEncUnregisterResource);
            if (pfn_unreg) {
                pfn_unreg(session, it->registered_resource);
            }
        }
        _entries.erase(it);
    }

    // Register new resource
    NV_ENC_REGISTER_RESOURCE reg_params{};
    reg_params.version = NV_ENC_REGISTER_RESOURCE_VER;
    reg_params.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
    reg_params.width = width;
    reg_params.height = height;
    reg_params.pitch = 0;
    reg_params.resourceToRegister = d3d_texture;
    reg_params.bufferUsage = NV_ENC_INPUT_IMAGE;
    reg_params.bufferFormat = buffer_format;

    auto pfn_register = reinterpret_cast<PNVENCREGISTERRESOURCE>(fn.nvEncRegisterResource);
    if (!pfn_register || pfn_register(session, &reg_params) != NV_ENC_SUCCESS || !reg_params.registeredResource) {
        return nullptr;
    }

    _entries.push_back(SurfaceRegistrationEntry{
        .d3d_texture = d3d_texture,
        .registered_resource = reg_params.registeredResource,
        .width = width,
        .height = height,
        .buffer_format = buffer_format
    });

    return reg_params.registeredResource;
}

void NvencSurfacePool::clear(void* session, const NVENC_FN_LIST& fn) {
    std::lock_guard<std::mutex> lock(_mutex);
    if (session && fn.nvEncUnregisterResource) {
        auto pfn_unreg = reinterpret_cast<PNVENCUNREGISTERRESOURCE>(fn.nvEncUnregisterResource);
        if (pfn_unreg) {
            for (const auto& entry : _entries) {
                if (entry.registered_resource) {
                    pfn_unreg(session, entry.registered_resource);
                }
            }
        }
    }
    _entries.clear();
}

size_t NvencSurfacePool::size() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _entries.size();
}

bool NvencSurfacePool::empty() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _entries.empty();
}

} // namespace moonshine::encoder::nvenc
