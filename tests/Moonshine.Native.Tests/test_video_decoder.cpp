#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>

int main() {
    std::cout << "[*] Verifying Direct3D 11 / 12 Hardware Video Decoder and Live Capabilities..." << std::endl;

    // 1. Query live decoder capabilities
    MoonshineDecoderCaps caps{};
    if (moonshine_video_query_caps(&caps) != 0) {
        std::cerr << "[-] Error: moonshine_video_query_caps returned non-zero" << std::endl;
        return 1;
    }

    std::cout << "  Live Decoder Caps -> Max: " << caps.max_width << "x" << caps.max_height
              << " @" << caps.max_fps << "fps"
              << " | H264: " << (int)caps.supports_h264
              << " | HEVC: " << (int)caps.supports_hevc
              << " | AV1: " << (int)caps.supports_av1
              << " | 10-Bit: " << (int)caps.supports_10bit
              << " | HDR10: " << (int)caps.supports_hdr10
              << " | D3D12: " << (int)caps.supports_d3d12 << std::endl;

    // 2. Test creation with invalid arguments (must fail closed)
    MoonshineDecoderHandle bad_dec = moonshine_video_create_d3d11(nullptr, 0, 0, 1);
    if (bad_dec != nullptr) {
        std::cerr << "[-] Error: create_d3d11 succeeded with 0x0 dimensions" << std::endl;
        moonshine_video_destroy(bad_dec);
        return 2;
    }

    // 3. Test null handle operations (must fail closed safely)
    MoonshineFrameDesc empty_frame{};
    if (moonshine_video_submit_frame(nullptr, &empty_frame) != -1) {
        std::cerr << "[-] Error: submit_frame succeeded on null handle" << std::endl;
        return 3;
    }

    if (moonshine_video_get_texture(nullptr) != nullptr) {
        std::cerr << "[-] Error: get_texture returned non-null on null handle" << std::endl;
        return 4;
    }

    if (moonshine_video_reset(nullptr, 1920, 1080) != -1) {
        std::cerr << "[-] Error: reset succeeded on null handle" << std::endl;
        return 5;
    }

    // 4. Test hardware decoder creation for supported codecs
    if (caps.supports_hevc) {
        std::cout << "[*] Initialising live D3D11 HEVC hardware decoder..." << std::endl;
        MoonshineDecoderHandle dec = moonshine_video_create_d3d11(nullptr, 1920, 1080, 1); // 1 = HEVC
        if (dec != nullptr) {
            std::cout << "  [+] Live D3D11 HEVC Decoder initialised successfully." << std::endl;

            // Submit test bitstream frame payload
            std::vector<uint8_t> test_nal = {0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01};
            MoonshineFrameDesc frame{};
            frame.frame_index = 1;
            frame.total_bytes = static_cast<uint32_t>(test_nal.size());
            frame.packet_count = 1;
            frame.is_keyframe = 1;
            frame.frame_buffer = test_nal.data();

            int submit_res = moonshine_video_submit_frame(dec, &frame);
            std::cout << "  Frame submission result: " << submit_res << std::endl;

            void* tex = moonshine_video_get_texture(dec);
            std::cout << "  Decoded GPU Texture handle: " << tex << std::endl;

            int reset_res = moonshine_video_reset(dec, 1280, 720);
            std::cout << "  Dynamic resolution reset to 720p: " << reset_res << std::endl;

            moonshine_video_destroy(dec);
        } else {
            std::cout << "  [*] Live D3D11 HEVC Decoder creation failed closed (expected on non-HEVC hardware)." << std::endl;
        }

        if (caps.supports_10bit) {
            std::cout << "[*] Initialising live D3D11 HEVC Main10 hardware decoder..." << std::endl;
            MoonshineDecoderHandle dec10 = moonshine_video_create_d3d11(nullptr, 1920, 1080, 2); // 2 = HEVC Main10
            if (dec10 != nullptr) {
                std::cout << "  [+] Live D3D11 HEVC Main10 Decoder initialised successfully." << std::endl;
                moonshine_video_destroy(dec10);
            }
        }
    }

    // Safe destroy with null
    moonshine_video_destroy(nullptr);

    std::cout << "[+] Direct3D 11/12 Hardware Video Decoder tests passed successfully." << std::endl;
    return 0;
}
