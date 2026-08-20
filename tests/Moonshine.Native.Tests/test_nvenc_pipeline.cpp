#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>
#include <vector>

int main() {
    std::cout << "[*] Running Dedicated NVIDIA NVENC Pipeline Tests..." << std::endl;

    // 1. Query Codec Support
    {
        uint32_t supported = 0;
        
        // H.264
        int res = moonshine_nvenc_query_codec_support(0, &supported);
        (void)res;
        assert(res == 1);
        assert(supported == 1);
        std::cout << "    [+] NVENC H.264 Support: YES" << std::endl;

        // HEVC
        res = moonshine_nvenc_query_codec_support(1, &supported);
        assert(res == 1);
        assert(supported == 1);
        std::cout << "    [+] NVENC HEVC Main Support: YES" << std::endl;

        // HEVC Main10 (10-bit HDR)
        res = moonshine_nvenc_query_codec_support(2, &supported);
        assert(res == 1);
        assert(supported == 1);
        std::cout << "    [+] NVENC HEVC Main10 (HDR10) Support: YES" << std::endl;

        // AV1 Profile 0
        res = moonshine_nvenc_query_codec_support(3, &supported);
        assert(res == 1);
        assert(supported == 1);
        std::cout << "    [+] NVENC AV1 Profile 0 Support: YES" << std::endl;
    }

    // 2. Test NVENC 4K 120 FPS HEVC Main10 Pipeline
    {
        MoonshineEncoderConfig config{};
        config.width = 3840;
        config.height = 2160;
        config.fps = 120;
        config.bitrate_kbps = 50000;
        config.peak_bitrate_kbps = 75000;
        config.codec = 2; // HEVC Main10
        config.rc_mode = 0; // CBR
        config.gop_length = 0; // Infinite GOP
        config.enable_intra_refresh = 0;
        config.enable_filler_data = 1;

        MoonshineEncoderHandle handle = moonshine_encoder_create(1, nullptr, &config); // Vendor 1 = NVENC
        assert(handle != nullptr);

        // Configure Ultra-Low Latency Tuning and P1 Preset
        int tuningRes = moonshine_nvenc_set_tuning(handle, 1, 2); // P1, UltraLowLatency
        (void)tuningRes;
        assert(tuningRes == 1);

        // Configure Intra-Refresh
        int intraRes = moonshine_nvenc_set_intra_refresh(handle, 1, 60, 4);
        (void)intraRes;
        assert(intraRes == 1);

        std::vector<uint8_t> buffer(1024 * 1024);
        MoonshineEncodedPacketDesc desc{};
        uint32_t written = 0;

        // Frame 0: Keyframe
        int encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        (void)encodeRes;
        assert(encodeRes == 1);
        assert(desc.frame_index == 0);
        assert(desc.is_keyframe == 1);
        assert(desc.is_header_packet == 1);
        assert(written > 0);
        std::cout << "    [+] NVENC HEVC Main10 4K120 Frame 0 Keyframe: " << written << " bytes" << std::endl;

        // Frame 1: Inter-frame
        encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        assert(encodeRes == 1);
        assert(desc.frame_index == 1);
        assert(desc.is_keyframe == 0);
        std::cout << "    [+] NVENC HEVC Main10 4K120 Frame 1 Inter-frame: " << written << " bytes" << std::endl;

        // Dynamic Reconfiguration
        config.bitrate_kbps = 80000;
        config.fps = 144;
        int reconfRes = moonshine_encoder_reconfigure(handle, &config);
        (void)reconfRes;
        assert(reconfRes == 1);

        moonshine_encoder_destroy(handle);
    }

    // 3. Test NVENC AV1 1440p 240 FPS Pipeline
    {
        MoonshineEncoderConfig config{};
        config.width = 2560;
        config.height = 1440;
        config.fps = 240;
        config.bitrate_kbps = 40000;
        config.peak_bitrate_kbps = 60000;
        config.codec = 3; // AV1
        config.rc_mode = 0; // CBR
        config.gop_length = 0;
        config.enable_intra_refresh = 0;
        config.enable_filler_data = 1;

        MoonshineEncoderHandle handle = moonshine_encoder_create(1, nullptr, &config);
        assert(handle != nullptr);

        std::vector<uint8_t> buffer(1024 * 1024);
        MoonshineEncodedPacketDesc desc{};
        uint32_t written = 0;

        int encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        (void)encodeRes;
        assert(encodeRes == 1);
        assert(desc.is_keyframe == 1);
        assert(written > 0);
        std::cout << "    [+] NVENC AV1 1440p240 Frame 0 Keyframe: " << written << " bytes" << std::endl;

        moonshine_encoder_destroy(handle);
    }

    std::cout << "[+] Dedicated NVIDIA NVENC Pipeline Tests Passed Successfully!" << std::endl;
    return 0;
}
