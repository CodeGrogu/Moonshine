#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include <cstdint>
#include <memory>
#include <vector>

namespace moonshine::audio {

/**
 * @brief Ultra-low-latency Windows Audio Session API (WASAPI) Exclusive Mode Renderer.
 * Delivers sub-5ms latency for 48kHz 32-bit floating point PCM audio across Stereo, 5.1, and 7.1 surround.
 */
class WasapiRenderer {
public:
    WasapiRenderer(uint32_t sample_rate, uint16_t channels, bool exclusive);
    ~WasapiRenderer();

    int Initialize();
    int SubmitPcm(const float* pcm_data, uint32_t sample_count);
    void GetMetrics(uint64_t& out_frames_rendered, uint32_t& out_underruns) const noexcept;
    void Shutdown();

    [[nodiscard]] bool IsInitialized() const noexcept { return initialized_; }
    [[nodiscard]] bool IsExclusive() const noexcept { return exclusive_; }
    [[nodiscard]] uint32_t GetSampleRate() const noexcept { return sample_rate_; }
    [[nodiscard]] uint16_t GetChannels() const noexcept { return channels_; }

private:
    uint32_t sample_rate_{48000};
    uint16_t channels_{2};
    bool exclusive_{false};
    bool initialized_{false};
    uint64_t frames_rendered_{0};
    uint32_t underruns_{0};
    std::vector<float> staging_buffer_;
};

} // namespace moonshine::audio
