#pragma once

#include "encoder/nvenc/nvenc_types.hpp"
#include <cstdint>
#include <utility>

namespace moonshine::encoder::nvenc {

class NvencMappedResourceGuard {
public:
    NvencMappedResourceGuard(void* session, const NVENC_FN_LIST* fn, void* registered_resource)
        : _session(session), _fn(fn) {
        if (_session && _fn && registered_resource && _fn->nvEncMapInputResource) {
            NV_ENC_MAP_INPUT_RESOURCE map_params{};
            map_params.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
            map_params.registeredResource = registered_resource;

            auto pfn_map = reinterpret_cast<PNVENCMAPINPUTRESOURCE>(_fn->nvEncMapInputResource);
            if (pfn_map && pfn_map(_session, &map_params) == NV_ENC_SUCCESS) {
                _mapped_resource = map_params.mappedResource;
                _mapped_buffer_fmt = map_params.mappedBufferFmt;
            }
        }
    }

    ~NvencMappedResourceGuard() {
        reset();
    }

    NvencMappedResourceGuard(const NvencMappedResourceGuard&) = delete;
    NvencMappedResourceGuard& operator=(const NvencMappedResourceGuard&) = delete;

    NvencMappedResourceGuard(NvencMappedResourceGuard&& other) noexcept
        : _session(other._session),
          _fn(other._fn),
          _mapped_resource(other._mapped_resource),
          _mapped_buffer_fmt(other._mapped_buffer_fmt) {
        other._session = nullptr;
        other._fn = nullptr;
        other._mapped_resource = nullptr;
        other._mapped_buffer_fmt = 0;
    }

    NvencMappedResourceGuard& operator=(NvencMappedResourceGuard&& other) noexcept {
        if (this != &other) {
            reset();
            _session = other._session;
            _fn = other._fn;
            _mapped_resource = other._mapped_resource;
            _mapped_buffer_fmt = other._mapped_buffer_fmt;
            other._session = nullptr;
            other._fn = nullptr;
            other._mapped_resource = nullptr;
            other._mapped_buffer_fmt = 0;
        }
        return *this;
    }

    void reset() noexcept {
        if (_session && _fn && _mapped_resource && _fn->nvEncUnmapInputResource) {
            auto pfn_unmap = reinterpret_cast<PNVENCUNMAPINPUTRESOURCE>(_fn->nvEncUnmapInputResource);
            if (pfn_unmap) {
                pfn_unmap(_session, _mapped_resource);
            }
        }
        _mapped_resource = nullptr;
        _mapped_buffer_fmt = 0;
    }

    [[nodiscard]] void* mapped_resource() const noexcept { return _mapped_resource; }
    [[nodiscard]] uint32_t mapped_buffer_format() const noexcept { return _mapped_buffer_fmt; }
    [[nodiscard]] bool is_valid() const noexcept { return _mapped_resource != nullptr; }
    explicit operator bool() const noexcept { return is_valid(); }

private:
    void* _session{nullptr};
    const NVENC_FN_LIST* _fn{nullptr};
    void* _mapped_resource{nullptr};
    uint32_t _mapped_buffer_fmt{0};
};

class NvencLockedBitstreamGuard {
public:
    NvencLockedBitstreamGuard(void* session, const NVENC_FN_LIST* fn, void* bitstream_buffer)
        : _session(session), _fn(fn), _bitstream_buffer(bitstream_buffer) {
        if (_session && _fn && _bitstream_buffer && _fn->nvEncLockBitstream) {
            NV_ENC_LOCK_BITSTREAM lock_params{};
            lock_params.version = NV_ENC_LOCK_BITSTREAM_VER;
            lock_params.doNotWait = 0;
            lock_params.outputBitstream = _bitstream_buffer;

            auto pfn_lock = reinterpret_cast<PNVENCLOCKBITSTREAM>(_fn->nvEncLockBitstream);
            if (pfn_lock && pfn_lock(_session, &lock_params) == NV_ENC_SUCCESS) {
                _locked = true;
                _bitstream_ptr = static_cast<uint8_t*>(lock_params.bitstreamBufferPtr);
                _bitstream_size = lock_params.bitstreamSizeInBytes;
                _picture_type = lock_params.pictureType;
                _output_timestamp = lock_params.outputTimeStamp;
                _frame_index = lock_params.frameIdx;
            }
        }
    }

    ~NvencLockedBitstreamGuard() {
        reset();
    }

    NvencLockedBitstreamGuard(const NvencLockedBitstreamGuard&) = delete;
    NvencLockedBitstreamGuard& operator=(const NvencLockedBitstreamGuard&) = delete;

    NvencLockedBitstreamGuard(NvencLockedBitstreamGuard&& other) noexcept
        : _session(other._session),
          _fn(other._fn),
          _bitstream_buffer(other._bitstream_buffer),
          _bitstream_ptr(other._bitstream_ptr),
          _bitstream_size(other._bitstream_size),
          _picture_type(other._picture_type),
          _output_timestamp(other._output_timestamp),
          _frame_index(other._frame_index),
          _locked(other._locked) {
        other._session = nullptr;
        other._fn = nullptr;
        other._bitstream_buffer = nullptr;
        other._bitstream_ptr = nullptr;
        other._bitstream_size = 0;
        other._picture_type = 0;
        other._output_timestamp = 0;
        other._frame_index = 0;
        other._locked = false;
    }

    NvencLockedBitstreamGuard& operator=(NvencLockedBitstreamGuard&& other) noexcept {
        if (this != &other) {
            reset();
            _session = other._session;
            _fn = other._fn;
            _bitstream_buffer = other._bitstream_buffer;
            _bitstream_ptr = other._bitstream_ptr;
            _bitstream_size = other._bitstream_size;
            _picture_type = other._picture_type;
            _output_timestamp = other._output_timestamp;
            _frame_index = other._frame_index;
            _locked = other._locked;

            other._session = nullptr;
            other._fn = nullptr;
            other._bitstream_buffer = nullptr;
            other._bitstream_ptr = nullptr;
            other._bitstream_size = 0;
            other._picture_type = 0;
            other._output_timestamp = 0;
            other._frame_index = 0;
            other._locked = false;
        }
        return *this;
    }

    void reset() noexcept {
        if (_session && _fn && _bitstream_buffer && _locked && _fn->nvEncUnlockBitstream) {
            auto pfn_unlock = reinterpret_cast<PNVENCUNLOCKBITSTREAM>(_fn->nvEncUnlockBitstream);
            if (pfn_unlock) {
                pfn_unlock(_session, _bitstream_buffer);
            }
        }
        _locked = false;
        _bitstream_ptr = nullptr;
        _bitstream_size = 0;
        _picture_type = 0;
    }

    [[nodiscard]] uint32_t bitstream_size() const noexcept { return _bitstream_size; }
    [[nodiscard]] const uint8_t* bitstream_ptr() const noexcept { return _bitstream_ptr; }
    [[nodiscard]] bool is_keyframe() const noexcept {
        return _picture_type == NV_ENC_PIC_TYPE_IDR || _picture_type == NV_ENC_PIC_TYPE_I;
    }
    [[nodiscard]] uint32_t picture_type() const noexcept { return _picture_type; }
    [[nodiscard]] uint64_t output_timestamp() const noexcept { return _output_timestamp; }
    [[nodiscard]] uint32_t frame_index() const noexcept { return _frame_index; }
    [[nodiscard]] bool is_valid() const noexcept { return _locked && _bitstream_ptr != nullptr; }
    explicit operator bool() const noexcept { return is_valid(); }

private:
    void* _session{nullptr};
    const NVENC_FN_LIST* _fn{nullptr};
    void* _bitstream_buffer{nullptr};
    uint8_t* _bitstream_ptr{nullptr};
    uint32_t _bitstream_size{0};
    uint32_t _picture_type{0};
    uint64_t _output_timestamp{0};
    uint32_t _frame_index{0};
    bool _locked{false};
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencMappedResourceGuard;
using nvenc::NvencLockedBitstreamGuard;
} // namespace moonshine::encoder
