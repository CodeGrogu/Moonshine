#pragma once

#include "moonshine/capture/desktop_capture_interface.hpp"
#include <cstdint>
#include <cstddef>
#include <memory>
#include <chrono>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::capture {

class DxgiDesktopDuplicator : public IDesktopCapture {
public:
    explicit DxgiDesktopDuplicator(uint32_t adapter_index = 0, uint32_t output_index = 0);
    ~DxgiDesktopDuplicator() override;

    DxgiDesktopDuplicator(const DxgiDesktopDuplicator&) = delete;
    DxgiDesktopDuplicator& operator=(const DxgiDesktopDuplicator&) = delete;

    bool initialize() override;
    void cleanup() override;

    bool acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame) override;
    void release_frame() override;
    bool recover() override;

    [[nodiscard]] uint32_t width() const noexcept override { return m_width; }
    [[nodiscard]] uint32_t height() const noexcept override { return m_height; }
    [[nodiscard]] uint32_t format() const noexcept override { return m_format; }
    [[nodiscard]] bool is_hdr() const noexcept override { return m_is_hdr; }
    [[nodiscard]] bool is_initialized() const noexcept override { return m_initialized; }
    [[nodiscard]] void* get_device() const noexcept override {
#if defined(_WIN32)
        return m_device.Get();
#else
        return nullptr;
#endif
    }

private:
    uint32_t m_adapter_index;
    uint32_t m_output_index;
    uint32_t m_width = 1920;
    uint32_t m_height = 1080;
    uint32_t m_format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
    bool m_is_hdr = false;
    bool m_initialized = false;
    bool m_frame_acquired = false;

#if defined(_WIN32)
    ComPtr<ID3D11Device> m_device;
    ComPtr<ID3D11DeviceContext> m_context;
    ComPtr<IDXGIOutputDuplication> m_duplication;
    ComPtr<ID3D11Texture2D> m_staging_texture;
    ComPtr<ID3D11Texture2D> m_shared_texture;
    HANDLE m_shared_handle = nullptr;
#else
    uint64_t m_mock_frame_counter = 0;
#endif
};

} // namespace moonshine::capture
