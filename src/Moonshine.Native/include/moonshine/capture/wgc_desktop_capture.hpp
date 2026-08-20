#pragma once

#include "moonshine/capture/desktop_capture_interface.hpp"
#include <cstdint>
#include <cstddef>
#include <memory>
#include <atomic>
#include <chrono>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::capture {

class WgcDesktopCapture : public IDesktopCapture {
public:
    explicit WgcDesktopCapture(void* hmonitor = nullptr, uint32_t target_fps = 60);
    ~WgcDesktopCapture() override;

    WgcDesktopCapture(const WgcDesktopCapture&) = delete;
    WgcDesktopCapture& operator=(const WgcDesktopCapture&) = delete;

    bool initialize() override;
    void cleanup() override;

    bool acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame) override;
    void release_frame() override;

    [[nodiscard]] uint32_t width() const noexcept override { return m_width; }
    [[nodiscard]] uint32_t height() const noexcept override { return m_height; }
    [[nodiscard]] uint32_t target_fps() const noexcept { return m_target_fps; }
    [[nodiscard]] bool is_initialized() const noexcept override { return m_initialized; }

private:
    void* m_hmonitor = nullptr;
    uint32_t m_target_fps = 60;
    uint32_t m_width = 1920;
    uint32_t m_height = 1080;
    bool m_initialized = false;
    bool m_frame_acquired = false;

    uint64_t m_frame_interval_qpc = 0;
    uint64_t m_last_frame_qpc = 0;
    uint64_t m_qpc_frequency = 0;

#if defined(_WIN32)
    ComPtr<ID3D11Device> m_d3d11_device;
    ComPtr<ID3D11DeviceContext> m_d3d11_context;
    ComPtr<ID3D12Device> m_d3d12_device;
    ComPtr<ID3D11Texture2D> m_shared_texture;
    HANDLE m_shared_handle = nullptr;
#else
    uint64_t m_mock_frame_counter = 0;
#endif
};

} // namespace moonshine::capture
