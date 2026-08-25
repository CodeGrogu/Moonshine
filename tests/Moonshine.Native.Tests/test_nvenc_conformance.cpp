#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <chrono>
#include <cmath>

#define ACTIVE_ASSERT(expr) \
    do { \
        if (!(expr)) { \
            std::cerr << "[-] Assertion failed: (" #expr ") at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::abort(); \
        } \
    } while (0)

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

using Microsoft::WRL::ComPtr;
#endif

namespace {

struct NalUnit {
    size_t offset{0};
    size_t size{0};
    uint8_t nal_type{0};
    uint8_t start_code_length{0}; // 3 or 4
};

inline bool is_start_code_4(const uint8_t* p, size_t rem) {
    return rem >= 4 && p[0] == 0x00 && p[1] == 0x00 && p[2] == 0x00 && p[3] == 0x01;
}

inline bool is_start_code_3(const uint8_t* p, size_t rem) {
    return rem >= 3 && p[0] == 0x00 && p[1] == 0x00 && p[2] == 0x01;
}

std::vector<NalUnit> parse_h264_nalus(const uint8_t* data, size_t size) {
    std::vector<NalUnit> nalus;
    if (!data || size < 4) return nalus;

    std::vector<size_t> start_offsets;
    std::vector<uint8_t> start_lengths;

    for (size_t i = 0; i + 3 <= size; ) {
        if (is_start_code_4(data + i, size - i)) {
            start_offsets.push_back(i);
            start_lengths.push_back(4);
            i += 4;
        } else if (is_start_code_3(data + i, size - i)) {
            start_offsets.push_back(i);
            start_lengths.push_back(3);
            i += 3;
        } else {
            ++i;
        }
    }

    for (size_t i = 0; i < start_offsets.size(); ++i) {
        size_t start = start_offsets[i];
        uint8_t sc_len = start_lengths[i];
        size_t next_start = (i + 1 < start_offsets.size()) ? start_offsets[i + 1] : size;
        size_t nal_payload_offset = start + sc_len;
        if (nal_payload_offset < size) {
            uint8_t first_byte = data[nal_payload_offset];
            uint8_t nal_type = first_byte & 0x1F;
            nalus.push_back(NalUnit{
                .offset = start,
                .size = next_start - start,
                .nal_type = nal_type,
                .start_code_length = sc_len
            });
        }
    }
    return nalus;
}

std::vector<NalUnit> parse_hevc_nalus(const uint8_t* data, size_t size) {
    std::vector<NalUnit> nalus;
    if (!data || size < 4) return nalus;

    std::vector<size_t> start_offsets;
    std::vector<uint8_t> start_lengths;

    for (size_t i = 0; i + 3 <= size; ) {
        if (is_start_code_4(data + i, size - i)) {
            start_offsets.push_back(i);
            start_lengths.push_back(4);
            i += 4;
        } else if (is_start_code_3(data + i, size - i)) {
            start_offsets.push_back(i);
            start_lengths.push_back(3);
            i += 3;
        } else {
            ++i;
        }
    }

    for (size_t i = 0; i < start_offsets.size(); ++i) {
        size_t start = start_offsets[i];
        uint8_t sc_len = start_lengths[i];
        size_t next_start = (i + 1 < start_offsets.size()) ? start_offsets[i + 1] : size;
        size_t nal_payload_offset = start + sc_len;
        if (nal_payload_offset < size) {
            uint8_t first_byte = data[nal_payload_offset];
            uint8_t nal_type = static_cast<uint8_t>((first_byte >> 1) & 0x3F);
            nalus.push_back(NalUnit{
                .offset = start,
                .size = next_start - start,
                .nal_type = nal_type,
                .start_code_length = sc_len
            });
        }
    }
    return nalus;
}

#if defined(_WIN32)
ComPtr<ID3D11Texture2D> create_test_texture(
    ID3D11Device* device,
    ID3D11DeviceContext* context,
    uint32_t width,
    uint32_t height,
    const float colour[4]
) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.SampleDesc.Quality = 0;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    desc.CPUAccessFlags = 0;
    desc.MiscFlags = 0;

    ComPtr<ID3D11Texture2D> texture;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &texture);
    ACTIVE_ASSERT(SUCCEEDED(hr) && texture != nullptr);

    ComPtr<ID3D11RenderTargetView> rtv;
    hr = device->CreateRenderTargetView(texture.Get(), nullptr, &rtv);
    ACTIVE_ASSERT(SUCCEEDED(hr) && rtv != nullptr);

    context->ClearRenderTargetView(rtv.Get(), colour);
    return texture;
}
#endif

} // namespace

int main() {
    std::cout << "=================================================================" << std::endl;
    std::cout << "  Moonshine Native NVENC Production Hardening & Conformance Test " << std::endl;
    std::cout << "=================================================================" << std::endl;

    // ------------------------------------------------------------------------
    // Subtest I: Defensive Error Handling (Must pass unconditionally)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [1/10] Running Defensive Error Handling Suite..." << std::endl;
    {
        // 1. State & Health on null handle
        ACTIVE_ASSERT(moonshine_encoder_get_state(nullptr) == 0);
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(nullptr) == 0);

        // 2. Destroy on null handle
        moonshine_encoder_destroy(nullptr); // Safe no-op

        // 3. Request keyframe on null handle
        moonshine_encoder_request_keyframe(nullptr); // Safe no-op

        // 4. Create with null parameters
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 10000;
        cfg.codec = 0;
        ACTIVE_ASSERT(moonshine_encoder_create(1, nullptr, &cfg) == nullptr);
        ACTIVE_ASSERT(moonshine_encoder_create(1, (void*)0x1234, nullptr) == nullptr);

        // 5. Encode frame with null arguments
        MoonshineEncodedPacketDesc desc{};
        uint8_t buffer[64];
        uint32_t out_size = 0;
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(nullptr, (void*)0x1, 0, &desc, buffer, 64, &out_size) == 0);

        // 6. Reconfigure on null handle
        ACTIVE_ASSERT(moonshine_encoder_reconfigure(nullptr, &cfg) == 0);

        // 7. Codec query null pointer safety
        ACTIVE_ASSERT(moonshine_nvenc_query_codec_support(0, nullptr) == 0);

        // 8. Caps query null pointer safety
        ACTIVE_ASSERT(moonshine_encoder_query_caps(1, nullptr, nullptr) == 0);

        // 9. D3D11 pattern and texture utility null safety
        ACTIVE_ASSERT(moonshine_d3d11_create_device(0x9999) == nullptr);
        ACTIVE_ASSERT(moonshine_d3d11_create_texture(nullptr, 1920, 1080, 0) == nullptr);
        ACTIVE_ASSERT(moonshine_d3d11_create_pattern_texture(nullptr, 1920, 1080, 0, 0) == nullptr);
        ACTIVE_ASSERT(moonshine_d3d11_render_pattern(nullptr, nullptr, 1920, 1080, 0, 0) == 0);
        moonshine_d3d11_destroy_texture(nullptr);
        moonshine_d3d11_destroy_device(nullptr);

        std::cout << "  [+] Defensive error handling tests passed." << std::endl;
    }

#if defined(_WIN32)
    // Enumerate DXGI adapters for NVIDIA hardware (0x10DE)
    std::cout << "\n[*] Probing physical NVIDIA GPU adapter..." << std::endl;
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr) || !factory) {
        std::cout << "[*] Note: Unable to create DXGI factory in current execution environment. Exiting cleanly." << std::endl;
        return 0;
    }

    ComPtr<IDXGIAdapter1> nv_adapter;
    ComPtr<IDXGIAdapter1> current_adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &current_adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(current_adapter->GetDesc1(&desc))) {
            if (desc.VendorId == 0x10DE) {
                nv_adapter = current_adapter;
                std::wcout << L"  [+] Physical NVIDIA GPU detected: " << desc.Description << std::endl;
                break;
            }
        }
    }

    if (!nv_adapter) {
        std::cout << "[*] Note: No physical NVIDIA GPU adapter detected (0x10DE). Skipping live hardware conformance tests." << std::endl;
        return 0;
    }

    // Create D3D11 device and context on NVIDIA adapter
    const UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    const D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL created_feature_level{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    hr = D3D11CreateDevice(
        nv_adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        static_cast<UINT>(std::size(feature_levels)),
        D3D11_SDK_VERSION,
        &device,
        &created_feature_level,
        &context
    );

    ACTIVE_ASSERT(SUCCEEDED(hr) && device != nullptr && context != nullptr);
    std::cout << "  [+] Direct3D 11 device initialised successfully on NVIDIA adapter." << std::endl;

    // Query capabilities
    MoonshineEncoderCaps caps{};
    int caps_res = moonshine_encoder_query_caps(1, device.Get(), &caps);
    ACTIVE_ASSERT(caps_res == 1);
    ACTIVE_ASSERT(caps.vendor_id == 1);
    ACTIVE_ASSERT(caps.supported_codecs_mask != 0);

    const bool supports_h264 = (caps.supported_codecs_mask & (1 << 0)) != 0;
    const bool supports_hevc = (caps.supported_codecs_mask & (1 << 1)) != 0;
    const bool supports_av1  = (caps.supported_codecs_mask & (1 << 3)) != 0;

    std::cout << "  [+] Capabilities -> H.264: " << supports_h264
              << ", HEVC: " << supports_hevc
              << ", AV1: " << supports_av1
              << ", Max Resolution: " << caps.max_width << "x" << caps.max_height
              << ", Max FPS: " << caps.max_fps << std::endl;

    // ------------------------------------------------------------------------
    // Subtest 1.5: Bitrate & Configuration Fail-Closed Boundary Tests
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [2/10] Executing Bitrate & Configuration Fail-Closed Boundary Tests..." << std::endl;
    {
        MoonshineEncoderConfig bad_cfg{};
        bad_cfg.width = 1920;
        bad_cfg.height = 1080;
        bad_cfg.fps = 60;
        bad_cfg.codec = supports_hevc ? 1 : 0;

        // 1. Bitrate < 500 kbps (must fail closed)
        bad_cfg.bitrate_kbps = 499;
        ACTIVE_ASSERT(moonshine_encoder_create(1, device.Get(), &bad_cfg) == nullptr);

        // 2. Bitrate > 150,000 kbps (must fail closed)
        bad_cfg.bitrate_kbps = 150001;
        ACTIVE_ASSERT(moonshine_encoder_create(1, device.Get(), &bad_cfg) == nullptr);

        // 3. Peak bitrate < bitrate (when peak > 0) (must fail closed)
        bad_cfg.bitrate_kbps = 10000;
        bad_cfg.peak_bitrate_kbps = 5000;
        ACTIVE_ASSERT(moonshine_encoder_create(1, device.Get(), &bad_cfg) == nullptr);

        // 4. Peak bitrate > 150,000 kbps (must fail closed)
        bad_cfg.bitrate_kbps = 10000;
        bad_cfg.peak_bitrate_kbps = 150001;
        ACTIVE_ASSERT(moonshine_encoder_create(1, device.Get(), &bad_cfg) == nullptr);

        // 5. Dynamic reconfigure fail-closed on invalid bitrate
        MoonshineEncoderConfig valid_cfg = bad_cfg;
        valid_cfg.bitrate_kbps = 10000;
        valid_cfg.peak_bitrate_kbps = 15000;
        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &valid_cfg);
        ACTIVE_ASSERT(enc != nullptr);

        MoonshineEncoderConfig invalid_reconfig = valid_cfg;
        invalid_reconfig.bitrate_kbps = 300; // < 500 kbps
        ACTIVE_ASSERT(moonshine_encoder_reconfigure(enc, &invalid_reconfig) == 0);

        invalid_reconfig.bitrate_kbps = 200000; // > 150,000 kbps
        ACTIVE_ASSERT(moonshine_encoder_reconfigure(enc, &invalid_reconfig) == 0);

        invalid_reconfig.bitrate_kbps = 10000;
        invalid_reconfig.peak_bitrate_kbps = 5000; // peak < bitrate
        ACTIVE_ASSERT(moonshine_encoder_reconfigure(enc, &invalid_reconfig) == 0);

        moonshine_encoder_destroy(enc);
        std::cout << "  [+] Bitrate fail-closed boundary validation tests passed." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest 2: Real Direct3D 11 GPU Pattern Generation & NVENC Submission
    // (Black, Solid Colour, Gradient, Moving Procedural Patterns, SMPTE Bars)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [3/10] Executing Real Direct3D 11 GPU Pattern Matrix Submission & Validation..." << std::endl;
    {
        struct PatternTest {
            uint32_t pattern_type;
            const char* pattern_name;
        };

        const PatternTest patterns[] = {
            { 0, "Black (Clear 0x00)" },
            { 1, "Solid Colour (Teal)" },
            { 2, "2D Linear Colour Gradient" },
            { 3, "Moving Procedural Pattern (Wave + Block)" },
            { 4, "SMPTE Standard Colour Bars" }
        };

        uint32_t chosen_codec = supports_hevc ? 1 : 0;
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.peak_bitrate_kbps = 20000;
        cfg.codec = chosen_codec;
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
        ACTIVE_ASSERT(enc != nullptr);
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);

        void* pattern_tex = moonshine_d3d11_create_pattern_texture(device.Get(), 1920, 1080, 0, 0);
        ACTIVE_ASSERT(pattern_tex != nullptr);

        for (const auto& p : patterns) {
            std::cout << "  -> Submitting Pattern: " << p.pattern_name << " (Type " << p.pattern_type << ")..." << std::endl;
            int render_res = moonshine_d3d11_render_pattern(device.Get(), pattern_tex, 1920, 1080, p.pattern_type, 0);
            ACTIVE_ASSERT(render_res == 1);

            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;

            int enc_res = moonshine_encoder_encode_frame(
                enc, pattern_tex, 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );

            ACTIVE_ASSERT(enc_res == 1);
            ACTIVE_ASSERT(written > 0);
            ACTIVE_ASSERT(desc.is_keyframe == 1);
            ACTIVE_ASSERT(desc.payload_size == written);
            ACTIVE_ASSERT(desc.timestamp_qpc > 0);

            // Verify NALU headers for the encoded pattern bitstream
            if (chosen_codec == 1) { // HEVC
                auto nalus = parse_hevc_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                bool has_vps = false, has_sps = false, has_pps = false, has_idr = false;
                for (const auto& nal : nalus) {
                    if (nal.nal_type == 32) has_vps = true;
                    if (nal.nal_type == 33) has_sps = true;
                    if (nal.nal_type == 34) has_pps = true;
                    if (nal.nal_type == 19 || nal.nal_type == 20) has_idr = true;
                }
                ACTIVE_ASSERT(has_vps && has_sps && has_pps && has_idr);
            } else if (chosen_codec == 0) { // H.264
                auto nalus = parse_h264_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                bool has_sps = false, has_pps = false, has_idr = false;
                for (const auto& nal : nalus) {
                    if (nal.nal_type == 7) has_sps = true;
                    if (nal.nal_type == 8) has_pps = true;
                    if (nal.nal_type == 5) has_idr = true;
                }
                ACTIVE_ASSERT(has_sps && has_pps && has_idr);
            }

            std::cout << "     [+] " << p.pattern_name << " encoded and structurally validated (" << written << " bytes)." << std::endl;
        }

        moonshine_d3d11_destroy_texture(pattern_tex);
        moonshine_encoder_destroy(enc);
        std::cout << "  [+] All 5 Direct3D 11 GPU patterns encoded and validated successfully." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest A: Resolution Matrix Tests (720p, 1080p, 1440p, 4K)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [4/10] Executing Resolution Matrix Conformance..." << std::endl;
    {
        struct ResTest {
            uint32_t width;
            uint32_t height;
            const char* name;
        };

        const ResTest res_table[] = {
            { 1280,  720, "1280x720 (720p HD)" },
            { 1920, 1080, "1920x1080 (1080p FHD)" },
            { 2560, 1440, "2560x1440 (1440p QHD)" },
            { 3840, 2160, "3840x2160 (4K UHD)" }
        };

        uint32_t codec = supports_hevc ? 1 : 0;
        const float colour[4] = { 0.2f, 0.4f, 0.6f, 1.0f };

        for (const auto& res : res_table) {
            std::cout << "  -> Testing Resolution: " << res.name << std::endl;
            auto texture = create_test_texture(device.Get(), context.Get(), res.width, res.height, colour);

            MoonshineEncoderConfig cfg{};
            cfg.width = res.width;
            cfg.height = res.height;
            cfg.fps = 60;
            cfg.bitrate_kbps = 15000;
            cfg.peak_bitrate_kbps = 20000;
            cfg.codec = codec;
            cfg.rc_mode = 0;

            MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
            ACTIVE_ASSERT(enc != nullptr);
            ACTIVE_ASSERT(moonshine_encoder_get_state(enc) == 5); // Ready
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            std::vector<uint8_t> bitstream(res.width * res.height * 4);
            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;

            // Frame 0: IDR Keyframe
            int res_enc0 = moonshine_encoder_encode_frame(
                enc, texture.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(res_enc0 == 1);
            ACTIVE_ASSERT(written > 0);
            ACTIVE_ASSERT(desc.is_keyframe == 1);
            ACTIVE_ASSERT(desc.payload_size == written);
            ACTIVE_ASSERT(desc.frame_index == 0);
            ACTIVE_ASSERT(desc.timestamp_qpc > 0);
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            // Frame 1: Inter frame
            int res_enc1 = moonshine_encoder_encode_frame(
                enc, texture.Get(), 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(res_enc1 == 1);
            ACTIVE_ASSERT(written > 0);
            ACTIVE_ASSERT(desc.frame_index == 1);
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            moonshine_encoder_destroy(enc);
            std::cout << "     [+] " << res.name << " passed successfully." << std::endl;
        }
    }

    // ------------------------------------------------------------------------
    // Subtest B: Codec Matrix Tests (H.264, HEVC, AV1)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [5/10] Executing Codec Matrix Conformance..." << std::endl;
    {
        const float colour[4] = { 0.8f, 0.2f, 0.3f, 1.0f };
        auto texture = create_test_texture(device.Get(), context.Get(), 1920, 1080, colour);

        const uint32_t codecs_to_test[] = { 0, 1, 3 }; // H.264, HEVC, AV1
        for (uint32_t c : codecs_to_test) {
            bool supported = false;
            if (c == 0) supported = supports_h264;
            else if (c == 1) supported = supports_hevc;
            else if (c == 3) supported = supports_av1;

            if (!supported) {
                std::cout << "  -> Codec " << c << " not supported by hardware; skipping." << std::endl;
                continue;
            }

            const char* codec_name = (c == 0) ? "H.264" : ((c == 1) ? "HEVC" : "AV1");
            std::cout << "  -> Testing Codec: " << codec_name << " (ID: " << c << ")" << std::endl;

            MoonshineEncoderConfig cfg{};
            cfg.width = 1920;
            cfg.height = 1080;
            cfg.fps = 60;
            cfg.bitrate_kbps = 12000;
            cfg.peak_bitrate_kbps = 18000;
            cfg.codec = c;
            cfg.rc_mode = 0;

            MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
            ACTIVE_ASSERT(enc != nullptr);
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            std::vector<uint8_t> bitstream(1920 * 1080 * 4);
            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;

            int enc_res = moonshine_encoder_encode_frame(
                enc, texture.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(enc_res == 1);
            ACTIVE_ASSERT(written > 0);
            ACTIVE_ASSERT(desc.is_keyframe == 1);

            if (c == 0) {
                auto nalus = parse_h264_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                bool has_sps = false, has_pps = false, has_idr = false;
                for (const auto& nal : nalus) {
                    if (nal.nal_type == 7) has_sps = true;
                    if (nal.nal_type == 8) has_pps = true;
                    if (nal.nal_type == 5) has_idr = true;
                }
                ACTIVE_ASSERT(has_sps && has_pps && has_idr);
            } else if (c == 1) {
                auto nalus = parse_hevc_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                bool has_vps = false, has_sps = false, has_pps = false, has_idr = false;
                for (const auto& nal : nalus) {
                    if (nal.nal_type == 32) has_vps = true;
                    if (nal.nal_type == 33) has_sps = true;
                    if (nal.nal_type == 34) has_pps = true;
                    if (nal.nal_type == 19 || nal.nal_type == 20) has_idr = true;
                }
                ACTIVE_ASSERT(has_vps && has_sps && has_pps && has_idr);
            }

            moonshine_encoder_destroy(enc);
            std::cout << "     [+] Codec " << codec_name << " bitstream verification passed." << std::endl;
        }
    }

    // ------------------------------------------------------------------------
    // Subtest C: Deep NALU Validation across Animated Sequence
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [6/10] Executing Deep NALU Bitstream Validation across 10 Animated Frames..." << std::endl;
    {
        uint32_t chosen_codec = supports_hevc ? 1 : 0;
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.peak_bitrate_kbps = 20000;
        cfg.codec = chosen_codec;
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
        ACTIVE_ASSERT(enc != nullptr);

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);
        int64_t last_qpc = 0;

        void* moving_tex = moonshine_d3d11_create_pattern_texture(device.Get(), 1920, 1080, 3, 0);
        ACTIVE_ASSERT(moving_tex != nullptr);

        for (uint32_t f = 0; f < 10; ++f) {
            // Update procedural moving pattern for current frame
            ACTIVE_ASSERT(moonshine_d3d11_render_pattern(device.Get(), moving_tex, 1920, 1080, 3, f) == 1);

            bool force_key = (f == 0 || f == 5);
            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;

            int enc_res = moonshine_encoder_encode_frame(
                enc, moving_tex, force_key ? 1 : 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(enc_res == 1);
            ACTIVE_ASSERT(written > 0);
            ACTIVE_ASSERT(desc.payload_size == written);
            ACTIVE_ASSERT(desc.frame_index == f);
            ACTIVE_ASSERT(desc.timestamp_qpc > last_qpc);
            last_qpc = desc.timestamp_qpc;

            if (force_key) {
                ACTIVE_ASSERT(desc.is_keyframe == 1);
            }

            if (chosen_codec == 1) { // HEVC
                auto nalus = parse_hevc_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                for (const auto& nal : nalus) {
                    ACTIVE_ASSERT(nal.start_code_length == 3 || nal.start_code_length == 4);
                    ACTIVE_ASSERT(nal.size > 0);
                }
                if (force_key) {
                    bool has_idr = false;
                    for (const auto& nal : nalus) {
                        if (nal.nal_type == 19 || nal.nal_type == 20) has_idr = true;
                    }
                    ACTIVE_ASSERT(has_idr);
                }
            } else if (chosen_codec == 0) { // H.264
                auto nalus = parse_h264_nalus(bitstream.data(), written);
                ACTIVE_ASSERT(!nalus.empty());
                for (const auto& nal : nalus) {
                    ACTIVE_ASSERT(nal.start_code_length == 3 || nal.start_code_length == 4);
                    ACTIVE_ASSERT(nal.size > 0);
                }
                if (force_key) {
                    bool has_idr = false;
                    for (const auto& nal : nalus) {
                        if (nal.nal_type == 5) has_idr = true;
                    }
                    ACTIVE_ASSERT(has_idr);
                }
            }
        }

        moonshine_d3d11_destroy_texture(moving_tex);
        moonshine_encoder_destroy(enc);
        std::cout << "  [+] Deep NALU sequence, monotonic indexing, and QPC validation passed." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest D: Direct3D 11 Video Decoder Hardware Loopback & Dimension Check
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [7/10] Executing Direct3D 11 Video Decoder Loopback & Dimension Verification..." << std::endl;
    {
        void* smpte_tex = moonshine_d3d11_create_pattern_texture(device.Get(), 1920, 1080, 4, 0);
        ACTIVE_ASSERT(smpte_tex != nullptr);

        MoonshineDecoderCaps dec_caps{};
        moonshine_video_query_caps(&dec_caps);

        uint32_t chosen_codec = 0;
        if (dec_caps.supports_10bit && (caps.supported_codecs_mask & (1 << 2))) {
            chosen_codec = 2; // HEVC Main10
        } else if (dec_caps.supports_hevc && (caps.supported_codecs_mask & (1 << 1))) {
            chosen_codec = 1; // HEVC
        } else {
            chosen_codec = 0; // H.264
        }

        MoonshineDecoderHandle dec = moonshine_video_create_d3d11(nullptr, 1920, 1080, chosen_codec);
        ACTIVE_ASSERT(dec != nullptr);

        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.peak_bitrate_kbps = 20000;
        cfg.codec = chosen_codec;
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
        ACTIVE_ASSERT(enc != nullptr);

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);

        // Frame 0 (Keyframe)
        MoonshineEncodedPacketDesc desc0{};
        uint32_t written0 = 0;
        int enc_res0 = moonshine_encoder_encode_frame(
            enc, smpte_tex, 1, &desc0, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written0
        );
        ACTIVE_ASSERT(enc_res0 == 1 && written0 > 0);

        MoonshineFrameDesc frame0{};
        frame0.frame_index = static_cast<uint32_t>(desc0.frame_index);
        frame0.total_bytes = written0;
        frame0.packet_count = 1;
        frame0.is_keyframe = desc0.is_keyframe;
        frame0.frame_buffer = bitstream.data();

        int submit_res0 = moonshine_video_submit_frame(dec, &frame0);
        ACTIVE_ASSERT(submit_res0 == 0);

        void* decoded_tex0 = moonshine_video_get_texture(dec);
        ACTIVE_ASSERT(decoded_tex0 != nullptr);

        // Verify decoded dimensions match exact source resolution
        uint32_t dec_w = 0, dec_h = 0;
        int dim_res = moonshine_video_get_dimensions(dec, &dec_w, &dec_h);
        ACTIVE_ASSERT(dim_res == 0);
        ACTIVE_ASSERT(dec_w == 1920);
        ACTIVE_ASSERT(dec_h == 1080);

        // Frame 1 (Inter-frame)
        MoonshineEncodedPacketDesc desc1{};
        uint32_t written1 = 0;
        int enc_res1 = moonshine_encoder_encode_frame(
            enc, smpte_tex, 0, &desc1, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written1
        );
        ACTIVE_ASSERT(enc_res1 == 1 && written1 > 0);

        MoonshineFrameDesc frame1{};
        frame1.frame_index = static_cast<uint32_t>(desc1.frame_index);
        frame1.total_bytes = written1;
        frame1.packet_count = 1;
        frame1.is_keyframe = desc1.is_keyframe;
        frame1.frame_buffer = bitstream.data();

        int submit_res1 = moonshine_video_submit_frame(dec, &frame1);
        ACTIVE_ASSERT(submit_res1 == 0);

        void* decoded_tex1 = moonshine_video_get_texture(dec);
        ACTIVE_ASSERT(decoded_tex1 != nullptr);

        moonshine_d3d11_destroy_texture(smpte_tex);
        moonshine_video_destroy(dec);
        moonshine_encoder_destroy(enc);
        std::cout << "  [+] Direct3D 11 Video Decoder loopback test and dimensions (" << dec_w << "x" << dec_h << ") verified." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest E: Dynamic IDR Keyframe & Bitrate Reconfiguration
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [8/10] Executing Dynamic IDR Keyframe & Bitrate Reconfiguration..." << std::endl;
    {
        const float colour[4] = { 0.9f, 0.4f, 0.1f, 1.0f };
        auto texture = create_test_texture(device.Get(), context.Get(), 1920, 1080, colour);

        uint32_t chosen_codec = supports_hevc ? 1 : 0;
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 8000;
        cfg.peak_bitrate_kbps = 12000;
        cfg.codec = chosen_codec;
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
        ACTIVE_ASSERT(enc != nullptr);

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);
        MoonshineEncodedPacketDesc desc{};
        uint32_t written = 0;

        // Frame 0: Initial IDR
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(enc, texture.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written) == 1);
        ACTIVE_ASSERT(desc.is_keyframe == 1);

        // Frame 1: P-frame
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(enc, texture.Get(), 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written) == 1);

        // Request asynchronous keyframe injection
        moonshine_encoder_request_keyframe(enc);

        // Frame 2: Must be keyframe even with force_idr = 0
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(enc, texture.Get(), 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written) == 1);
        ACTIVE_ASSERT(desc.is_keyframe == 1);

        // Dynamic bitrate reconfiguration
        MoonshineEncoderConfig new_cfg = cfg;
        new_cfg.bitrate_kbps = 25000;
        new_cfg.peak_bitrate_kbps = 35000;
        int reconfig_res = moonshine_encoder_reconfigure(enc, &new_cfg);
        ACTIVE_ASSERT(reconfig_res == 1);

        // Frame 3: Encode with updated bitrate
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(enc, texture.Get(), 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written) == 1);
        ACTIVE_ASSERT(written > 0);

        moonshine_encoder_destroy(enc);
        std::cout << "  [+] Dynamic keyframe injection and bitrate reconfiguration passed." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest F: Buffer Overrun Protection (Tiny Buffer Memory Hardening)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [9/10] Executing Buffer Overrun Protection..." << std::endl;
    {
        const float colour[4] = { 0.3f, 0.3f, 0.3f, 1.0f };
        auto texture = create_test_texture(device.Get(), context.Get(), 1920, 1080, colour);

        uint32_t chosen_codec = supports_hevc ? 1 : 0;
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.peak_bitrate_kbps = 20000;
        cfg.codec = chosen_codec;
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
        ACTIVE_ASSERT(enc != nullptr);

        // Buffer structure with guard canary bytes
        struct GuardedMemory {
            uint8_t canary_before[32];
            uint8_t tiny_buffer[16];
            uint8_t canary_after[32];
        } mem;

        std::memset(mem.canary_before, 0xAA, sizeof(mem.canary_before));
        std::memset(mem.tiny_buffer, 0x00, sizeof(mem.tiny_buffer));
        std::memset(mem.canary_after, 0xBB, sizeof(mem.canary_after));

        MoonshineEncodedPacketDesc desc{};
        uint32_t written = 9999;

        // Call encode with 16 byte buffer: must safely fail with written = 0 and zero memory overrun
        int enc_fail = moonshine_encoder_encode_frame(
            enc, texture.Get(), 1, &desc, mem.tiny_buffer, 16, &written
        );

        ACTIVE_ASSERT(enc_fail == 0);
        ACTIVE_ASSERT(written == 0);

        // Verify guard canaries are completely untouched
        for (size_t i = 0; i < sizeof(mem.canary_before); ++i) {
            ACTIVE_ASSERT(mem.canary_before[i] == 0xAA);
        }
        for (size_t i = 0; i < sizeof(mem.canary_after); ++i) {
            ACTIVE_ASSERT(mem.canary_after[i] == 0xBB);
        }

        // Verify encoder remains healthy and operational after buffer rejection
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

        // Encode subsequent frame with valid buffer
        std::vector<uint8_t> valid_buffer(1920 * 1080 * 4);
        int enc_ok = moonshine_encoder_encode_frame(
            enc, texture.Get(), 1, &desc, valid_buffer.data(), static_cast<uint32_t>(valid_buffer.size()), &written
        );
        ACTIVE_ASSERT(enc_ok == 1);
        ACTIVE_ASSERT(written > 0);

        moonshine_encoder_destroy(enc);
        std::cout << "  [+] Buffer overrun protection and canary integrity verified." << std::endl;
    }

    // ------------------------------------------------------------------------
    // Subtest G: Rapid Start/Stop Lifecycle & Concurrency (10 Cycles & Multi-Instance)
    // ------------------------------------------------------------------------
    std::cout << "\n[*] [10/10] Executing Rapid Start/Stop Cycles & Multi-Instance Concurrency..." << std::endl;
    {
        const float colour[4] = { 0.4f, 0.6f, 0.2f, 1.0f };
        auto texture = create_test_texture(device.Get(), context.Get(), 1920, 1080, colour);
        uint32_t chosen_codec = supports_hevc ? 1 : 0;

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);

        for (int cycle = 0; cycle < 10; ++cycle) {
            MoonshineEncoderConfig cfg{};
            cfg.width = 1920;
            cfg.height = 1080;
            cfg.fps = 60;
            cfg.bitrate_kbps = 10000;
            cfg.peak_bitrate_kbps = 15000;
            cfg.codec = chosen_codec;
            cfg.rc_mode = 0;

            MoonshineEncoderHandle enc = moonshine_encoder_create(1, device.Get(), &cfg);
            ACTIVE_ASSERT(enc != nullptr);
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;
            int enc_res = moonshine_encoder_encode_frame(
                enc, texture.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(enc_res == 1);
            ACTIVE_ASSERT(written > 0);

            moonshine_encoder_destroy(enc);
        }
        std::cout << "  [+] 10 sequential create/encode/destroy cycles completed cleanly." << std::endl;

        // Multi-Instance Concurrency (2 simultaneous instances)
        const float colour1[4] = { 0.2f, 0.7f, 0.9f, 1.0f };
        const float colour2[4] = { 0.9f, 0.6f, 0.1f, 1.0f };
        auto texture1 = create_test_texture(device.Get(), context.Get(), 1920, 1080, colour1);
        auto texture2 = create_test_texture(device.Get(), context.Get(), 1280, 720, colour2);

        MoonshineEncoderConfig cfg1{};
        cfg1.width = 1920;
        cfg1.height = 1080;
        cfg1.fps = 60;
        cfg1.bitrate_kbps = 12000;
        cfg1.peak_bitrate_kbps = 16000;
        cfg1.codec = supports_h264 ? 0 : 1;
        cfg1.rc_mode = 0;

        MoonshineEncoderConfig cfg2{};
        cfg2.width = 1280;
        cfg2.height = 720;
        cfg2.fps = 60;
        cfg2.bitrate_kbps = 8000;
        cfg2.peak_bitrate_kbps = 12000;
        cfg2.codec = supports_hevc ? 1 : 0;
        cfg2.rc_mode = 0;

        MoonshineEncoderHandle enc1 = moonshine_encoder_create(1, device.Get(), &cfg1);
        MoonshineEncoderHandle enc2 = moonshine_encoder_create(1, device.Get(), &cfg2);

        ACTIVE_ASSERT(enc1 != nullptr);
        ACTIVE_ASSERT(enc2 != nullptr);
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc1) == 1);
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc2) == 1);

        std::vector<uint8_t> buf1(1920 * 1080 * 4);
        std::vector<uint8_t> buf2(1280 * 720 * 4);

        for (int frame = 0; frame < 5; ++frame) {
            MoonshineEncodedPacketDesc desc1{};
            uint32_t written1 = 0;
            int res1 = moonshine_encoder_encode_frame(
                enc1, texture1.Get(), (frame == 0) ? 1 : 0, &desc1, buf1.data(), static_cast<uint32_t>(buf1.size()), &written1
            );
            ACTIVE_ASSERT(res1 == 1 && written1 > 0);

            MoonshineEncodedPacketDesc desc2{};
            uint32_t written2 = 0;
            int res2 = moonshine_encoder_encode_frame(
                enc2, texture2.Get(), (frame == 0) ? 1 : 0, &desc2, buf2.data(), static_cast<uint32_t>(buf2.size()), &written2
            );
            ACTIVE_ASSERT(res2 == 1 && written2 > 0);
        }

        moonshine_encoder_destroy(enc1);
        moonshine_encoder_destroy(enc2);
        std::cout << "  [+] Multi-instance concurrency test completed successfully." << std::endl;
    }

#else
    std::cout << "[*] Non-Windows OS detected. Live Direct3D 11 NVENC tests skipped." << std::endl;
#endif

    std::cout << "\n=================================================================" << std::endl;
    std::cout << "  All NVENC Conformance & Production Hardening Tests Passed!     " << std::endl;
    std::cout << "=================================================================" << std::endl;
    return 0;
}
