#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>
#include <vector>

int main() {
    std::cout << "[*] Running Native Hardware Video Encoder Tests..." << std::endl;

    // 1. Query Capabilities across all hardware vendors
    {
        MoonshineEncoderCaps caps{};
        
        // Auto
        int res = moonshine_encoder_query_caps(0, nullptr, &caps);
        (void)res;
        assert(res == 1);
        assert(caps.max_width >= 3840);
        assert(caps.max_fps >= 120);
        std::cout << "    [+] Auto Vendor Query Caps: Max " << caps.max_width << "x" << caps.max_height 
                  << " @ " << caps.max_fps << " FPS" << std::endl;

        // NVIDIA NVENC
        res = moonshine_encoder_query_caps(1, nullptr, &caps);
        assert(res == 1);
        assert(caps.supports_10bit == 1);
        assert(caps.vendor_id == 1);
        std::cout << "    [+] NVENC Query Caps: 10-bit HDR=" << (int)caps.supports_10bit 
                  << ", Max Bitrate=" << caps.max_bitrate_kbps << " kbps" << std::endl;

        // AMD AMF
        res = moonshine_encoder_query_caps(2, nullptr, &caps);
        assert(res == 1);
        assert(caps.vendor_id == 2);
        std::cout << "    [+] AMD AMF Query Caps: VendorID=" << (int)caps.vendor_id << std::endl;

        // Intel QuickSync
        res = moonshine_encoder_query_caps(3, nullptr, &caps);
        assert(res == 1);
        assert(caps.vendor_id == 3);
        std::cout << "    [+] Intel QuickSync Query Caps: VendorID=" << (int)caps.vendor_id << std::endl;

        // Direct3D 11 Hardware
        res = moonshine_encoder_query_caps(4, nullptr, &caps);
        assert(res == 1);
        assert(caps.vendor_id == 4);
        std::cout << "    [+] Direct3D 11 Hardware Query Caps: VendorID=" << (int)caps.vendor_id << std::endl;
    }

    // 2. Test Multi-Codec Encoding Lifecycles
    const uint32_t codecs[] = { 0, 1, 2, 3 }; // H264, HEVC, HEVC Main10, AV1
    for (uint32_t codec : codecs) {
        MoonshineEncoderConfig config{};
        config.width = 1920;
        config.height = 1080;
        config.fps = 60;
        config.bitrate_kbps = 20000;
        config.peak_bitrate_kbps = 30000;
        config.codec = codec;
        config.rc_mode = 0; // CBR
        config.gop_length = 0; // Infinite
        config.enable_intra_refresh = 0;
        config.enable_filler_data = 1;

        MoonshineEncoderHandle handle = moonshine_encoder_create(0, nullptr, &config);
        assert(handle != nullptr);

        std::vector<uint8_t> buffer(1024 * 1024);
        MoonshineEncodedPacketDesc desc{};
        uint32_t written = 0;

        // Frame 0: Expect Keyframe (IDR)
        int encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        (void)encodeRes;
        assert(encodeRes == 1);
        assert(desc.frame_index == 0);
        assert(desc.is_keyframe == 1);
        assert(desc.is_header_packet == 1);
        assert(written > 0);
        assert(desc.payload_size == written);

        // Frame 1: Expect Inter-frame
        encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        assert(encodeRes == 1);
        assert(desc.frame_index == 1);
        assert(desc.is_keyframe == 0);

        // Request Force Keyframe
        moonshine_encoder_request_keyframe(handle);
        encodeRes = moonshine_encoder_encode_frame(handle, nullptr, 0, &desc, buffer.data(), (uint32_t)buffer.size(), &written);
        assert(encodeRes == 1);
        assert(desc.frame_index == 2);
        assert(desc.is_keyframe == 1);

        // Reconfigure Bitrate
        config.bitrate_kbps = 40000;
        config.fps = 120;
        int reconfRes = moonshine_encoder_reconfigure(handle, &config);
        (void)reconfRes;
        assert(reconfRes == 1);

        moonshine_encoder_destroy(handle);
        std::cout << "    [+] Codec " << codec << " verified successfully." << std::endl;
    }

    std::cout << "[+] Native Hardware Video Encoder Tests Passed Successfully!" << std::endl;
    return 0;
}
