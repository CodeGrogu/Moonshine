#pragma once

#include <cstdint>
#include <cstddef>

namespace moonshine::video {

/**
 * @brief High-performance DXGI Flip Model swapchain presentation engine.
 * Supports DXGI_SWAP_EFFECT_FLIP_DISCARD, tearing for VRR (G-Sync/FreeSync),
 * and HDR10 (ST 2084 / Rec.2020) color space configuration.
 */
class DxgiSwapchain {
public:
    DxgiSwapchain();
    ~DxgiSwapchain();

    int Initialize(void* hwnd, void* d3d11_device, uint32_t width, uint32_t height, uint32_t buffer_count, bool is_hdr10);
    int Present(uint32_t sync_interval, uint32_t flags);
    int Resize(uint32_t width, uint32_t height);
    int SetHdr(bool is_hdr10);
    void Shutdown();

    [[nodiscard]] bool IsInitialized() const noexcept { return initialized_; }
    [[nodiscard]] bool IsHdr() const noexcept { return is_hdr10_; }
    [[nodiscard]] uint32_t GetPresentedFrames() const noexcept { return presented_frames_; }
    [[nodiscard]] uint32_t GetWidth() const noexcept { return width_; }
    [[nodiscard]] uint32_t GetHeight() const noexcept { return height_; }

private:
    void* hwnd_{nullptr};
    void* d3d_device_{nullptr};
    uint32_t width_{0};
    uint32_t height_{0};
    uint32_t buffer_count_{2};
    bool is_hdr10_{false};
    bool initialized_{false};
    uint32_t presented_frames_{0};
};

} // namespace moonshine::video
