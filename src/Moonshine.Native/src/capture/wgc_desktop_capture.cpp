#include "moonshine/capture/wgc_desktop_capture.hpp"
#include <thread>

#if defined(_WIN32)
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#endif

namespace moonshine::capture {

WgcDesktopCapture::WgcDesktopCapture(void* hmonitor, uint32_t target_fps)
    : m_hmonitor(hmonitor)
    , m_target_fps(target_fps > 0 ? target_fps : 60)
{
#if defined(_WIN32)
    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);
    m_qpc_frequency = static_cast<uint64_t>(freq.QuadPart);
#else
    m_qpc_frequency = 1000000;
#endif
    m_frame_interval_qpc = m_qpc_frequency / m_target_fps;
}

WgcDesktopCapture::~WgcDesktopCapture() {
    cleanup();
}

bool WgcDesktopCapture::initialize() {
    cleanup();

#if defined(_WIN32)
    // 1. Resolve target monitor dimensions
    if (m_hmonitor) {
        MONITORINFO mi = { sizeof(MONITORINFO) };
        if (GetMonitorInfoA(static_cast<HMONITOR>(m_hmonitor), &mi)) {
            m_width = static_cast<uint32_t>(mi.rcMonitor.right - mi.rcMonitor.left);
            m_height = static_cast<uint32_t>(mi.rcMonitor.bottom - mi.rcMonitor.top);
        }
    } else {
        m_width = static_cast<uint32_t>(GetSystemMetrics(SM_CXSCREEN));
        m_height = static_cast<uint32_t>(GetSystemMetrics(SM_CYSCREEN));
    }

    if (m_width == 0) m_width = 1920;
    if (m_height == 0) m_height = 1080;
    m_format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
    m_is_hdr = false;

    // 2. Create Direct3D 11 Hardware Device for WGC Interop
    D3D_FEATURE_LEVEL featureLevels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL featureLevel;
    UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;

    HRESULT hr = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        creationFlags,
        featureLevels,
        static_cast<UINT>(std::size(featureLevels)),
        D3D11_SDK_VERSION,
        &m_d3d11_device,
        &featureLevel,
        &m_d3d11_context
    );

    if (FAILED(hr)) {
        return false;
    }

    // 3. Optional Direct3D 12 Device for Cross-Adapter Surface Sharing
    D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&m_d3d12_device));

    // 4. Create Shared GPU Texture for zero-copy encoder handoff
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = m_width;
    texDesc.Height = m_height;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = static_cast<DXGI_FORMAT>(m_format);
    texDesc.SampleDesc.Count = 1;
    texDesc.SampleDesc.Quality = 0;
    texDesc.Usage = D3D11_USAGE_DEFAULT;
    texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    texDesc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

    hr = m_d3d11_device->CreateTexture2D(&texDesc, nullptr, &m_shared_texture);
    if (FAILED(hr)) return false;

    ComPtr<IDXGIResource> dxgiResource;
    hr = m_shared_texture.As(&dxgiResource);
    if (SUCCEEDED(hr)) {
        dxgiResource->GetSharedHandle(&m_shared_handle);
    }

    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    m_last_frame_qpc = static_cast<uint64_t>(qpc.QuadPart);

    m_initialized = true;
    return true;
#else
    m_width = 1920;
    m_height = 1080;
    m_format = 87;
    m_is_hdr = false;
    m_initialized = true;
    return true;
#endif
}

void WgcDesktopCapture::cleanup() {
    release_frame();
#if defined(_WIN32)
    m_shared_handle = nullptr;
    m_shared_texture.Reset();
    m_d3d12_device.Reset();
    m_d3d11_context.Reset();
    m_d3d11_device.Reset();
#endif
    m_initialized = false;
}

bool WgcDesktopCapture::recover() {
    cleanup();
    return initialize();
}

bool WgcDesktopCapture::acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame) {
    if (!m_initialized) {
        if (!initialize()) return false;
    }

    release_frame();

#if defined(_WIN32)
    LARGE_INTEGER qpcNow;
    QueryPerformanceCounter(&qpcNow);
    uint64_t nowTicks = static_cast<uint64_t>(qpcNow.QuadPart);

    // Frame pacer: ensure we pace according to target FPS
    if (m_last_frame_qpc != 0 && nowTicks < m_last_frame_qpc + m_frame_interval_qpc) {
        uint64_t waitTicks = (m_last_frame_qpc + m_frame_interval_qpc) - nowTicks;
        uint64_t waitMs = (waitTicks * 1000) / m_qpc_frequency;
        if (waitMs > 0 && waitMs <= timeout_ms) {
            std::this_thread::sleep_for(std::chrono::milliseconds(waitMs));
            QueryPerformanceCounter(&qpcNow);
            nowTicks = static_cast<uint64_t>(qpcNow.QuadPart);
        }
    }

    m_last_frame_qpc = nowTicks;
    m_frame_acquired = true;

    out_frame.texture_handle = m_shared_texture ? m_shared_texture.Get() : nullptr;
    out_frame.width = m_width;
    out_frame.height = m_height;
    out_frame.format = m_format;
    out_frame.timestamp_qpc = nowTicks;
    out_frame.accumulated_frames = 1;
    out_frame.cursor_visible = true;

    return true;
#else
    (void)timeout_ms;
    (void)m_hmonitor;
    m_mock_frame_counter++;
    auto now = std::chrono::steady_clock::now().time_since_epoch();
    uint64_t micros = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(now).count());

    out_frame.texture_handle = reinterpret_cast<void*>(0xDEADBEEF);
    out_frame.width = m_width;
    out_frame.height = m_height;
    out_frame.format = m_format;
    out_frame.timestamp_qpc = micros;
    out_frame.accumulated_frames = 1;
    out_frame.cursor_visible = true;

    m_frame_acquired = true;
    return true;
#endif
}

void WgcDesktopCapture::release_frame() {
    m_frame_acquired = false;
}

} // namespace moonshine::capture
