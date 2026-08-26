#include "moonshine/video/dxgi_swapchain.hpp"
#include <algorithm>
#include <cstring>
#include <iostream>

#if defined(_WIN32)
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
    #include <d3d11.h>
    #include <d3d11_1.h>
    #include <dxgi1_4.h>
    #include <dxgi1_5.h>
    #include <dxgi1_6.h>
    #include <wrl/client.h>

    using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::video {

DxgiSwapchain::DxgiSwapchain() = default;

DxgiSwapchain::~DxgiSwapchain() {
    Shutdown();
}

void* DxgiSwapchain::GetD3DDevice() const noexcept {
#if defined(_WIN32)
    return device_.Get();
#else
    return nullptr;
#endif
}

void DxgiSwapchain::GetMetrics(MoonshineSwapchainMetrics& out_metrics) const noexcept {
    out_metrics.frames_presented = presented_frames_;
    out_metrics.presentation_errors = presentation_errors_;
    out_metrics.dropped_frames = dropped_frames_;
}

void DxgiSwapchain::ReleaseViews() {
#if defined(_WIN32)
    video_output_view_.Reset();
    video_processor_.Reset();
    video_enumerator_.Reset();
    video_context_.Reset();
    video_device_.Reset();
    rtv_.Reset();
    backbuffer_.Reset();
#endif
}

int DxgiSwapchain::CreateOrRecreateViews() {
#if defined(_WIN32)
    if (!swapchain1_ || !device_) {
        return -1;
    }

    HRESULT hr = swapchain1_->GetBuffer(0, IID_PPV_ARGS(&backbuffer_));
    if (FAILED(hr) || !backbuffer_) {
        return -1;
    }

    hr = device_->CreateRenderTargetView(backbuffer_.Get(), nullptr, &rtv_);
    if (FAILED(hr)) {
        return -1;
    }

    // Attempt video processor setup for GPU-side NV12/P010 color conversion
    if (SUCCEEDED(device_.As(&video_device_)) && SUCCEEDED(context_.As(&video_context_))) {
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content_desc{};
        content_desc.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content_desc.InputWidth = width_;
        content_desc.InputHeight = height_;
        content_desc.OutputWidth = width_;
        content_desc.OutputHeight = height_;
        content_desc.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;

        if (SUCCEEDED(video_device_->CreateVideoProcessorEnumerator(&content_desc, &video_enumerator_))) {
            video_device_->CreateVideoProcessor(video_enumerator_.Get(), 0, &video_processor_);

            D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC out_desc{};
            out_desc.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
            out_desc.Texture2D.MipSlice = 0;
            video_device_->CreateVideoProcessorOutputView(
                backbuffer_.Get(),
                video_enumerator_.Get(),
                &out_desc,
                &video_output_view_
            );
        }
    }

    return 0;
#else
    return 0;
#endif
}

int DxgiSwapchain::Initialize(void* hwnd, void* d3d11_device, uint32_t width, uint32_t height, uint32_t buffer_count, bool is_hdr10) {
    Shutdown();

    if (width == 0 || height == 0) {
        return -1;
    }

    hwnd_ = hwnd;
    width_ = width;
    height_ = height;
    buffer_count_ = (buffer_count < 2) ? 2 : (buffer_count > 4 ? 4 : buffer_count);
    is_hdr10_ = is_hdr10;
    presented_frames_ = 0;
    presentation_errors_ = 0;
    dropped_frames_ = 0;

#if defined(_WIN32)
    if (d3d11_device) {
        auto* dev = static_cast<ID3D11Device*>(d3d11_device);
        device_ = dev;
        dev->GetImmediateContext(&context_);
    } else {
        UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        D3D_FEATURE_LEVEL feature_levels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL_10_1
        };
        D3D_FEATURE_LEVEL selected_level{};

        HRESULT hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            create_flags,
            feature_levels,
            ARRAYSIZE(feature_levels),
            D3D11_SDK_VERSION,
            &device_,
            &selected_level,
            &context_
        );

        if (FAILED(hr) || !device_) {
            return -2;
        }
    }

    ComPtr<IDXGIDevice> dxgi_device;
    if (FAILED(device_.As(&dxgi_device))) return -2;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_device->GetAdapter(&adapter))) return -2;

    ComPtr<IDXGIFactory2> factory2;
    if (FAILED(adapter->GetParent(IID_PPV_ARGS(&factory2)))) return -2;

    // Check for tearing feature support (DXGI 1.5+)
    tearing_supported_ = false;
    ComPtr<IDXGIFactory5> factory5;
    if (SUCCEEDED(factory2.As(&factory5))) {
        BOOL allow_tearing = FALSE;
        if (SUCCEEDED(factory5->CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING, &allow_tearing, sizeof(allow_tearing)))) {
            tearing_supported_ = (allow_tearing == TRUE);
        }
    }

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = width_;
    desc.Height = height_;
    desc.Format = is_hdr10_ ? DXGI_FORMAT_R10G10B10A2_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.Stereo = FALSE;
    desc.SampleDesc.Count = 1;
    desc.SampleDesc.Quality = 0;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = buffer_count_;
    desc.Scaling = DXGI_SCALING_NONE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    desc.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;

    if (tearing_supported_) {
        desc.Flags |= DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
    }

    HRESULT hr = S_OK;
    if (hwnd_) {
        desc.Scaling = DXGI_SCALING_NONE;
        hr = factory2->CreateSwapChainForHwnd(
            device_.Get(),
            static_cast<HWND>(hwnd_),
            &desc,
            nullptr,
            nullptr,
            &swapchain1_
        );
        if (SUCCEEDED(hr) && swapchain1_) {
            factory2->MakeWindowAssociation(static_cast<HWND>(hwnd_), DXGI_MWA_NO_ALT_ENTER);
        }
    } else {
        // WinUI 3 Composition SwapChain for ISwapChainPanelNative
        desc.Scaling = DXGI_SCALING_STRETCH;
        hr = factory2->CreateSwapChainForComposition(
            device_.Get(),
            &desc,
            nullptr,
            &swapchain1_
        );
    }

    if (FAILED(hr) || !swapchain1_) {
        return -2;
    }

    // Query IDXGISwapChain2 for 1-frame latency waitable object
    if (SUCCEEDED(swapchain1_.As(&swapchain2_))) {
        swapchain2_->SetMaximumFrameLatency(1);
        waitable_object_ = swapchain2_->GetFrameLatencyWaitableObject();
    }

    // Configure HDR10 Rec.2020 color space if requested
    if (is_hdr10_ && SUCCEEDED(swapchain1_.As(&swapchain3_))) {
        swapchain3_->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
    }

    // Query IDXGISwapChain4 for advanced HDR metadata
    swapchain1_.As(&swapchain4_);

    if (CreateOrRecreateViews() != 0) {
        Shutdown();
        return -2;
    }

    initialized_ = true;
    return 0;
#else
    initialized_ = true;
    return 0;
#endif
}

int DxgiSwapchain::Present(uint32_t sync_interval, uint32_t flags) {
    if (!initialized_) return -1;

#if defined(_WIN32)
    if (!swapchain1_) return -1;

    UINT present_flags = flags;
    if (tearing_supported_ && sync_interval == 0) {
        present_flags |= DXGI_PRESENT_ALLOW_TEARING;
    }

    HRESULT hr = swapchain1_->Present(sync_interval, present_flags);
    if (hr == DXGI_STATUS_OCCLUDED) {
        // Window is occluded (e.g. minimised or covered): handled cleanly without recording spurious presentation errors
        return 0;
    }

    if (FAILED(hr)) {
        presentation_errors_++;
        if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
            return -2; // Device lost
        }
        return -1;
    }

    presented_frames_++;
    return 0;
#else
    presented_frames_++;
    return 0;
#endif
}

#if defined(_WIN32)
static bool SafeGetTextureDesc(ID3D11Texture2D* tex, D3D11_TEXTURE2D_DESC* out_desc) noexcept {
    if (!tex || !out_desc) return false;
    __try {
        tex->GetDesc(out_desc);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}
#endif

int DxgiSwapchain::PresentTexture(void* texture_handle, uint32_t sync_interval, uint32_t flags) {
    if (!initialized_) return -1;

#if defined(_WIN32)
    if (!swapchain1_ || !context_) return -1;

    if (texture_handle) {
        auto* src_tex = static_cast<ID3D11Texture2D*>(texture_handle);
        D3D11_TEXTURE2D_DESC src_desc{};
        if (SafeGetTextureDesc(src_tex, &src_desc)) {
            if (backbuffer_) {
                D3D11_TEXTURE2D_DESC dst_desc{};
                backbuffer_->GetDesc(&dst_desc);

                if (src_desc.Format == dst_desc.Format && src_desc.Width == dst_desc.Width && src_desc.Height == dst_desc.Height) {
                    context_->CopyResource(backbuffer_.Get(), src_tex);
                } else if (video_processor_ && video_context_ && video_output_view_) {
                    D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC in_desc{};
                    in_desc.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
                    in_desc.Texture2D.MipSlice = 0;

                    ComPtr<ID3D11VideoProcessorInputView> input_view;
                    if (SUCCEEDED(video_device_->CreateVideoProcessorInputView(src_tex, video_enumerator_.Get(), &in_desc, &input_view))) {
                        D3D11_VIDEO_PROCESSOR_STREAM stream{};
                        stream.Enable = TRUE;
                        stream.pInputSurface = input_view.Get();

                        video_context_->VideoProcessorBlt(
                            video_processor_.Get(),
                            video_output_view_.Get(),
                            0,
                            1,
                            &stream
                        );
                    } else {
                        context_->CopyResource(backbuffer_.Get(), src_tex);
                    }
                } else {
                    context_->CopyResource(backbuffer_.Get(), src_tex);
                }
            }
        }
    }

    return Present(sync_interval, flags);
#else
    presented_frames_++;
    return 0;
#endif
}

int DxgiSwapchain::Resize(uint32_t width, uint32_t height) {
    if (!initialized_ || width == 0 || height == 0) return -1;

    width_ = width;
    height_ = height;

#if defined(_WIN32)
    if (!swapchain1_) return -1;

    ReleaseViews();

    UINT flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
    if (tearing_supported_) {
        flags |= DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
    }

    DXGI_FORMAT format = is_hdr10_ ? DXGI_FORMAT_R10G10B10A2_UNORM : DXGI_FORMAT_B8G8R8A8_UNORM;
    HRESULT hr = swapchain1_->ResizeBuffers(buffer_count_, width_, height_, format, flags);
    if (FAILED(hr)) {
        return -1;
    }

    return CreateOrRecreateViews();
#else
    return 0;
#endif
}

int DxgiSwapchain::SetHdr(bool is_hdr10) {
    if (!initialized_) return -1;
    is_hdr10_ = is_hdr10;

#if defined(_WIN32)
    int res = Resize(width_, height_);
    if (res == 0 && swapchain3_) {
        DXGI_COLOR_SPACE_TYPE color_space = is_hdr10_
            ? DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020
            : DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709;
        swapchain3_->SetColorSpace1(color_space);
    }
    return res;
#else
    return 0;
#endif
}

int DxgiSwapchain::SetHdrMetadata(const MoonshineHdr10Metadata* metadata) {
    if (!initialized_ || !metadata) return -1;

#if defined(_WIN32)
    if (!swapchain4_) return -1;

    // Luminance bounds: 0 to 10,000 nits (100,000,000 in 0.0001 cd/m^2 units)
    constexpr uint32_t kMaxMasteringLuminanceUnits = 100000000; // 10,000 nits * 10000
    uint32_t max_lum = (metadata->max_mastering_luminance < kMaxMasteringLuminanceUnits)
        ? metadata->max_mastering_luminance
        : kMaxMasteringLuminanceUnits;
    uint32_t min_lum = (metadata->min_mastering_luminance < max_lum)
        ? metadata->min_mastering_luminance
        : max_lum;
    uint16_t max_cll = (metadata->max_content_light_level < 10000)
        ? metadata->max_content_light_level
        : static_cast<uint16_t>(10000);
    uint16_t max_fall = (metadata->max_frame_average_light_level < 10000)
        ? metadata->max_frame_average_light_level
        : static_cast<uint16_t>(10000);
    if (max_cll > 0 && max_fall > max_cll) {
        max_fall = max_cll;
    }

    DXGI_HDR_METADATA_HDR10 hdr10{};
    hdr10.RedPrimary[0] = metadata->red_primary[0];
    hdr10.RedPrimary[1] = metadata->red_primary[1];
    hdr10.GreenPrimary[0] = metadata->green_primary[0];
    hdr10.GreenPrimary[1] = metadata->green_primary[1];
    hdr10.BluePrimary[0] = metadata->blue_primary[0];
    hdr10.BluePrimary[1] = metadata->blue_primary[1];
    hdr10.WhitePoint[0] = metadata->white_point[0];
    hdr10.WhitePoint[1] = metadata->white_point[1];
    hdr10.MaxMasteringLuminance = max_lum;
    hdr10.MinMasteringLuminance = min_lum;
    hdr10.MaxContentLightLevel = max_cll;
    hdr10.MaxFrameAverageLightLevel = max_fall;

    HRESULT hr = swapchain4_->SetHDRMetaData(DXGI_HDR_METADATA_TYPE_HDR10, sizeof(hdr10), &hdr10);
    return SUCCEEDED(hr) ? 0 : -1;
#else
    return 0;
#endif
}

void DxgiSwapchain::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    width_ = 0;
    height_ = 0;
    waitable_object_ = nullptr;
    tearing_supported_ = false;

#if defined(_WIN32)
    ReleaseViews();
    swapchain4_.Reset();
    swapchain3_.Reset();
    swapchain2_.Reset();
    swapchain1_.Reset();
    context_.Reset();
    device_.Reset();
#endif
}

void* DxgiSwapchain::GetDxgiSwapChain() const noexcept {
#if defined(_WIN32)
    return swapchain1_.Get();
#else
    return nullptr;
#endif
}

} // namespace moonshine::video
