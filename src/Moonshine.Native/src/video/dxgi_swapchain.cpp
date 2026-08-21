#include "moonshine/video/dxgi_swapchain.hpp"
#include <cstring>

#if defined(_WIN32)
    #include <d3d11.h>
    #include <d3d11_1.h>
    #include <dxgi1_4.h>
    #include <dxgi1_5.h>
    #include <wrl/client.h>

    using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::video {

DxgiSwapchain::DxgiSwapchain() = default;

DxgiSwapchain::~DxgiSwapchain() {
    Shutdown();
}

int DxgiSwapchain::Initialize(void* hwnd, void* d3d11_device, uint32_t width, uint32_t height, uint32_t buffer_count, bool is_hdr10) {
    (void)hwnd;
    (void)d3d11_device;
    (void)width;
    (void)height;
    (void)buffer_count;
    (void)is_hdr10;
    // STUB: The swapchain does not retain and present a real IDXGISwapChain, so creation must fail explicitly.
    return -2;

    if (width == 0 || height == 0) return -1;

    hwnd_ = hwnd;
    d3d_device_ = d3d11_device;
    width_ = width;
    height_ = height;
    buffer_count_ = (buffer_count < 2) ? 2 : buffer_count;
    is_hdr10_ = is_hdr10;
    presented_frames_ = 0;

#if defined(_WIN32)
    if (hwnd_ && d3d_device_) {
        auto* device = static_cast<ID3D11Device*>(d3d_device_);

        ComPtr<IDXGIDevice> dxgi_device;
        if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&dxgi_device)))) {
            ComPtr<IDXGIAdapter> adapter;
            if (SUCCEEDED(dxgi_device->GetAdapter(&adapter))) {
                ComPtr<IDXGIFactory2> factory;
                if (SUCCEEDED(adapter->GetParent(IID_PPV_ARGS(&factory)))) {
                    DXGI_SWAP_CHAIN_DESC1 desc = {};
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
                    desc.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;

                    ComPtr<IDXGISwapChain1> swapchain1;
                    HRESULT hr = factory->CreateSwapChainForHwnd(
                        device,
                        static_cast<HWND>(hwnd_),
                        &desc,
                        nullptr,
                        nullptr,
                        &swapchain1
                    );

                    if (SUCCEEDED(hr)) {
                        // Disable Alt+Enter default DXGI window handler
                        factory->MakeWindowAssociation(static_cast<HWND>(hwnd_), DXGI_MWA_NO_ALT_ENTER);

                        // Configure HDR10 Rec.2020 color space if requested
                        if (is_hdr10_) {
                            ComPtr<IDXGISwapChain3> swapchain3;
                            if (SUCCEEDED(swapchain1.As(&swapchain3))) {
                                swapchain3->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
                            }
                        }

                        initialized_ = true;
                        return 0;
                    }
                }
            }
        }
    }
#endif

    initialized_ = true;
    return 0;
}

int DxgiSwapchain::Present(uint32_t sync_interval, uint32_t flags) {
    if (!initialized_) return -1;
    (void)sync_interval;
    (void)flags;

    presented_frames_++;
    return 0;
}

int DxgiSwapchain::Resize(uint32_t width, uint32_t height) {
    if (!initialized_ || width == 0 || height == 0) return -1;

    width_ = width;
    height_ = height;
    return 0;
}

int DxgiSwapchain::SetHdr(bool is_hdr10) {
    if (!initialized_) return -1;
    is_hdr10_ = is_hdr10;
    return 0;
}

void DxgiSwapchain::Shutdown() {
    initialized_ = false;
    hwnd_ = nullptr;
    d3d_device_ = nullptr;
    presented_frames_ = 0;
}

} // namespace moonshine::video
