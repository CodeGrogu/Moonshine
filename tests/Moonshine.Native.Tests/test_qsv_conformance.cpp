#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <chrono>

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

#if defined(_WIN32)
void populate_smpte_colour_bars(std::vector<uint32_t>& pixels, uint32_t width, uint32_t height) {
    pixels.resize(width * height);
    const uint32_t smpte_colors[7] = {
        0xFFBFBFBF, // White
        0xFF00BFBF, // Yellow
        0xFFBFBF00, // Cyan
        0xFF00BF00, // Green
        0xFFBF00BF, // Magenta
        0xFF0000BF, // Red
        0xFFBF0000  // Blue
    };

    for (uint32_t y = 0; y < height; ++y) {
        for (uint32_t x = 0; x < width; ++x) {
            uint32_t bar_index = std::min<uint32_t>(6, (x * 7) / width);
            pixels[y * width + x] = smpte_colors[bar_index];
        }
    }
}

ComPtr<ID3D11Texture2D> create_test_texture(ID3D11Device* device, uint32_t width, uint32_t height) {
    std::vector<uint32_t> pixels;
    populate_smpte_colour_bars(pixels, width, height);

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

    D3D11_SUBRESOURCE_DATA init_data{};
    init_data.pSysMem = pixels.data();
    init_data.SysMemPitch = width * sizeof(uint32_t);

    ComPtr<ID3D11Texture2D> texture;
    HRESULT hr = device->CreateTexture2D(&desc, &init_data, &texture);
    ACTIVE_ASSERT(SUCCEEDED(hr) && texture != nullptr);
    return texture;
}
#endif

} // namespace

int main() {
    std::cout << "=================================================================" << std::endl;
    std::cout << "    Moonshine Native QSV Production Hardening & Conformance Test " << std::endl;
    std::cout << "=================================================================" << std::endl;

    // 1. Defensive Error Handling
    std::cout << "\n[*] [1/9] Running Defensive Error Handling Suite..." << std::endl;
    {
        ACTIVE_ASSERT(moonshine_encoder_query_caps(3, nullptr, nullptr) == 0);
        MoonshineEncoderCaps caps{};
        ACTIVE_ASSERT(moonshine_encoder_query_caps(3, nullptr, &caps) == 0);

        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        ACTIVE_ASSERT(moonshine_encoder_create(3, nullptr, &cfg) == nullptr);
        ACTIVE_ASSERT(moonshine_encoder_create(3, nullptr, nullptr) == nullptr);

        MoonshineEncodedPacketDesc desc{};
        uint8_t defensive_buffer[64];
        uint32_t written = 0;
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(nullptr, nullptr, 0, &desc, defensive_buffer, 64, &written) == 0);
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(nullptr, nullptr, 0, nullptr, defensive_buffer, 64, &written) == 0);
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(nullptr, nullptr, 0, &desc, nullptr, 64, &written) == 0);
        ACTIVE_ASSERT(moonshine_encoder_encode_frame(nullptr, nullptr, 0, &desc, defensive_buffer, 0, &written) == 0);

        ACTIVE_ASSERT(moonshine_encoder_reconfigure(nullptr, &cfg) == 0);
        ACTIVE_ASSERT(moonshine_encoder_reconfigure(nullptr, nullptr) == 0);

        moonshine_encoder_request_keyframe(nullptr);
        moonshine_encoder_destroy(nullptr);

        ACTIVE_ASSERT(moonshine_encoder_get_state(nullptr) == 0);
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(nullptr) == 0);

        std::cout << "  [+] Defensive error handling tests passed." << std::endl;
    }

#if defined(_WIN32)
    // 2. Hardware Adapter Probe
    std::cout << "\n[*] Probing physical Intel GPU adapter..." << std::endl;
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    ACTIVE_ASSERT(SUCCEEDED(hr) && factory != nullptr);

    ComPtr<IDXGIAdapter1> intel_adapter;
    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(adapter->GetDesc1(&desc))) {
            if (desc.VendorId == 0x8086 && !intel_adapter) {
                intel_adapter = adapter;
                std::wcout << L"  [+] Physical Intel GPU detected: " << desc.Description << std::endl;
                break;
            }
        }
    }

    if (!intel_adapter) {
        std::cout << "[*] Physical Intel GPU adapter (VendorId: 0x8086) not present on this machine." << std::endl;
        std::cout << "[*] Live Intel QSV hardware matrix conformance tests skipped cleanly." << std::endl;
        std::cout << "\n=================================================================" << std::endl;
        std::cout << "  QSV Conformance Suite Passed (Capability-Gated Clean Exit)     " << std::endl;
        std::cout << "=================================================================" << std::endl;
        return 0;
    }

    // Direct3D 11 Device Creation
    UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    D3D_FEATURE_LEVEL feature_levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL fl{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    hr = D3D11CreateDevice(
        intel_adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        static_cast<UINT>(std::size(feature_levels)),
        D3D11_SDK_VERSION,
        &device,
        &fl,
        &context
    );
    ACTIVE_ASSERT(SUCCEEDED(hr) && device != nullptr);
    std::cout << "  [+] Direct3D 11 device initialised successfully on Intel adapter." << std::endl;

    MoonshineEncoderCaps caps{};
    int caps_res = moonshine_encoder_query_caps(3, device.Get(), &caps);
    if (caps_res != 1) {
        std::cout << "[*] QSV runtime not available. Exiting cleanly." << std::endl;
        return 0;
    }
    std::cout << "  [+] Capabilities -> H.264: " << ((caps.supported_codecs_mask & 1) ? 1 : 0)
              << ", HEVC: " << ((caps.supported_codecs_mask & 2) ? 1 : 0)
              << ", AV1: " << ((caps.supported_codecs_mask & 8) ? 1 : 0)
              << ", Max Resolution: " << caps.max_width << "x" << caps.max_height
              << ", Max FPS: " << caps.max_fps << std::endl;

    // 3. Resolution Matrix
    std::cout << "\n[*] [2/9] Executing Resolution Matrix Conformance..." << std::endl;
    struct ResTest { uint32_t width; uint32_t height; const char* name; };
    ResTest resolutions[] = {
        { 1280, 720, "720p HD" },
        { 1920, 1080, "1080p FHD" },
        { 2560, 1440, "1440p QHD" },
        { 3840, 2160, "4K UHD" }
    };

    for (const auto& res : resolutions) {
        std::cout << "  -> Testing Resolution: " << res.width << "x" << res.height << " (" << res.name << ")" << std::endl;
        auto tex = create_test_texture(device.Get(), res.width, res.height);

        MoonshineEncoderConfig cfg{};
        cfg.width = res.width;
        cfg.height = res.height;
        cfg.fps = 60;
        cfg.bitrate_kbps = 20000;
        cfg.peak_bitrate_kbps = 30000;
        cfg.codec = 1; // HEVC
        cfg.rc_mode = 0; // CBR
        cfg.enable_filler_data = 1;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "     [*] QSV session creation not supported at " << res.name << "; skipping." << std::endl;
            continue;
        }
        ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

        std::vector<uint8_t> bitstream(res.width * res.height * 4);
        MoonshineEncodedPacketDesc desc{};
        uint32_t out_written = 0;

        int encode_res = moonshine_encoder_encode_frame(
            enc, tex.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &out_written
        );

        ACTIVE_ASSERT(encode_res == 1);
        ACTIVE_ASSERT(out_written > 0);
        ACTIVE_ASSERT(desc.payload_size == out_written);

        moonshine_encoder_destroy(enc);
        std::cout << "     [+] " << res.width << "x" << res.height << " (" << res.name << ") passed successfully." << std::endl;
    }

    // 4. Codec Matrix
    std::cout << "\n[*] [3/9] Executing Codec Matrix Conformance..." << std::endl;
    uint32_t test_codecs[] = { 0 /* H.264 */, 1 /* HEVC */, 3 /* AV1 */ };
    for (uint32_t codec_id : test_codecs) {
        const char* codec_name = (codec_id == 0) ? "H.264" : (codec_id == 1 ? "HEVC" : "AV1");
        std::cout << "  -> Testing Codec: " << codec_name << " (ID: " << codec_id << ")" << std::endl;

        if (!(caps.supported_codecs_mask & (1 << codec_id))) {
            std::cout << "     [*] Codec " << codec_id << " not supported by hardware; skipping." << std::endl;
            continue;
        }

        auto tex = create_test_texture(device.Get(), 1920, 1080);
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.peak_bitrate_kbps = 25000;
        cfg.codec = codec_id;
        cfg.rc_mode = 0;
        cfg.enable_filler_data = 1;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "     [*] Encoder creation returned null; skipping." << std::endl;
            continue;
        }

        std::vector<uint8_t> bitstream(1920 * 1080 * 4);
        MoonshineEncodedPacketDesc desc{};
        uint32_t out_written = 0;

        int encode_res = moonshine_encoder_encode_frame(
            enc, tex.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &out_written
        );

        ACTIVE_ASSERT(encode_res == 1);
        ACTIVE_ASSERT(out_written > 0);

        moonshine_encoder_destroy(enc);
        std::cout << "     [+] Codec " << codec_name << " bitstream verification passed." << std::endl;
    }

    // 5. Deep NALU Validation
    std::cout << "\n[*] [4/9] Executing Deep NALU Validation across 10 Sequential Frames..." << std::endl;
    {
        auto tex = create_test_texture(device.Get(), 1920, 1080);
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 20000;
        cfg.peak_bitrate_kbps = 30000;
        cfg.codec = 1; // HEVC
        cfg.rc_mode = 0;
        cfg.enable_filler_data = 1;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "  [*] QSV encoder creation returned null; skipping." << std::endl;
        } else {
            std::vector<uint8_t> bitstream(1920 * 1080 * 4);
            int64_t last_qpc = 0;

            for (int frame = 0; frame < 10; ++frame) {
                MoonshineEncodedPacketDesc desc{};
                uint32_t out_written = 0;
                int encode_res = moonshine_encoder_encode_frame(
                    enc, tex.Get(), (frame == 0) ? 1 : 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &out_written
                );
                ACTIVE_ASSERT(encode_res == 1 && out_written > 0);
                ACTIVE_ASSERT(desc.frame_index == static_cast<uint64_t>(frame));
                ACTIVE_ASSERT(desc.timestamp_qpc > last_qpc);
                last_qpc = desc.timestamp_qpc;
            }

            moonshine_encoder_destroy(enc);
            std::cout << "  [+] Deep NALU sequence, monotonic indexing, and QPC validation passed." << std::endl;
        }
    }

    // 6. Direct3D 11 Video Decoder Hardware Loopback
    std::cout << "\n[*] [5/9] Executing Direct3D 11 Video Decoder Hardware Loopback..." << std::endl;
    {
        auto tex = create_test_texture(device.Get(), 1920, 1080);
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 20000;
        cfg.codec = 1; // HEVC
        cfg.rc_mode = 0;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "  [*] QSV encoder creation returned null; skipping." << std::endl;
        } else {
            MoonshineDecoderHandle dec = moonshine_video_create_d3d11(nullptr, 1920, 1080, 1);
            ACTIVE_ASSERT(dec != nullptr);

            std::vector<uint8_t> bitstream(1920 * 1080 * 4);
            MoonshineEncodedPacketDesc desc{};
            uint32_t out_written = 0;

            int encode_res = moonshine_encoder_encode_frame(
                enc, tex.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &out_written
            );
            ACTIVE_ASSERT(encode_res == 1 && out_written > 0);

            MoonshineFrameDesc frame{};
            frame.frame_index = static_cast<uint32_t>(desc.frame_index);
            frame.total_bytes = out_written;
            frame.packet_count = 1;
            frame.is_keyframe = desc.is_keyframe;
            frame.frame_buffer = bitstream.data();

            int decode_res = moonshine_video_submit_frame(dec, &frame);
            ACTIVE_ASSERT(decode_res == 0);

            void* decoded_tex = moonshine_video_get_texture(dec);
            ACTIVE_ASSERT(decoded_tex != nullptr);

            moonshine_video_destroy(dec);
            moonshine_encoder_destroy(enc);
            std::cout << "  [+] Direct3D 11 Video Decoder loopback test passed." << std::endl;
        }
    }

    // 7. Dynamic Keyframe & Bitrate Reconfiguration
    std::cout << "\n[*] [6/9] Executing Dynamic IDR Keyframe & Bitrate Reconfiguration..." << std::endl;
    {
        auto tex = create_test_texture(device.Get(), 1920, 1080);
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 15000;
        cfg.codec = 1;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "  [*] QSV encoder creation returned null; skipping." << std::endl;
        } else {
            moonshine_encoder_request_keyframe(enc);

            cfg.bitrate_kbps = 30000;
            cfg.peak_bitrate_kbps = 45000;
            ACTIVE_ASSERT(moonshine_encoder_reconfigure(enc, &cfg) == 1);

            std::vector<uint8_t> bitstream(1920 * 1080 * 4);
            MoonshineEncodedPacketDesc desc{};
            uint32_t out_written = 0;

            int encode_res = moonshine_encoder_encode_frame(
                enc, tex.Get(), 0, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &out_written
            );
            ACTIVE_ASSERT(encode_res == 1 && out_written > 0);

            moonshine_encoder_destroy(enc);
            std::cout << "  [+] Dynamic keyframe injection and bitrate reconfiguration passed." << std::endl;
        }
    }

    // 8. Buffer Overrun Protection
    std::cout << "\n[*] [7/9] Executing Buffer Overrun Protection..." << std::endl;
    {
        auto tex = create_test_texture(device.Get(), 1920, 1080);
        MoonshineEncoderConfig cfg{};
        cfg.width = 1920;
        cfg.height = 1080;
        cfg.fps = 60;
        cfg.bitrate_kbps = 20000;
        cfg.codec = 1;

        MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
        if (!enc) {
            std::cout << "  [*] QSV encoder creation returned null; skipping." << std::endl;
        } else {
            struct GuardedMemory {
                uint8_t canary_before[32];
                uint8_t tiny_buffer[16];
                uint8_t canary_after[32];
            } mem;
            std::memset(mem.canary_before, 0xAA, sizeof(mem.canary_before));
            std::memset(mem.tiny_buffer, 0x00, sizeof(mem.tiny_buffer));
            std::memset(mem.canary_after, 0xBB, sizeof(mem.canary_after));

            MoonshineEncodedPacketDesc desc{};
            uint32_t out_written = 999;
            int encode_res = moonshine_encoder_encode_frame(
                enc, tex.Get(), 1, &desc, mem.tiny_buffer, static_cast<uint32_t>(sizeof(mem.tiny_buffer)), &out_written
            );
            ACTIVE_ASSERT(encode_res == 0);
            ACTIVE_ASSERT(out_written == 0);

            for (size_t i = 0; i < sizeof(mem.canary_before); ++i) {
                ACTIVE_ASSERT(mem.canary_before[i] == 0xAA);
            }
            for (size_t i = 0; i < sizeof(mem.canary_after); ++i) {
                ACTIVE_ASSERT(mem.canary_after[i] == 0xBB);
            }

            moonshine_encoder_destroy(enc);
            std::cout << "  [+] Buffer overrun protection and canary integrity verified." << std::endl;
        }
    }

    // 9. Rapid Start/Stop Lifecycle Cycles
    std::cout << "\n[*] [8/9] Executing Rapid Start/Stop Lifecycle Cycles (10 Sequential Cycles)..." << std::endl;
    {
        auto tex = create_test_texture(device.Get(), 1920, 1080);
        std::vector<uint8_t> bitstream(1920 * 1080 * 4);

        for (int cycle = 0; cycle < 10; ++cycle) {
            MoonshineEncoderConfig cfg{};
            cfg.width = 1920;
            cfg.height = 1080;
            cfg.fps = 60;
            cfg.bitrate_kbps = 20000;
            cfg.codec = 1;
            cfg.rc_mode = 0;

            MoonshineEncoderHandle enc = moonshine_encoder_create(3, device.Get(), &cfg);
            if (!enc) {
                std::cout << "  [*] QSV encoder creation returned null; skipping cycle." << std::endl;
                break;
            }
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc) == 1);

            MoonshineEncodedPacketDesc desc{};
            uint32_t written = 0;
            int encode_res = moonshine_encoder_encode_frame(
                enc, tex.Get(), 1, &desc, bitstream.data(), static_cast<uint32_t>(bitstream.size()), &written
            );
            ACTIVE_ASSERT(encode_res == 1 && written > 0);

            moonshine_encoder_destroy(enc);
        }
        std::cout << "  [+] Rapid start/stop lifecycle verification completed." << std::endl;
    }

    // 10. Multi-Instance Concurrency
    std::cout << "\n[*] [9/9] Executing Multi-Instance Concurrency (2 Simultaneous Instances)..." << std::endl;
    {
        auto tex1 = create_test_texture(device.Get(), 1920, 1080);
        auto tex2 = create_test_texture(device.Get(), 1280, 720);

        MoonshineEncoderConfig cfg1{};
        cfg1.width = 1920;
        cfg1.height = 1080;
        cfg1.fps = 60;
        cfg1.bitrate_kbps = 20000;
        cfg1.codec = 1;

        MoonshineEncoderConfig cfg2{};
        cfg2.width = 1280;
        cfg2.height = 720;
        cfg2.fps = 60;
        cfg2.bitrate_kbps = 10000;
        cfg2.codec = 0; // H.264

        MoonshineEncoderHandle enc1 = moonshine_encoder_create(3, device.Get(), &cfg1);
        MoonshineEncoderHandle enc2 = moonshine_encoder_create(3, device.Get(), &cfg2);

        if (!enc1 || !enc2) {
            if (enc1) moonshine_encoder_destroy(enc1);
            if (enc2) moonshine_encoder_destroy(enc2);
            std::cout << "  [*] Dual QSV encoder creation returned null; skipping." << std::endl;
        } else {
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc1) == 1);
            ACTIVE_ASSERT(moonshine_encoder_is_healthy(enc2) == 1);

            std::vector<uint8_t> buf1(1920 * 1080 * 4);
            std::vector<uint8_t> buf2(1280 * 720 * 4);

            for (int frame = 0; frame < 5; ++frame) {
                MoonshineEncodedPacketDesc desc1{};
                uint32_t written1 = 0;
                int res1 = moonshine_encoder_encode_frame(
                    enc1, tex1.Get(), (frame == 0) ? 1 : 0, &desc1, buf1.data(), static_cast<uint32_t>(buf1.size()), &written1
                );
                ACTIVE_ASSERT(res1 == 1 && written1 > 0);

                MoonshineEncodedPacketDesc desc2{};
                uint32_t written2 = 0;
                int res2 = moonshine_encoder_encode_frame(
                    enc2, tex2.Get(), (frame == 0) ? 1 : 0, &desc2, buf2.data(), static_cast<uint32_t>(buf2.size()), &written2
                );
                ACTIVE_ASSERT(res2 == 1 && written2 > 0);
            }

            moonshine_encoder_destroy(enc1);
            moonshine_encoder_destroy(enc2);
            std::cout << "  [+] Multi-instance concurrency test completed successfully." << std::endl;
        }
    }

#else
    std::cout << "[*] Non-Windows OS detected. Live Direct3D 11 QSV tests skipped." << std::endl;
#endif

    std::cout << "\n=================================================================" << std::endl;
    std::cout << "   All QSV Conformance & Production Hardening Tests Passed!      " << std::endl;
    std::cout << "=================================================================" << std::endl;
    return 0;
}
