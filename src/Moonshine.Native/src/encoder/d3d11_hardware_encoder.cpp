#include "moonshine/encoder/d3d11_hardware_encoder.hpp"
#include <cstring>
#include <chrono>
#include <iostream>

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <dxgi1_2.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfuuid.lib")
#endif

namespace moonshine::encoder {

D3D11HardwareEncoder::D3D11HardwareEncoder() = default;

D3D11HardwareEncoder::~D3D11HardwareEncoder() {
    cleanup();
}

bool D3D11HardwareEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
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

    ComPtr<IDXGIAdapter1> adapter1;
    if (SUCCEEDED(adapter.As(&adapter1))) {
        DXGI_ADAPTER_DESC1 desc1{};
        if (SUCCEEDED(adapter1->GetDesc1(&desc1))) {
            if (desc1.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
                return false;
            }
        }
    }

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

bool D3D11HardwareEncoder::encode_frame(
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

bool D3D11HardwareEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config = new_config;
    _force_keyframe = true;
    return true;
}

void D3D11HardwareEncoder::request_keyframe() {
    _force_keyframe = true;
}

bool D3D11HardwareEncoder::drain() {
    return _initialized;
}

bool D3D11HardwareEncoder::flush() {
    if (!_initialized) return false;
    _force_keyframe = true;
    return true;
}

void D3D11HardwareEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
}

bool D3D11HardwareEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::Direct3D11Hardware);

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) return false;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return false;

    ComPtr<IDXGIAdapter1> adapter1;
    if (SUCCEEDED(adapter.As(&adapter1))) {
        DXGI_ADAPTER_DESC1 desc1{};
        if (SUCCEEDED(adapter1->GetDesc1(&desc1))) {
            if (desc1.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
                return false;
            }
        }
    }

    out_caps.supported_codecs_mask = (1 << 0) | (1 << 1); // H264, HEVC
    out_caps.max_width = 4096;
    out_caps.max_height = 4096;
    out_caps.max_fps = 120;
    out_caps.supports_10bit = 0;
    out_caps.supports_lossless = 0;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 100000;
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

} // namespace moonshine::encoder
