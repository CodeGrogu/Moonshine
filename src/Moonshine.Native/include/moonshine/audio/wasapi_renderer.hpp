#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include <cstdint>
#include <memory>
#include <vector>

namespace moonshine::audio {

class WasapiRenderer {
public:
    WasapiRenderer(uint32_t sample_rate, uint16_t channels, bool exclusive);
    ~WasapiRenderer();

    int Initialize();
    int SubmitPcm(const float* pcm_data, uint32_t sample_count);
    void Shutdown();

private:
    uint32_t sample_rate_{48000};
    uint16_t channels_{2};
    bool exclusive_{false};
    bool initialized_{false};
    std::vector<float> staging_buffer_;
};

} // namespace moonshine::audio
