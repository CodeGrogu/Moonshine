#include "moonshine/video/video_decoder_interface.hpp"
#include <cstring>

#if defined(_WIN32)
    #include <d3d11.h>
    #include <d3d11_1.h>
    #include <d3d12.h>
    #include <d3d12video.h>
    #include <dxgi1_4.h>
    #include <wrl/client.h>

    using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::video {

// ============================================================================
// Direct3D 11 Video Decoder Implementation
// ============================================================================

D3D11VideoDecoder::D3D11VideoDecoder() = default;

D3D11VideoDecoder::~D3D11VideoDecoder() {
    Shutdown();
}

int D3D11VideoDecoder::Initialize(void* hwnd, uint32_t width, uint32_t height, VideoCodec codec) {
    (void)hwnd;
    (void)width;
    (void)height;
    (void)codec;
    // STUB: D3D11VA decode-buffer submission has not been implemented, so decoder creation must fail explicitly.
    return -2;

    if (width == 0 || height == 0) return -1;

    hwnd_ = hwnd;
    width_ = width;
    height_ = height;
    codec_ = codec;
    decoded_frames_ = 0;

#if defined(_WIN32)
    // Initialize D3D11 Device with Video Support
    UINT create_flags = D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1
    };

    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL feature_level;

    HRESULT hr = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        create_flags,
        feature_levels,
        ARRAYSIZE(feature_levels),
        D3D11_SDK_VERSION,
        &device,
        &feature_level,
        &context
    );

    // Fallback to WARP software rasterizer if hardware GPU is absent (e.g. headless CI)
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_WARP,
            nullptr,
            create_flags,
            feature_levels,
            ARRAYSIZE(feature_levels),
            D3D11_SDK_VERSION,
            &device,
            &feature_level,
            &context
        );
    }

    if (SUCCEEDED(hr)) {
        initialized_ = true;
        return 0;
    }
#endif

    // Fallback for non-Windows or simulated environments
    initialized_ = true;
    return 0;
}

// STUB: Hardware D3D11VA video decoder buffer submission is modeled via frame counter increments rather than physical Direct3D 11 bitstream buffer execution in this build.
int D3D11VideoDecoder::SubmitFrame(const MoonshineFrameDesc& frame) {
    if (!initialized_ || !frame.frame_buffer || frame.total_bytes == 0) {
        return -1;
    }

    decoded_frames_++;
    return 0;
}

void D3D11VideoDecoder::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    decoded_frames_ = 0;
}

void D3D11VideoDecoder::QueryCaps(MoonshineDecoderCaps& out_caps) noexcept {
    std::memset(&out_caps, 0, sizeof(MoonshineDecoderCaps));
    // STUB: Decoder capability probing requires real D3D11VA feature negotiation, which is not implemented in this build.
    return;

    out_caps.max_width = 3840;
    out_caps.max_height = 2160;
    out_caps.max_fps = 240;
    out_caps.supports_av1 = 1;
    out_caps.supports_hevc = 1;
    out_caps.supports_h264 = 1;
    out_caps.supports_hdr10 = 1;
    out_caps.supports_10bit = 1;
    out_caps.supports_d3d12 = 1;
    out_caps.supports_vulkan = 1;
}

// ============================================================================
// Direct3D 12 Video Decoder Implementation
// ============================================================================

D3D12VideoDecoder::D3D12VideoDecoder() = default;

D3D12VideoDecoder::~D3D12VideoDecoder() {
    Shutdown();
}

int D3D12VideoDecoder::Initialize(void* hwnd, uint32_t width, uint32_t height, VideoCodec codec) {
    (void)hwnd;
    (void)width;
    (void)height;
    (void)codec;
    // STUB: D3D12 Video decode command-list submission has not been implemented, so decoder creation must fail explicitly.
    return -2;

    if (width == 0 || height == 0) return -1;

    hwnd_ = hwnd;
    width_ = width;
    height_ = height;
    codec_ = codec;
    decoded_frames_ = 0;

#if defined(_WIN32)
    ComPtr<ID3D12Device> device;
    HRESULT hr = D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device));
    if (SUCCEEDED(hr)) {
        initialized_ = true;
        return 0;
    }
#endif

    initialized_ = true;
    return 0;
}

// STUB: Hardware D3D12 video decoder buffer submission is modeled via frame counter increments rather than physical Direct3D 12 Video bitstream execution in this build.
int D3D12VideoDecoder::SubmitFrame(const MoonshineFrameDesc& frame) {
    if (!initialized_ || !frame.frame_buffer || frame.total_bytes == 0) {
        return -1;
    }

    decoded_frames_++;
    return 0;
}

void D3D12VideoDecoder::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    decoded_frames_ = 0;
}

} // namespace moonshine::video
