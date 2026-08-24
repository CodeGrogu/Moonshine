#include "moonshine/encoder/qsv_video_encoder.hpp"
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

QsvVideoEncoder::QsvVideoEncoder() = default;

QsvVideoEncoder::~QsvVideoEncoder() {
    cleanup();
}

bool QsvVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d_device);
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

    // Check for Intel oneVPL / Media SDK library
    HMODULE hVpl = LoadLibraryW(L"vpl.dll");
    if (!hVpl) {
        hVpl = LoadLibraryW(L"mfx64.dll");
    }
    if (!hVpl) {
        return false;
    }

    FreeLibrary(hVpl);

    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;
    _initialized = true;
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
    if (!_initialized || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        out_written_size = 0;
        return false;
    }

    bool is_key = force_idr || _force_keyframe.exchange(false) || (_frame_counter == 0);

    out_desc.frame_index = _frame_counter++;
    auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
    out_desc.timestamp_qpc = std::chrono::duration_cast<std::chrono::microseconds>(now).count();
    out_desc.is_keyframe = is_key ? 1 : 0;
    out_desc.is_header_packet = is_key ? 1 : 0;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    out_written_size = 0;
    out_desc.payload_size = 0;
    return false;
}

bool QsvVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config = new_config;
    _force_keyframe = true;
    return true;
}

void QsvVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void QsvVideoEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
}

bool QsvVideoEncoder::set_target_usage(QsvTargetUsage usage, bool low_power_vdenc) {
    _usage = usage;
    _low_power_vdenc = low_power_vdenc;
    return true;
}

bool QsvVideoEncoder::set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_cycle_size = cycle_size;
    _intra_refresh_qp_delta = qp_delta;
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

    HMODULE hVpl = LoadLibraryW(L"vpl.dll");
    if (!hVpl) {
        hVpl = LoadLibraryW(L"mfx64.dll");
    }
    if (!hVpl) return false;

    out_caps.supported_codecs_mask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3); // H264, HEVC, HEVC Main10, AV1
    out_caps.max_width = 7680;
    out_caps.max_height = 4320;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = 1;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 150000;

    FreeLibrary(hVpl);
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool QsvVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    static const bool s_supported = []() {
        HMODULE hVpl = LoadLibraryExW(L"vpl.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!hVpl) {
            hVpl = LoadLibraryW(L"vpl.dll");
        }
        if (!hVpl) {
            hVpl = LoadLibraryExW(L"mfx64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        }
        if (!hVpl) {
            hVpl = LoadLibraryW(L"mfx64.dll");
        }
        if (!hVpl) return false;
        FreeLibrary(hVpl);
        return true;
    }();
    if (!s_supported) return false;
    return codec == VideoCodec::H264 || codec == VideoCodec::Hevc ||
           codec == VideoCodec::HevcMain10 || codec == VideoCodec::Av1;
#else
    (void)codec;
    return false;
#endif
}

} // namespace moonshine::encoder
