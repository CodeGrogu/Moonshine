#include "encoder/nvenc/nvenc_bitstream_pool.hpp"
#include <algorithm>
#include <mutex>

namespace moonshine::encoder::nvenc {

NvencBitstreamPool::NvencBitstreamPool() = default;

NvencBitstreamPool::~NvencBitstreamPool() = default;

NvencBitstreamPool::NvencBitstreamPool(NvencBitstreamPool&& other) noexcept {
    std::lock_guard<std::mutex> lock(other._mutex);
    _free_buffers = std::move(other._free_buffers);
    _allocated_buffers = std::move(other._allocated_buffers);
}

NvencBitstreamPool& NvencBitstreamPool::operator=(NvencBitstreamPool&& other) noexcept {
    if (this != &other) {
        std::scoped_lock lock(_mutex, other._mutex);
        _free_buffers = std::move(other._free_buffers);
        _allocated_buffers = std::move(other._allocated_buffers);
    }
    return *this;
}

void* NvencBitstreamPool::acquire_buffer(void* session, const NVENC_FN_LIST& fn, uint32_t size) {
    if (!session || !fn.nvEncCreateBitstreamBuffer) {
        return nullptr;
    }

    std::lock_guard<std::mutex> lock(_mutex);

    if (!_free_buffers.empty()) {
        void* buf = _free_buffers.back();
        _free_buffers.pop_back();
        return buf;
    }

    NV_ENC_CREATE_BITSTREAM_BUFFER create_params{};
    create_params.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    create_params.size = size;
    create_params.memoryHeap = 0;

    auto pfn_create = reinterpret_cast<PNVENCCREATEBITSTREAMBUFFER>(fn.nvEncCreateBitstreamBuffer);
    if (!pfn_create || pfn_create(session, &create_params) != NV_ENC_SUCCESS || !create_params.bitstreamBuffer) {
        return nullptr;
    }

    _allocated_buffers.push_back(create_params.bitstreamBuffer);
    return create_params.bitstreamBuffer;
}

void NvencBitstreamPool::release_buffer(void* buffer) {
    if (!buffer) return;

    std::lock_guard<std::mutex> lock(_mutex);
    auto it = std::find(_allocated_buffers.begin(), _allocated_buffers.end(), buffer);
    if (it != _allocated_buffers.end()) {
        auto free_it = std::find(_free_buffers.begin(), _free_buffers.end(), buffer);
        if (free_it == _free_buffers.end()) {
            _free_buffers.push_back(buffer);
        }
    }
}

void NvencBitstreamPool::clear(void* session, const NVENC_FN_LIST& fn) {
    std::lock_guard<std::mutex> lock(_mutex);
    if (session && fn.nvEncDestroyBitstreamBuffer) {
        auto pfn_destroy = reinterpret_cast<PNVENCDESTROYBITSTREAMBUFFER>(fn.nvEncDestroyBitstreamBuffer);
        if (pfn_destroy) {
            for (void* buf : _allocated_buffers) {
                if (buf) {
                    pfn_destroy(session, buf);
                }
            }
        }
    }
    _free_buffers.clear();
    _allocated_buffers.clear();
}

size_t NvencBitstreamPool::total_count() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _allocated_buffers.size();
}

size_t NvencBitstreamPool::free_count() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _free_buffers.size();
}

bool NvencBitstreamPool::empty() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _allocated_buffers.empty();
}

} // namespace moonshine::encoder::nvenc
