#include "moonshine/color/d3d_color_converter.hpp"

#if defined(_WIN32)
#include <d3dcompiler.h>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")
#endif

namespace moonshine::color {

D3DColorConverter::D3DColorConverter(uint32_t width, uint32_t height, uint32_t in_format, uint32_t out_format)
    : m_width(width > 0 ? width : 1920)
    , m_height(height > 0 ? height : 1080)
    , m_in_format(in_format)
    , m_out_format(out_format)
{
}

D3DColorConverter::~D3DColorConverter() {
    cleanup();
}

bool D3DColorConverter::initialize(void* d3d11_device) {
    cleanup();

#if defined(_WIN32)
    if (d3d11_device) {
        m_device = static_cast<ID3D11Device*>(d3d11_device);
        m_device->GetImmediateContext(&m_context);
    } else {
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
            &m_device,
            &featureLevel,
            &m_context
        );

        if (FAILED(hr)) {
            hr = D3D11CreateDevice(
                nullptr,
                D3D_DRIVER_TYPE_WARP,
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
    }

    m_initialized = true;
    return true;
#else
    (void)d3d11_device;
    m_initialized = true;
    return true;
#endif
}

void D3DColorConverter::cleanup() {
#if defined(_WIN32)
    m_constant_buffer.Reset();
    m_compute_shader.Reset();
    m_context.Reset();
    m_device.Reset();
#endif
    m_initialized = false;
}

bool D3DColorConverter::convert(void* in_texture, void* out_texture) {
    if (!m_initialized || !in_texture || !out_texture) {
        return false;
    }

#if defined(_WIN32)
    if (m_context) {
        auto* srcRes = static_cast<ID3D11Resource*>(in_texture);
        auto* dstRes = static_cast<ID3D11Resource*>(out_texture);
        m_context->CopyResource(dstRes, srcRes);
        return true;
    }
    return false;
#else
    (void)in_texture;
    (void)out_texture;
    return true;
#endif
}

} // namespace moonshine::color
