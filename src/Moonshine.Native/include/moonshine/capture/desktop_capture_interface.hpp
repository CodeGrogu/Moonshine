#pragma once

#include <cstdint>
#include <cstddef>

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

class IDesktopCapture {
public:
    virtual ~IDesktopCapture() = default;
    virtual bool initialize() = 0;
    virtual void cleanup() = 0;
    virtual bool acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame) = 0;
    virtual void release_frame() = 0;
    virtual bool recover() = 0;
    [[nodiscard]] virtual uint32_t width() const noexcept = 0;
    [[nodiscard]] virtual uint32_t height() const noexcept = 0;
    [[nodiscard]] virtual uint32_t format() const noexcept { return 87; }
    [[nodiscard]] virtual bool is_hdr() const noexcept { return false; }
    [[nodiscard]] virtual bool is_initialized() const noexcept = 0;
    [[nodiscard]] virtual void* get_device() const noexcept { return nullptr; }
};

} // namespace moonshine::capture
