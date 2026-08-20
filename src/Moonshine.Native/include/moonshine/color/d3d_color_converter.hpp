#pragma once

#include <cstdint>
#include <cstddef>
#include <memory>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::color {

enum class ColorMatrix : uint32_t {
    Bt709_Sdr = 0,
    Bt2020_Hdr10 = 1
};

class D3DColorConverter {
public:
    D3DColorConverter(uint32_t width, uint32_t height, uint32_t in_format, uint32_t out_format);
    ~D3DColorConverter();

    D3DColorConverter(const D3DColorConverter&) = delete;
    D3DColorConverter& operator=(const D3DColorConverter&) = delete;

    bool initialize(void* d3d11_device = nullptr);
    void cleanup();

    bool convert(void* in_texture, void* out_texture);

    [[nodiscard]] uint32_t width() const noexcept { return m_width; }
    [[nodiscard]] uint32_t height() const noexcept { return m_height; }
    [[nodiscard]] uint32_t in_format() const noexcept { return m_in_format; }
    [[nodiscard]] uint32_t out_format() const noexcept { return m_out_format; }
    [[nodiscard]] bool is_initialized() const noexcept { return m_initialized; }

private:
    uint32_t m_width;
    uint32_t m_height;
    uint32_t m_in_format;
    uint32_t m_out_format;
    bool m_initialized = false;

#if defined(_WIN32)
    ComPtr<ID3D11Device> m_device;
    ComPtr<ID3D11DeviceContext> m_context;
    ComPtr<ID3D11ComputeShader> m_compute_shader;
    ComPtr<ID3D11Buffer> m_constant_buffer;
#endif
};

} // namespace moonshine::color
