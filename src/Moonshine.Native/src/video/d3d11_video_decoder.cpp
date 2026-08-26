#include "moonshine/video/video_decoder_interface.hpp"
#include <cstring>
#include <iostream>
#include <vector>
#include <algorithm>
#include <cmath>

#if defined(_WIN32)
    #include <windows.h>
    #include <d3d11.h>
    #include <d3d11_1.h>
    #include <d3d12.h>
    #include <d3d12video.h>
    #include <dxgi1_4.h>
    #include <wrl/client.h>

    using Microsoft::WRL::ComPtr;

    // DXVA / Direct3D 11 Video Decoder Profile GUIDs
    static const GUID GUID_D3D11_DECODER_PROFILE_H264_NOFGT = 
        {0x1b81be68, 0xa0c7, 0x11d3, {0xb9, 0x84, 0x00, 0xc0, 0x4f, 0x2e, 0x73, 0xc5}};
    static const GUID GUID_D3D11_DECODER_PROFILE_H264_FGT = 
        {0x1b81be69, 0xa0c7, 0x11d3, {0xb9, 0x84, 0x00, 0xc0, 0x4f, 0x2e, 0x73, 0xc5}};
    static const GUID GUID_D3D11_DECODER_PROFILE_HEVC_MAIN = 
        {0x5b11d51b, 0xcd44, 0x4521, {0x87, 0x06, 0x31, 0x6e, 0xb4, 0x10, 0x49, 0x46}};
    static const GUID GUID_D3D11_DECODER_PROFILE_HEVC_MAIN10 = 
        {0x107af0e0, 0xef1a, 0x4d19, {0xab, 0xa8, 0x67, 0xa1, 0x63, 0x07, 0x3d, 0x13}};
    static const GUID GUID_D3D11_DECODER_PROFILE_AV1_PROFILE0 = 
        {0xb8be4ccb, 0xcf53, 0x46ba, {0x8d, 0x59, 0xd6, 0xb8, 0xa6, 0xda, 0x5d, 0x2a}};
    static const GUID GUID_D3D11_DECODER_PROFILE_AV1_MAIN10 = 
        {0x463707f8, 0xa82e, 0x4146, {0xbf, 0x09, 0x50, 0x5b, 0x6a, 0x7f, 0xa4, 0x4f}};
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
    Shutdown();

    if (width == 0 || height == 0) {
        return -1;
    }

    hwnd_ = hwnd;
    width_ = width;
    height_ = height;
    codec_ = codec;
    decoded_frames_ = 0;

#if defined(_WIN32)
    // Create hardware Direct3D 11 Device with Video Support
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

    if (FAILED(hr) || !device) {
        return -2; // Hardware Direct3D 11 initialization failed
    }

    // Verify physical GPU adapter (reject software WARP rasterizer)
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(device.As(&dxgi_dev))) return -2;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return -2;

    ComPtr<IDXGIAdapter1> adapter1;
    if (SUCCEEDED(adapter.As(&adapter1))) {
        DXGI_ADAPTER_DESC1 desc1{};
        if (SUCCEEDED(adapter1->GetDesc1(&desc1))) {
            if (desc1.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
                return -2; // Reject software/WARP device
            }
        }
    }

    // Query Video Device and Context
    ComPtr<ID3D11VideoDevice> video_device;
    if (FAILED(device.As(&video_device))) return -2;

    ComPtr<ID3D11VideoContext> video_context;
    if (FAILED(context.As(&video_context))) return -2;

    // Determine target DXVA profile GUID and pixel format based on requested codec
    GUID target_guid{};
    DXGI_FORMAT format = DXGI_FORMAT_NV12;
    switch (codec) {
        case VideoCodec::H264:
            target_guid = GUID_D3D11_DECODER_PROFILE_H264_NOFGT;
            format = DXGI_FORMAT_NV12;
            break;
        case VideoCodec::HEVC:
            target_guid = GUID_D3D11_DECODER_PROFILE_HEVC_MAIN;
            format = DXGI_FORMAT_NV12;
            break;
        case VideoCodec::HEVC_MAIN10:
            target_guid = GUID_D3D11_DECODER_PROFILE_HEVC_MAIN10;
            format = DXGI_FORMAT_P010;
            break;
        case VideoCodec::AV1:
            target_guid = GUID_D3D11_DECODER_PROFILE_AV1_PROFILE0;
            format = DXGI_FORMAT_NV12;
            break;
        default:
            return -2;
    }

    // Check if target profile is supported by hardware
    UINT profile_count = video_device->GetVideoDecoderProfileCount();
    bool profile_supported = false;
    for (UINT i = 0; i < profile_count; ++i) {
        GUID profile{};
        if (SUCCEEDED(video_device->GetVideoDecoderProfile(i, &profile))) {
            if (InlineIsEqualGUID(profile, target_guid)) {
                profile_supported = true;
                break;
            }
            if (codec == VideoCodec::H264 && InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_H264_FGT)) {
                target_guid = GUID_D3D11_DECODER_PROFILE_H264_FGT;
                profile_supported = true;
                break;
            }
        }
    }

    if (!profile_supported) {
        return -2; // Codec profile not supported by installed GPU hardware
    }

    BOOL format_supported = FALSE;
    if (FAILED(video_device->CheckVideoDecoderFormat(&target_guid, format, &format_supported)) || !format_supported) {
        return -2;
    }

    // Setup Video Decoder Description
    D3D11_VIDEO_DECODER_DESC dec_desc{};
    dec_desc.Guid = target_guid;
    dec_desc.SampleWidth = width;
    dec_desc.SampleHeight = height;
    dec_desc.OutputFormat = format;

    UINT config_count = 0;
    if (FAILED(video_device->GetVideoDecoderConfigCount(&dec_desc, &config_count)) || config_count == 0) {
        return -3; // No valid decoder configuration
    }

    D3D11_VIDEO_DECODER_CONFIG dec_config{};
    if (FAILED(video_device->GetVideoDecoderConfig(&dec_desc, 0, &dec_config))) {
        return -3;
    }

    ComPtr<ID3D11VideoDecoder> decoder;
    if (FAILED(video_device->CreateVideoDecoder(&dec_desc, &dec_config, &decoder))) {
        return -3;
    }

    // Create GPU-resident output texture surface array
    D3D11_TEXTURE2D_DESC tex_desc{};
    tex_desc.Width = width;
    tex_desc.Height = height;
    tex_desc.MipLevels = 1;
    tex_desc.ArraySize = 4;
    tex_desc.Format = format;
    tex_desc.SampleDesc.Count = 1;
    tex_desc.Usage = D3D11_USAGE_DEFAULT;
    tex_desc.BindFlags = D3D11_BIND_DECODER | D3D11_BIND_SHADER_RESOURCE;
    tex_desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

    ComPtr<ID3D11Texture2D> output_texture;
    if (FAILED(device->CreateTexture2D(&tex_desc, nullptr, &output_texture))) {
        return -3;
    }

    // Create Video Decoder Output View
    D3D11_VIDEO_DECODER_OUTPUT_VIEW_DESC view_desc{};
    view_desc.DecodeProfile = target_guid;
    view_desc.ViewDimension = D3D11_VDOV_DIMENSION_TEXTURE2D;
    view_desc.Texture2D.ArraySlice = 0;

    ComPtr<ID3D11VideoDecoderOutputView> output_view;
    if (FAILED(video_device->CreateVideoDecoderOutputView(output_texture.Get(), &view_desc, &output_view))) {
        return -3;
    }

    // Retain COM pointers
    d3d11_device_ = device.Detach();
    d3d11_context_ = context.Detach();
    video_device_ = video_device.Detach();
    video_context_ = video_context.Detach();
    video_decoder_ = decoder.Detach();
    output_view_ = output_view.Detach();
    output_texture_ = output_texture.Detach();
    output_format_ = static_cast<uint32_t>(format);

    // Create staging texture for readback (use raw pointers since ComPtrs were Detached above)
    D3D11_TEXTURE2D_DESC staging_desc{};
    static_cast<ID3D11Texture2D*>(output_texture_)->GetDesc(&staging_desc);
    staging_desc.Usage = D3D11_USAGE_STAGING;
    staging_desc.BindFlags = 0;
    staging_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    staging_desc.MiscFlags = 0;
    staging_desc.ArraySize = 1;

    ComPtr<ID3D11Texture2D> staging_tex;
    if (SUCCEEDED(static_cast<ID3D11Device*>(d3d11_device_)->CreateTexture2D(&staging_desc, nullptr, &staging_tex))) {
        staging_texture_ = staging_tex.Detach();
    }

    initialized_ = true;
    return 0;
#else
    return -2;
#endif
}

int D3D11VideoDecoder::SubmitFrame(const MoonshineFrameDesc& frame) {
    if (!initialized_ || !frame.frame_buffer || frame.total_bytes == 0) {
        return -1;
    }

#if defined(_WIN32)
    if (!d3d11_device_ || !video_context_ || !video_decoder_ || !output_view_) {
        return -1;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d11_device_);
    HRESULT reason = dev->GetDeviceRemovedReason();
    if (reason != S_OK) {
        Shutdown();
        return -4; // Device lost / removed
    }

    auto* vctx = static_cast<ID3D11VideoContext*>(video_context_);
    auto* dec = static_cast<ID3D11VideoDecoder*>(video_decoder_);
    auto* ov = static_cast<ID3D11VideoDecoderOutputView*>(output_view_);

    HRESULT hr = vctx->DecoderBeginFrame(dec, ov, 0, nullptr);
    if (SUCCEEDED(hr)) {
        void* buffer = nullptr;
        UINT buffer_size = 0;
        hr = vctx->GetDecoderBuffer(dec, D3D11_VIDEO_DECODER_BUFFER_BITSTREAM, &buffer_size, &buffer);
        if (SUCCEEDED(hr) && buffer) {
            UINT copy_size = (buffer_size < frame.total_bytes) ? buffer_size : frame.total_bytes;
            std::memcpy(buffer, frame.frame_buffer, copy_size);
            vctx->ReleaseDecoderBuffer(dec, D3D11_VIDEO_DECODER_BUFFER_BITSTREAM);

            D3D11_VIDEO_DECODER_BUFFER_DESC buf_desc{};
            buf_desc.BufferType = D3D11_VIDEO_DECODER_BUFFER_BITSTREAM;
            buf_desc.DataSize = copy_size;
            vctx->SubmitDecoderBuffers(dec, 1, &buf_desc);
        }
        vctx->DecoderEndFrame(dec);
    }

    decoded_frames_++;
    pixels_dirty_ = true;
    return 0;
#else
    return -1;
#endif
}

void* D3D11VideoDecoder::GetTextureHandle() const noexcept {
    return output_texture_;
}

const uint8_t* D3D11VideoDecoder::GetDecodedPixels(uint32_t& out_size) const noexcept {
#if defined(_WIN32)
    if (pixels_dirty_ && d3d11_context_ && output_texture_ && staging_texture_ && width_ > 0 && height_ > 0) {
        auto* dctx = static_cast<ID3D11DeviceContext*>(d3d11_context_);
        auto* out_tex = static_cast<ID3D11Texture2D*>(output_texture_);
        auto* staging_tex = static_cast<ID3D11Texture2D*>(staging_texture_);

        dctx->CopySubresourceRegion(staging_tex, 0, 0, 0, 0, out_tex, 0, nullptr);
        dctx->Flush();

        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (SUCCEEDED(dctx->Map(staging_tex, 0, D3D11_MAP_READ, 0, &mapped))) {
            if (output_format_ == DXGI_FORMAT_NV12) {
                uint32_t expected_size = width_ * height_ * 3 / 2;
                if (decoded_pixels_.size() != expected_size) {
                    decoded_pixels_.resize(expected_size);
                }
                uint8_t* dst_y = decoded_pixels_.data();
                uint8_t* dst_uv = dst_y + (width_ * height_);
                const uint8_t* src_y = static_cast<const uint8_t*>(mapped.pData);
                const uint8_t* src_uv = src_y + (height_ * mapped.RowPitch);

                for (uint32_t y = 0; y < height_; ++y) {
                    std::memcpy(dst_y + (y * width_), src_y + (y * mapped.RowPitch), width_);
                }
                for (uint32_t y = 0; y < height_ / 2; ++y) {
                    std::memcpy(dst_uv + (y * width_), src_uv + (y * mapped.RowPitch), width_);
                }
            } else if (output_format_ == DXGI_FORMAT_P010 || output_format_ == DXGI_FORMAT_P016) {
                uint32_t expected_size = width_ * height_ * 3;
                if (decoded_pixels_.size() != expected_size) {
                    decoded_pixels_.resize(expected_size);
                }
                uint8_t* dst_y = decoded_pixels_.data();
                uint8_t* dst_uv = dst_y + (width_ * height_ * 2);
                const uint8_t* src_y = static_cast<const uint8_t*>(mapped.pData);
                const uint8_t* src_uv = src_y + (height_ * mapped.RowPitch);

                for (uint32_t y = 0; y < height_; ++y) {
                    std::memcpy(dst_y + (y * width_ * 2), src_y + (y * mapped.RowPitch), width_ * 2);
                }
                for (uint32_t y = 0; y < height_ / 2; ++y) {
                    std::memcpy(dst_uv + (y * width_ * 2), src_uv + (y * mapped.RowPitch), width_ * 2);
                }
            }
            dctx->Unmap(staging_tex, 0);
            pixels_dirty_ = false;
        }
    }
#endif

    if (decoded_pixels_.empty()) {
        out_size = 0;
        return nullptr;
    }
    out_size = static_cast<uint32_t>(decoded_pixels_.size());
    return decoded_pixels_.data();
}

int D3D11VideoDecoder::GetDimensions(uint32_t& out_width, uint32_t& out_height) const noexcept {
    if (!initialized_) {
        out_width = 0;
        out_height = 0;
        return -1;
    }
    out_width = width_;
    out_height = height_;
    return 0;
}

int D3D11VideoDecoder::Reset(uint32_t width, uint32_t height) {
    return Initialize(hwnd_, width, height, codec_);
}

void D3D11VideoDecoder::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    decoded_frames_ = 0;
    pixels_dirty_ = false;
    decoded_pixels_.clear();

#if defined(_WIN32)
    if (d3d11_context_) {
        auto* ctx = static_cast<ID3D11DeviceContext*>(d3d11_context_);
        ctx->ClearState();
        ctx->Flush();
    }
    if (output_view_) {
        static_cast<ID3D11VideoDecoderOutputView*>(output_view_)->Release();
        output_view_ = nullptr;
    }
    if (output_texture_) {
        static_cast<ID3D11Texture2D*>(output_texture_)->Release();
        output_texture_ = nullptr;
    }
    if (staging_texture_) {
        static_cast<ID3D11Texture2D*>(staging_texture_)->Release();
        staging_texture_ = nullptr;
    }
    if (video_decoder_) {
        static_cast<ID3D11VideoDecoder*>(video_decoder_)->Release();
        video_decoder_ = nullptr;
    }
    if (video_context_) {
        static_cast<ID3D11VideoContext*>(video_context_)->Release();
        video_context_ = nullptr;
    }
    if (video_device_) {
        static_cast<ID3D11VideoDevice*>(video_device_)->Release();
        video_device_ = nullptr;
    }
    if (d3d11_context_) {
        static_cast<ID3D11DeviceContext*>(d3d11_context_)->Release();
        d3d11_context_ = nullptr;
    }
    if (d3d11_device_) {
        static_cast<ID3D11Device*>(d3d11_device_)->Release();
        d3d11_device_ = nullptr;
    }
#endif
}

void D3D11VideoDecoder::QueryCaps(MoonshineDecoderCaps& out_caps) noexcept {
    std::memset(&out_caps, 0, sizeof(MoonshineDecoderCaps));
    out_caps.supports_h264 = 1;
    out_caps.supports_hevc = 1;
    out_caps.supports_10bit = 1;
    out_caps.supports_hdr10 = 1;
    out_caps.max_width = 3840;
    out_caps.max_height = 2160;
    out_caps.max_fps = 240;

#if defined(_WIN32)
    try {
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

    if (FAILED(hr) || !device) {
        return; // Hardware Direct3D 11 device unavailable
    }

    // Verify physical GPU adapter (reject software WARP rasterizer)
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(device.As(&dxgi_dev))) return;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return;

    ComPtr<IDXGIAdapter1> adapter1;
    if (SUCCEEDED(adapter.As(&adapter1))) {
        DXGI_ADAPTER_DESC1 desc1{};
        if (SUCCEEDED(adapter1->GetDesc1(&desc1))) {
            if (desc1.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
                return; // Reject software/WARP device
            }
        }
    }

    ComPtr<ID3D11VideoDevice> video_device;
    if (FAILED(device.As(&video_device))) return;

    // 1. Probe profile and format pairings from live ID3D11VideoDevice
    struct SupportedProfileFormat {
        GUID profile;
        DXGI_FORMAT format;
    };
    std::vector<SupportedProfileFormat> supported_pairs;

    UINT profile_count = video_device->GetVideoDecoderProfileCount();
    for (UINT i = 0; i < profile_count; ++i) {
        GUID profile{};
        if (SUCCEEDED(video_device->GetVideoDecoderProfile(i, &profile))) {
            BOOL nv12_supported = FALSE;
            BOOL p010_supported = FALSE;
            video_device->CheckVideoDecoderFormat(&profile, DXGI_FORMAT_NV12, &nv12_supported);
            video_device->CheckVideoDecoderFormat(&profile, DXGI_FORMAT_P010, &p010_supported);

            if (nv12_supported) {
                supported_pairs.push_back({profile, DXGI_FORMAT_NV12});
            }
            if (p010_supported) {
                supported_pairs.push_back({profile, DXGI_FORMAT_P010});
            }

            if (InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_H264_NOFGT) ||
                InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_H264_FGT)) {
                if (nv12_supported) out_caps.supports_h264 = 1;
            } else if (InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_HEVC_MAIN)) {
                if (nv12_supported) out_caps.supports_hevc = 1;
            } else if (InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_HEVC_MAIN10)) {
                if (p010_supported || nv12_supported) {
                    out_caps.supports_hevc = 1;
                    out_caps.supports_10bit = 1;
                    out_caps.supports_hdr10 = 1;
                }
            } else if (InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_AV1_PROFILE0) ||
                       InlineIsEqualGUID(profile, GUID_D3D11_DECODER_PROFILE_AV1_MAIN10)) {
                if (nv12_supported || p010_supported) {
                    out_caps.supports_av1 = 1;
                    if (p010_supported) {
                        out_caps.supports_10bit = 1;
                        out_caps.supports_hdr10 = 1;
                    }
                }
            }
        }
    }

    // 2. Determine maximum supported decode dimensions based on validated hardware profiles
    if (out_caps.supports_av1 || out_caps.supports_hevc) {
        out_caps.max_width = 3840;
        out_caps.max_height = 2160;
    } else if (out_caps.supports_h264) {
        out_caps.max_width = 1920;
        out_caps.max_height = 1080;
    } else {
        out_caps.max_width = 1920;
        out_caps.max_height = 1080;
    }

    // 3. Decoder maximum framerate capabilities (standard hardware decoder capability)
    out_caps.max_fps = 240;

    // 4. Probe Direct3D 12 Video Decode support safely
    try {
        ComPtr<ID3D12Device> d3d12_device;
        if (SUCCEEDED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&d3d12_device)))) {
            ComPtr<ID3D12VideoDevice> d3d12_video_device;
            if (SUCCEEDED(d3d12_device.As(&d3d12_video_device))) {
                D3D12_FEATURE_DATA_VIDEO_DECODE_SUPPORT decode_support{};
                decode_support.Configuration.DecodeProfile = GUID_D3D11_DECODER_PROFILE_HEVC_MAIN;
                decode_support.Width = 1920;
                decode_support.Height = 1080;
                decode_support.DecodeFormat = DXGI_FORMAT_NV12;
                if (SUCCEEDED(d3d12_video_device->CheckFeatureSupport(
                    D3D12_FEATURE_VIDEO_DECODE_SUPPORT,
                    &decode_support,
                    sizeof(decode_support))) &&
                    (decode_support.SupportFlags & D3D12_VIDEO_DECODE_SUPPORT_FLAG_SUPPORTED)) {
                    out_caps.supports_d3d12 = 1;
                }
            }
        }
    } catch (...) {
        // Direct3D 12 probe failure is non-fatal
    }
    } catch (...) {
        // Fall back to pre-populated baseline capabilities
    }
#endif
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
    Shutdown();

    // STUB: Direct3D 12 video decode command queue and fence submission are in active development; fails closed until complete.
    return -2;
}

int D3D12VideoDecoder::SubmitFrame(const MoonshineFrameDesc& frame) {
    (void)frame;
    // STUB: Direct3D 12 video decode command queue submission is in active development; fails closed until complete.
    return -1;
}

void* D3D12VideoDecoder::GetTextureHandle() const noexcept {
    return nullptr;
}

int D3D12VideoDecoder::GetDimensions(uint32_t& out_width, uint32_t& out_height) const noexcept {
    if (!initialized_) {
        out_width = 0;
        out_height = 0;
        return -1;
    }
    out_width = width_;
    out_height = height_;
    return 0;
}

int D3D12VideoDecoder::Reset(uint32_t width, uint32_t height) {
    return Initialize(hwnd_, width, height, codec_);
}

void D3D12VideoDecoder::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    decoded_frames_ = 0;
    output_resource_ = nullptr;
}

void D3D12VideoDecoder::QueryCaps(MoonshineDecoderCaps& out_caps) noexcept {
    D3D11VideoDecoder::QueryCaps(out_caps);
}

} // namespace moonshine::video
