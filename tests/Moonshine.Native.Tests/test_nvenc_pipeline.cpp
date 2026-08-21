#include "moonshine/export/moonshine_native_api.h"
#include <iostream>

int main() {
    std::cout << "[*] Testing live NVIDIA NVENC codec query support..." << std::endl;
    for (uint32_t codec = 0; codec <= 3; ++codec) {
        uint32_t supported = 0;
        if (moonshine_nvenc_query_codec_support(codec, &supported) != 1) {
            return 1;
        }
        if (supported != 0 && supported != 1) {
            return 2;
        }
        std::cout << "  Codec " << codec << " NVENC support: " << supported << std::endl;
    }
    return 0;
}
