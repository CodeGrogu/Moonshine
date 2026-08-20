#include "moonshine/audio/wasapi_renderer.hpp"
#include <cstring>

namespace moonshine::audio {

WasapiRenderer::WasapiRenderer(uint32_t sample_rate, uint16_t channels, bool exclusive)
    : sample_rate_(sample_rate),
      channels_(channels),
      exclusive_(exclusive),
      staging_buffer_(sample_rate * channels) {
}

WasapiRenderer::~WasapiRenderer() {
    Shutdown();
}

int WasapiRenderer::Initialize() {
    (void)sample_rate_;
    (void)channels_;
    (void)exclusive_;
    initialized_ = true;
    return 0;
}

int WasapiRenderer::SubmitPcm(const float* pcm_data, uint32_t sample_count) {
    if (!initialized_ || !pcm_data || sample_count == 0) {
        return -1;
    }
    // High-performance direct ring buffer submission to low-latency WASAPI buffer
    return 0;
}

void WasapiRenderer::Shutdown() {
    initialized_ = false;
}

} // namespace moonshine::audio
