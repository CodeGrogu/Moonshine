#include "moonshine/encoder/amf_video_encoder.hpp"
#include <cstring>
#include <chrono>
#include <iostream>

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

typedef uint64_t amf_uint64;
typedef int32_t AMF_RESULT;
#define AMF_OK 0

typedef AMF_RESULT(__cdecl *AMFInit_Fn)(amf_uint64 version, void** ppFactory);
typedef AMF_RESULT(__cdecl *AMFQueryVersion_Fn)(amf_uint64* pVersion);

#endif

namespace moonshine::encoder {

AmfVideoEncoder::AmfVideoEncoder() = default;

AmfVideoEncoder::~AmfVideoEncoder() {
    cleanup();
}

bool AmfVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
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
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x1002) { // AMD Vendor ID
        return false;
    }

    // Attempt to load amfrt64.dll from AMD driver package
    HMODULE hAmf = LoadLibraryW(L"amfrt64.dll");
    if (!hAmf) {
        return false;
    }

    auto queryVersion = reinterpret_cast<AMFQueryVersion_Fn>(
        GetProcAddress(hAmf, "AMFQueryVersion")
    );
    if (!queryVersion) {
        FreeLibrary(hAmf);
        return false;
    }

    amf_uint64 version = 0;
    if (queryVersion(&version) != AMF_OK) {
        FreeLibrary(hAmf);
        return false;
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

bool AmfVideoEncoder::encode_frame(
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

bool AmfVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config = new_config;
    _force_keyframe = true;
    return true;
}

void AmfVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void AmfVideoEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
}

bool AmfVideoEncoder::set_preset_and_usage(AmfQualityPreset preset, AmfUsage usage) {
    _preset = preset;
    _usage = usage;
    return true;
}

bool AmfVideoEncoder::set_intra_refresh(bool enabled, uint32_t num_mbs_per_slot) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_num_mbs_per_slot = num_mbs_per_slot;
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

    HMODULE hAmf = LoadLibraryW(L"amfrt64.dll");
    if (!hAmf) return false;

    auto queryVersion = reinterpret_cast<AMFQueryVersion_Fn>(
        GetProcAddress(hAmf, "AMFQueryVersion")
    );
    if (!queryVersion) {
        FreeLibrary(hAmf);
        return false;
    }

    amf_uint64 version = 0;
    if (queryVersion(&version) != AMF_OK) {
        FreeLibrary(hAmf);
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

    FreeLibrary(hAmf);
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool AmfVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    static const bool s_supported = []() {
        HMODULE hAmf = LoadLibraryExW(L"amfrt64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!hAmf) {
            hAmf = LoadLibraryW(L"amfrt64.dll");
        }
        if (!hAmf) return false;
        FreeLibrary(hAmf);
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
