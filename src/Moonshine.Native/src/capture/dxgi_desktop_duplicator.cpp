#include "moonshine/capture/dxgi_desktop_duplicator.hpp"

#if defined(_WIN32)
#include <dxgi1_3.h>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#endif

namespace moonshine::capture {

DxgiDesktopDuplicator::DxgiDesktopDuplicator(uint32_t adapter_index, uint32_t output_index)
    : m_adapter_index(adapter_index)
    , m_output_index(output_index)
{
}

DxgiDesktopDuplicator::~DxgiDesktopDuplicator() {
    cleanup();
}

bool DxgiDesktopDuplicator::initialize() {
#if defined(_WIN32)
    cleanup();

    // 1. Create DXGI Factory
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return false;

    // 2. Enumerate target adapter
    ComPtr<IDXGIAdapter1> adapter;
    hr = factory->EnumAdapters1(m_adapter_index, &adapter);
    if (FAILED(hr)) return false;

    // 3. Create Direct3D 11 Device
    D3D_FEATURE_LEVEL featureLevels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL featureLevel;
    UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;

    hr = D3D11CreateDevice(
        adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        creationFlags,
        featureLevels,
        static_cast<UINT>(std::size(featureLevels)),
        D3D11_SDK_VERSION,
        &m_device,
        &featureLevel,
        &m_context
    );

    if (FAILED(hr)) {
        // Fallback to default hardware driver
        hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            creationFlags,
            featureLevels,
            static_cast<UINT>(std::size(featureLevels)),
            D3D11_SDK_VERSION,
            &m_device,
            &featureLevel,
            &m_context
        );
        if (FAILED(hr)) return false;
    }

    // 4. Enumerate target display output
    ComPtr<IDXGIOutput> output;
    hr = adapter->EnumOutputs(m_output_index, &output);
    if (FAILED(hr)) {
        // Fallback to primary output of default adapter
        ComPtr<IDXGIAdapter> defaultAdapter;
        hr = factory->EnumAdapters(0, &defaultAdapter);
        if (FAILED(hr) || FAILED(defaultAdapter->EnumOutputs(0, &output))) {
            return false;
        }
    }

    ComPtr<IDXGIOutput1> output1;
    hr = output.As(&output1);
    if (FAILED(hr)) return false;

    // 5. Initialize Output Duplication session
    hr = output1->DuplicateOutput(m_device.Get(), &m_duplication);
    if (FAILED(hr)) return false;

    DXGI_OUTDUPL_DESC duplDesc;
    m_duplication->GetDesc(&duplDesc);
    m_width = duplDesc.ModeDesc.Width;
    m_height = duplDesc.ModeDesc.Height;

    if (m_width == 0) m_width = 1920;
    if (m_height == 0) m_height = 1080;

    // 6. Create shared destination texture for zero-copy encoder handoff
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = m_width;
    texDesc.Height = m_height;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = duplDesc.ModeDesc.Format != DXGI_FORMAT_UNKNOWN ? duplDesc.ModeDesc.Format : DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.SampleDesc.Quality = 0;
    texDesc.Usage = D3D11_USAGE_DEFAULT;
    texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    texDesc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

    hr = m_device->CreateTexture2D(&texDesc, nullptr, &m_shared_texture);
    if (FAILED(hr)) return false;

    ComPtr<IDXGIResource> dxgiResource;
    hr = m_shared_texture.As(&dxgiResource);
    if (SUCCEEDED(hr)) {
        dxgiResource->GetSharedHandle(&m_shared_handle);
    }

    m_initialized = true;
    return true;
#else
    m_width = 1920;
    m_height = 1080;
    m_initialized = true;
    return true;
#endif
}

void DxgiDesktopDuplicator::cleanup() {
    release_frame();
#if defined(_WIN32)
    m_shared_handle = nullptr;
    m_shared_texture.Reset();
    m_staging_texture.Reset();
    m_duplication.Reset();
    m_context.Reset();
    m_device.Reset();
#endif
    m_initialized = false;
}

bool DxgiDesktopDuplicator::acquire_frame(uint32_t timeout_ms, CaptureFrame& out_frame) {
    if (!m_initialized) {
        if (!initialize()) return false;
    }

    release_frame();

#if defined(_WIN32)
    if (!m_duplication) return false;

    DXGI_OUTDUPL_FRAME_INFO frameInfo = {};
    ComPtr<IDXGIResource> desktopResource;

    HRESULT hr = m_duplication->AcquireNextFrame(timeout_ms, &frameInfo, &desktopResource);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
        return false;
    }

    if (FAILED(hr)) {
        if (hr == DXGI_ERROR_ACCESS_LOST || hr == DXGI_ERROR_INVALID_CALL) {
            cleanup();
            initialize();
        }
        return false;
    }

    m_frame_acquired = true;

    ComPtr<ID3D11Texture2D> acquiredTexture;
    hr = desktopResource.As(&acquiredTexture);
    if (FAILED(hr)) {
        release_frame();
        return false;
    }

    if (m_shared_texture && m_context) {
        m_context->CopyResource(m_shared_texture.Get(), acquiredTexture.Get());
    }

    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);

    out_frame.texture_handle = m_shared_texture ? m_shared_texture.Get() : acquiredTexture.Get();
    out_frame.width = m_width;
    out_frame.height = m_height;
    out_frame.format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
    out_frame.timestamp_qpc = static_cast<uint64_t>(qpc.QuadPart);
    out_frame.accumulated_frames = frameInfo.AccumulatedFrames;
    out_frame.cursor_visible = frameInfo.PointerPosition.Visible != 0;

    return true;
#else
    (void)timeout_ms;
    (void)m_adapter_index;
    (void)m_output_index;
    m_mock_frame_counter++;
    auto now = std::chrono::steady_clock::now().time_since_epoch();
    uint64_t micros = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(now).count());

    out_frame.texture_handle = reinterpret_cast<void*>(0xCAFEBABE);
    out_frame.width = m_width;
    out_frame.height = m_height;
    out_frame.format = 87;
    out_frame.timestamp_qpc = micros;
    out_frame.accumulated_frames = 1;
    out_frame.cursor_visible = true;

    m_frame_acquired = true;
    return true;
#endif
}

void DxgiDesktopDuplicator::release_frame() {
#if defined(_WIN32)
    if (m_frame_acquired && m_duplication) {
        m_duplication->ReleaseFrame();
    }
#endif
    m_frame_acquired = false;
}

} // namespace moonshine::capture
