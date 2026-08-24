#include "moonshine/encoder/amf_video_encoder.hpp"
#include "encoder/amf/amf_types.hpp"
#include "encoder/amf/amf_api.hpp"
#include "encoder/amf/amf_session.hpp"
#include <cstring>
#include <chrono>
#include <iostream>

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder {

struct AmfVideoEncoder::Impl {
    amf::AmfApi api;
    amf::AmfSession session;
};

AmfVideoEncoder::AmfVideoEncoder()
    : _impl(std::make_unique<Impl>()) {
}

AmfVideoEncoder::~AmfVideoEncoder() {
    cleanup();
}

bool AmfVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

    _state = AmfLifecycleState::Uninitialised;

    if (!d3d_device || !_impl) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);

    HRESULT reason = dev->GetDeviceRemovedReason();
    if (reason != S_OK) {
        _state = AmfLifecycleState::Faulted;
        return false;
    }

    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) {
        return false;
    }

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) {
        return false;
    }

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x1002) { // AMD Vendor ID
        return false;
    }

    _state = AmfLifecycleState::DeviceAttached;

    if (!_impl->api.load()) {
        _state = AmfLifecycleState::Faulted;
        return false;
    }

    _state = AmfLifecycleState::SessionCreated;

    if (!_impl->session.open(_impl->api, d3d_device)) {
        _impl->api.unload();
        _state = AmfLifecycleState::Faulted;
        return false;
    }

    _impl->session.set_preset_and_usage(_preset, _usage);
    _impl->session.set_intra_refresh(_intra_refresh_enabled, _intra_refresh_num_mbs_per_slot);

    if (!_impl->session.configure(config)) {
        _impl->session.close();
        _impl->api.unload();
        _state = AmfLifecycleState::Faulted;
        return false;
    }

    _state = AmfLifecycleState::EncoderInitialised;

    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;
    _initialized = true;
    _state = AmfLifecycleState::Ready;
    return true;
#else
    (void)d3d_device;
    (void)config;
    return false;
#endif
}

bool AmfVideoEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    out_written_size = 0;

    if (_state == AmfLifecycleState::Faulted || _state == AmfLifecycleState::Disposed ||
        !_initialized || !_impl || !_impl->session.is_open() || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        _state = AmfLifecycleState::Faulted;
        return false;
    }

    bool is_key = force_idr || _force_keyframe.load() || (_frame_counter.load() == 0);
    uint64_t current_frame = _frame_counter.load();

    auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
    uint64_t timestamp_us = static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(now).count()
    );

    _state = AmfLifecycleState::Encoding;

    bool res = _impl->session.encode(
        d3d_texture,
        is_key,
        current_frame,
        timestamp_us,
        out_desc,
        out_bitstream,
        max_buffer_size,
        out_written_size
    );

    if (res && out_written_size > 0) {
        _frame_counter++;
        _force_keyframe = false;
        _state = AmfLifecycleState::Ready;
        return true;
    }

    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        _state = AmfLifecycleState::Faulted;
    } else {
        _state = AmfLifecycleState::Ready;
    }
    return false;
#else
    (void)d3d_texture;
    (void)force_idr;
    (void)out_desc;
    (void)out_bitstream;
    (void)max_buffer_size;
    return false;
#endif
}

bool AmfVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized || !_impl || _state == AmfLifecycleState::Faulted || _state == AmfLifecycleState::Disposed) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        _state = AmfLifecycleState::Faulted;
        return false;
    }
#endif

    _config = new_config;
    _force_keyframe = true;
    bool success = _impl->session.reconfigure(new_config);
    if (!success) {
#if defined(_WIN32)
        if (dev && dev->GetDeviceRemovedReason() != S_OK) {
            _state = AmfLifecycleState::Faulted;
        }
#endif
        return false;
    }
    _state = AmfLifecycleState::Ready;
    return true;
}

void AmfVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void AmfVideoEncoder::cleanup() {
    _initialized = false;

    if (_impl) {
        _state = AmfLifecycleState::Flushing;
        _impl->session.close();
        _impl->api.unload();
    }

    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
    _state = AmfLifecycleState::Disposed;
}

bool AmfVideoEncoder::is_healthy() const noexcept {
#if defined(_WIN32)
    if (!_initialized || _state == AmfLifecycleState::Faulted ||
        _state == AmfLifecycleState::Disposed || _state == AmfLifecycleState::Uninitialised ||
        !_impl || !_impl->session.is_open() || !_d3d_device) {
        return false;
    }
    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        return false;
    }
    return true;
#else
    return false;
#endif
}

bool AmfVideoEncoder::set_preset_and_usage(AmfQualityPreset preset, AmfUsage usage) {
    _preset = preset;
    _usage = usage;
    if (_impl) {
        _impl->session.set_preset_and_usage(preset, usage);
    }
    return true;
}

bool AmfVideoEncoder::set_intra_refresh(bool enabled, uint32_t num_mbs_per_slot) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_num_mbs_per_slot = num_mbs_per_slot;
    if (_impl) {
        _impl->session.set_intra_refresh(enabled, num_mbs_per_slot);
    }
    return true;
}

bool AmfVideoEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::AmdAmf);

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) return false;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return false;

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x1002) return false;

    amf::AmfApi api;
    if (!api.load() || !api.factory()) {
        return false;
    }

    out_caps.supported_codecs_mask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3); // H264, HEVC, HEVC Main10, AV1
    out_caps.max_width = 7680;
    out_caps.max_height = 4320;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = 1;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 150000;

    api.unload();
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool AmfVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    amf::AmfApi api;
    if (!api.load()) {
        return false;
    }
    api.unload();

    return codec == VideoCodec::H264 || codec == VideoCodec::Hevc ||
           codec == VideoCodec::HevcMain10 || codec == VideoCodec::Av1;
#else
    (void)codec;
    return false;
#endif
}

} // namespace moonshine::encoder
