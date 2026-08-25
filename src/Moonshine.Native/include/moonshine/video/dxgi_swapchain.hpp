#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include <cstdint>
#include <cstddef>

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
#endif

namespace moonshine::video {

/**
 * @brief High-performance DXGI Flip Model swapchain presentation engine.
 * Supports DXGI_SWAP_EFFECT_FLIP_DISCARD, tearing for VRR (G-Sync/FreeSync),
 * 1-frame latency waitable object, direct GPU-to-GPU decoded texture blitting,
 * and HDR10 (ST 2084 / Rec.2020) color space configuration.
 */
class DxgiSwapchain {
public:
    DxgiSwapchain();
    ~DxgiSwapchain();

    int Initialize(void* hwnd, void* d3d11_device, uint32_t width, uint32_t height, uint32_t buffer_count, bool is_hdr10);
    int Present(uint32_t sync_interval, uint32_t flags);
    int PresentTexture(void* texture_handle, uint32_t sync_interval, uint32_t flags);
    int Resize(uint32_t width, uint32_t height);
    int SetHdr(bool is_hdr10);
    int SetHdrMetadata(const MoonshineHdr10Metadata* metadata);
    void Shutdown();

    void GetMetrics(MoonshineSwapchainMetrics& out_metrics) const noexcept;
    [[nodiscard]] bool IsInitialized() const noexcept { return initialized_; }
    [[nodiscard]] bool IsHdr() const noexcept { return is_hdr10_; }
    [[nodiscard]] bool IsTearingSupported() const noexcept { return tearing_supported_; }
    [[nodiscard]] void* GetFrameLatencyWaitableObject() const noexcept { return waitable_object_; }
    [[nodiscard]] uint64_t GetPresentedFrames() const noexcept { return presented_frames_; }
    [[nodiscard]] uint64_t GetPresentationErrors() const noexcept { return presentation_errors_; }
    [[nodiscard]] uint32_t GetWidth() const noexcept { return width_; }
    [[nodiscard]] uint32_t GetHeight() const noexcept { return height_; }
    [[nodiscard]] void* GetD3DDevice() const noexcept;

private:
    int CreateOrRecreateViews();
    void ReleaseViews();

    void* hwnd_{nullptr};
    uint32_t width_{0};
    uint32_t height_{0};
    uint32_t buffer_count_{2};
    bool is_hdr10_{false};
    bool initialized_{false};
    bool tearing_supported_{false};
    void* waitable_object_{nullptr};

    uint64_t presented_frames_{0};
    uint64_t presentation_errors_{0};
    uint64_t dropped_frames_{0};

#if defined(_WIN32)
    Microsoft::WRL::ComPtr<ID3D11Device> device_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_{nullptr};
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swapchain1_{nullptr};
    Microsoft::WRL::ComPtr<IDXGISwapChain2> swapchain2_{nullptr};
    Microsoft::WRL::ComPtr<IDXGISwapChain3> swapchain3_{nullptr};
    Microsoft::WRL::ComPtr<IDXGISwapChain4> swapchain4_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11Texture2D> backbuffer_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> rtv_{nullptr};

    Microsoft::WRL::ComPtr<ID3D11VideoDevice> video_device_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11VideoContext> video_context_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11VideoProcessor> video_processor_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11VideoProcessorEnumerator> video_enumerator_{nullptr};
    Microsoft::WRL::ComPtr<ID3D11VideoProcessorOutputView> video_output_view_{nullptr};
#endif
};

} // namespace moonshine::video
