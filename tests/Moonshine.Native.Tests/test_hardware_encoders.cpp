#include "moonshine/export/moonshine_native_api.h"
#include <iostream>

int main() {
    std::cout << "[*] Verifying multi-vendor hardware encoder capabilities and fail-closed activation..." << std::endl;

    for (uint32_t vendor = 0; vendor <= 4; ++vendor) {
        MoonshineEncoderCaps caps{};
        if (moonshine_encoder_query_caps(vendor, nullptr, &caps) != 0) {
            std::cerr << "[-] Error: query_caps succeeded with null device" << std::endl;
            return 1;
        }
        if (caps.supported_codecs_mask != 0) {
            std::cerr << "[-] Error: supported_codecs_mask non-zero for null device" << std::endl;
            return 2;
        }

        MoonshineEncoderConfig config{};
        config.width = 1920;
        config.height = 1080;
        config.fps = 60;
        config.bitrate_kbps = 20000;
        if (moonshine_encoder_create(vendor, nullptr, &config) != nullptr) {
            std::cerr << "[-] Error: encoder_create succeeded with null device" << std::endl;
            return 3;
        }
    }

    // Query codec support functions
    for (uint32_t codec = 0; codec <= 3; ++codec) {
        uint32_t nvenc_supported = 0;
        uint32_t amf_supported = 0;
        uint32_t qsv_supported = 0;
        if (moonshine_nvenc_query_codec_support(codec, &nvenc_supported) != 1) return 4;
        if (moonshine_amf_query_codec_support(codec, &amf_supported) != 1) return 5;
        if (moonshine_qsv_query_codec_support(codec, &qsv_supported) != 1) return 6;
        std::cout << "  Codec " << codec
                  << " [NVENC: " << nvenc_supported
                  << ", AMF: " << amf_supported
                  << ", QSV: " << qsv_supported << "]" << std::endl;
    }

    std::cout << "[+] Hardware encoder capability and fail-closed tests passed." << std::endl;
    return 0;
}
