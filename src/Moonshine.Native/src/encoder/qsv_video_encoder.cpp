#include "moonshine/encoder/qsv_video_encoder.hpp"
#include "encoder/qsv/qsv_types.hpp"
#include "encoder/qsv/qsv_api.hpp"
#include "encoder/qsv/qsv_session.hpp"
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

struct QsvVideoEncoder::Impl {
    qsv::QsvApi api;
    qsv::QsvSession session;
};

QsvVideoEncoder::QsvVideoEncoder()
    : _impl(std::make_unique<Impl>()) {
}

QsvVideoEncoder::~QsvVideoEncoder() {
    cleanup();
}

bool QsvVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

    _state = QsvLifecycleState::Uninitialised;

    if (!d3d_device || !_impl) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);

    HRESULT reason = dev->GetDeviceRemovedReason();
    if (reason != S_OK) {
        _state = QsvLifecycleState::Faulted;
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
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x8086) { // Intel Vendor ID
        return false;
    }

    _state = QsvLifecycleState::DeviceAttached;

    if (!_impl->api.load()) {
        _state = QsvLifecycleState::Faulted;
        return false;
    }

    if (!_impl->session.open(_impl->api, d3d_device)) {
        _state = QsvLifecycleState::Faulted;
        return false;
    }

    _state = QsvLifecycleState::SessionCreated;

    _impl->session.set_target_usage(_usage, _low_power_vdenc);
    _impl->session.set_intra_refresh(_intra_refresh_enabled, _intra_refresh_cycle_size, _intra_refresh_qp_delta);

    if (!_impl->session.configure(config)) {
        _state = QsvLifecycleState::Faulted;
        return false;
    }

    _state = QsvLifecycleState::EncoderInitialised;

    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;
    _initialized = true;
    _state = QsvLifecycleState::Ready;
    return true;
#else
    (void)d3d_device;
    (void)config;
    return false;
#endif
}

bool QsvVideoEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    out_written_size = 0;
    std::memset(&out_desc, 0, sizeof(EncodedPacketDesc));

    if (!_initialized || !_impl || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

#if defined(_WIN32)
    if (_d3d_device) {
        auto* dev = static_cast<ID3D11Device*>(_d3d_device);
        if (dev->GetDeviceRemovedReason() != S_OK) {
            _state = QsvLifecycleState::Faulted;
            _initialized = false;
            return false;
        }
    }

    _state = QsvLifecycleState::Encoding;

    bool request_idr = force_idr || _force_keyframe.load() || (_frame_counter.load() == 0);
    uint64_t frame_id = _frame_counter.load();

    auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
    uint64_t timestamp_us = std::chrono::duration_cast<std::chrono::microseconds>(now).count();

    bool ok = _impl->session.encode(
        d3d_texture,
        request_idr,
        frame_id,
        timestamp_us,
        out_desc,
        out_bitstream,
        max_buffer_size,
        out_written_size
    );

    if (ok && out_written_size > 0) {
        _frame_counter++;
        _force_keyframe = false;
        _state = QsvLifecycleState::Ready;
        return true;
    }

    if (_d3d_device && static_cast<ID3D11Device*>(_d3d_device)->GetDeviceRemovedReason() != S_OK) {
        _state = QsvLifecycleState::Faulted;
        _initialized = false;
    } else {
        _state = QsvLifecycleState::Ready;
    }
    return false;
#else
    (void)d3d_texture; (void)force_idr; (void)out_desc; (void)out_bitstream; (void)max_buffer_size;
    return false;
#endif
}

bool QsvVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized || !_impl) return false;

#if defined(_WIN32)
    if (_d3d_device) {
        auto* dev = static_cast<ID3D11Device*>(_d3d_device);
        if (dev->GetDeviceRemovedReason() != S_OK) {
            _state = QsvLifecycleState::Faulted;
            _initialized = false;
            return false;
        }
    }

    bool ok = _impl->session.reconfigure(new_config);
    if (ok) {
        _config = new_config;
        _force_keyframe = true;
    }
    return ok;
#else
    (void)new_config;
    return false;
#endif
}

void QsvVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

bool QsvVideoEncoder::drain() {
    if (!_initialized || !_impl || _state == QsvLifecycleState::Faulted || _state == QsvLifecycleState::Disposed) {
        return false;
    }
    _state = QsvLifecycleState::Flushing;
    bool res = _impl->session.drain();
    _state = QsvLifecycleState::Ready;
    return res;
}

bool QsvVideoEncoder::flush() {
    if (!_initialized || !_impl || _state == QsvLifecycleState::Faulted || _state == QsvLifecycleState::Disposed) {
        return false;
    }
    _state = QsvLifecycleState::Flushing;
    bool res = _impl->session.flush();
    _force_keyframe = true;
    _state = QsvLifecycleState::Ready;
    return res;
}

void QsvVideoEncoder::cleanup() {
    if (_impl) {
        _impl->session.close();
        _impl->api.unload();
    }
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
    _state = QsvLifecycleState::Disposed;
}

bool QsvVideoEncoder::is_healthy() const noexcept {
    if (!_initialized || !_impl || _state == QsvLifecycleState::Faulted || _state == QsvLifecycleState::Disposed || _state == QsvLifecycleState::Uninitialised) {
        return false;
    }
#if defined(_WIN32)
    if (_d3d_device) {
        auto* dev = static_cast<ID3D11Device*>(_d3d_device);
        if (dev->GetDeviceRemovedReason() != S_OK) {
            return false;
        }
    }
#endif
    return _impl->session.is_configured();
}

bool QsvVideoEncoder::set_target_usage(QsvTargetUsage usage, bool low_power_vdenc) {
    _usage = usage;
    _low_power_vdenc = low_power_vdenc;
    if (_impl && _impl->session.is_open()) {
        _impl->session.set_target_usage(usage, low_power_vdenc);
    }
    return true;
}

bool QsvVideoEncoder::set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_cycle_size = cycle_size;
    _intra_refresh_qp_delta = qp_delta;
    if (_impl && _impl->session.is_open()) {
        _impl->session.set_intra_refresh(enabled, cycle_size, qp_delta);
    }
    return true;
}

bool QsvVideoEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::IntelQuickSync);

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
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x8086) return false;

    qsv::QsvApi api;
    if (!api.load()) {
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

bool QsvVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    qsv::QsvApi api;
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
