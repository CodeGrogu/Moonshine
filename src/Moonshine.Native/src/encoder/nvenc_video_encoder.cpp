#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include <cstring>
#include <chrono>
#include <iostream>

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

// GUID definitions for NVENC Codecs and Presets
static const GUID NV_ENC_CODEC_H264_GUID_LOCAL =
    { 0x6bc82769, 0x474f, 0x4dfa, { 0x8c, 0x22, 0x47, 0x00, 0x7b, 0x52, 0x30, 0x16 } };
static const GUID NV_ENC_CODEC_HEVC_GUID_LOCAL =
    { 0x790cdc98, 0x7022, 0x4da8, { 0xac, 0x83, 0x31, 0x4e, 0x9e, 0x38, 0x24, 0x49 } };
static const GUID NV_ENC_CODEC_AV1_GUID_LOCAL =
    { 0x0a352289, 0x0aa7, 0x4759, { 0x84, 0x2d, 0xdd, 0x30, 0xbf, 0x6d, 0x56, 0x96 } };
static const GUID NV_ENC_PRESET_P1_GUID_LOCAL =
    { 0xfc0a36d2, 0xa436, 0x4523, { 0x95, 0x1e, 0x2c, 0x82, 0x22, 0xb6, 0x95, 0x9e } };

typedef int NVENCSTATUS;
#define NV_ENC_SUCCESS 0

typedef struct _NVENC_FN_LIST {
    uint32_t version;
    void* nvEncOpenEncodeSession;
    void* nvEncGetEncodeGUIDCount;
    void* nvEncGetEncodeProfileGUIDCount;
    void* nvEncGetEncodeProfileGUIDs;
    void* nvEncGetEncodeGUIDs;
    void* nvEncGetInputFormatCount;
    void* nvEncGetInputFormats;
    void* nvEncGetEncodeCaps;
    void* nvEncGetEncodePresetCount;
    void* nvEncGetEncodePresetGUIDs;
    void* nvEncGetEncodePresetConfig;
    void* nvEncInitializeEncoder;
    void* nvEncCreateInputBuffer;
    void* nvEncDestroyInputBuffer;
    void* nvEncCreateBitstreamBuffer;
    void* nvEncDestroyBitstreamBuffer;
    void* nvEncEncodePicture;
    void* nvEncLockBitstream;
    void* nvEncUnlockBitstream;
    void* nvEncLockInputBuffer;
    void* nvEncUnlockInputBuffer;
    void* nvEncGetEncodeStats;
    void* nvEncGetSequenceParams;
    void* nvEncRegisterAsyncEvent;
    void* nvEncUnregisterAsyncEvent;
    void* nvEncMapInputResource;
    void* nvEncUnmapInputResource;
    void* nvEncDestroyEncoder;
    void* nvEncInvalidateRefFrames;
    void* nvEncOpenEncodeSessionEx;
    void* nvEncRegisterResource;
    void* nvEncUnregisterResource;
    void* nvEncReconfigureEncoder;
    void* reserved1;
    void* nvEncCreateSubFrameDataBuffer;
    void* nvEncDestroySubFrameDataBuffer;
    void* nvEncGetSequenceParamEx;
    void* reserved2[285];
} NVENC_FN_LIST;

typedef NVENCSTATUS(__stdcall *NvEncodeAPICreateInstance_Fn)(NVENC_FN_LIST* functionList);
typedef NVENCSTATUS(__stdcall *NvEncodeAPIGetMaxSupportedVersion_Fn)(uint32_t* version);

#endif

namespace moonshine::encoder {

NvencVideoEncoder::NvencVideoEncoder() = default;

NvencVideoEncoder::~NvencVideoEncoder() {
    cleanup();
}

bool NvencVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    // Verify adapter is genuine NVIDIA hardware
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
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) { // NVIDIA Vendor ID
        return false;
    }

    // Attempt to dynamically load nvEncodeAPI64.dll from system drivers
    HMODULE hNvenc = LoadLibraryW(L"nvEncodeAPI64.dll");
    if (!hNvenc) {
        return false;
    }

    auto createInstance = reinterpret_cast<NvEncodeAPICreateInstance_Fn>(
        GetProcAddress(hNvenc, "NvEncodeAPICreateInstance")
    );
    if (!createInstance) {
        FreeLibrary(hNvenc);
        return false;
    }

    NVENC_FN_LIST fn_list{};
    fn_list.version = (2 << 24) | (8 << 16) | sizeof(NVENC_FN_LIST);
    if (createInstance(&fn_list) != NV_ENC_SUCCESS) {
        FreeLibrary(hNvenc);
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

bool NvencVideoEncoder::encode_frame(
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

bool NvencVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config = new_config;
    _force_keyframe = true;
    return true;
}

void NvencVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void NvencVideoEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
}

bool NvencVideoEncoder::set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) {
    _preset = preset;
    _tuning = tuning;
    return true;
}

bool NvencVideoEncoder::set_intra_refresh(bool enabled, uint32_t period, uint32_t count) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_period = period;
    _intra_refresh_count = count;
    return true;
}

bool NvencVideoEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::NvidiaNvenc);

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
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) return false;

    HMODULE hNvenc = LoadLibraryW(L"nvEncodeAPI64.dll");
    if (!hNvenc) return false;

    auto createInstance = reinterpret_cast<NvEncodeAPICreateInstance_Fn>(
        GetProcAddress(hNvenc, "NvEncodeAPICreateInstance")
    );
    if (!createInstance) {
        FreeLibrary(hNvenc);
        return false;
    }

    NVENC_FN_LIST fn_list{};
    fn_list.version = (2 << 24) | (8 << 16) | sizeof(NVENC_FN_LIST);
    if (createInstance(&fn_list) != NV_ENC_SUCCESS) {
        FreeLibrary(hNvenc);
        return false;
    }

    out_caps.supported_codecs_mask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3); // H264, HEVC, HEVC Main10, AV1
    out_caps.max_width = 8192;
    out_caps.max_height = 8192;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = 1;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 200000;

    FreeLibrary(hNvenc);
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool NvencVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    static const bool s_supported = []() {
        HMODULE hNvenc = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!hNvenc) {
            hNvenc = LoadLibraryW(L"nvEncodeAPI64.dll");
        }
        if (!hNvenc) return false;
        FreeLibrary(hNvenc);
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
