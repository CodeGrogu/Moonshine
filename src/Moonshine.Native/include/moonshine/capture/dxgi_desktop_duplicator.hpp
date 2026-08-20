#pragma once

#include <cstdint>
#include <cstddef>
#include <memory>
#include <chrono>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::capture {

struct CaptureFrame {
    void*    texture_handle = nullptr;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t format = 0; // DXGI_FORMAT_B8G8R8A8_UNORM = 87, DXGI_FORMAT_R10G10B10A2_UNORM = 24
    uint64_t timestamp_qpc = 0;
    uint32_t accumulated_frames = 0;
    bool     cursor_visible = false;
};

class DxgiDesktopDuplicator {
public:
    explicit DxgiDesktopDuplicator(uint32_t adapter_index = 0, uint32_t output_index = 0);
    ~DxgiDesktopDuplicator();

    DxgiDesktopDuplicator(const DxgiDesktopDuplicator&) = delete;
    DxgiDesktopDuplicator& operator=(const DxgiDesktopDuplicator&) = delete;

    bool initialize();
    void cleanup();

    bool acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame);
    void release_frame();

    [[nodiscard]] uint32_t width() const noexcept { return m_width; }
    [[nodiscard]] uint32_t height() const noexcept { return m_height; }
    [[nodiscard]] bool is_initialized() const noexcept { return m_initialized; }

private:
    uint32_t m_adapter_index;
    uint32_t m_output_index;
    uint32_t m_width = 1920;
    uint32_t m_height = 1080;
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
