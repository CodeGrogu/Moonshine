#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>

int main() {
    std::cout << "[*] Verifying unimplemented hardware encoders fail explicitly..." << std::endl;
    for (uint32_t vendor = 0; vendor <= 4; ++vendor) {
        MoonshineEncoderCaps caps{};
        assert(moonshine_encoder_query_caps(vendor, nullptr, &caps) == 0);
        MoonshineEncoderConfig config{};
        config.width = 1920;
        config.height = 1080;
        config.fps = 60;
        config.bitrate_kbps = 20000;
        assert(moonshine_encoder_create(vendor, nullptr, &config) == nullptr);
    }
    return 0;
}
