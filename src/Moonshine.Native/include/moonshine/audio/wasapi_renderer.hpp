#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include "moonshine/audio/audio_resampler.hpp"
#include "moonshine/audio/channel_converter.hpp"
#include <cstdint>
#include <memory>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <wrl/client.h>
#include <ksmedia.h>
#endif

namespace moonshine::audio {

enum class WasapiState : uint32_t {
    Stopped = 0,
    Running = 1,
    DeviceLost = 2,
    Recovering = 3,
    Faulted = 4
};

/**
 * @brief Ultra-low-latency Windows Audio Session API (WASAPI) Exclusive & Shared Mode Renderer.
 * Delivers sub-5ms latency for floating point PCM audio across Stereo, 5.1, and 7.1 surround with
 * persistent FIFO buffer pressure resilience, band-limited resampling, and robust recovery state machine.
 */
class WasapiRenderer {
public:
    WasapiRenderer(uint32_t sample_rate, uint16_t channels, bool exclusive);
    ~WasapiRenderer();

    int Initialize();
    int Recover();
    int SubmitPcm(const float* pcm_data, uint32_t sample_count);
    void GetMetrics(uint64_t& out_frames_rendered, uint32_t& out_underruns) const noexcept;
    void Shutdown();

    [[nodiscard]] bool IsInitialized() const noexcept { return state_ == WasapiState::Running; }
    [[nodiscard]] bool IsExclusive() const noexcept { return exclusive_; }
    [[nodiscard]] uint32_t GetSampleRate() const noexcept { return sample_rate_; }
    [[nodiscard]] uint16_t GetChannels() const noexcept { return channels_; }
    [[nodiscard]] bool IsDeviceInvalidated() const noexcept { return state_ == WasapiState::DeviceLost || state_ == WasapiState::Faulted; }
    [[nodiscard]] WasapiState GetState() const noexcept { return state_; }

private:
    int SetupAudioClientLocked();

    uint32_t sample_rate_{48000};
    uint16_t channels_{2};
    bool exclusive_{false};
    WasapiState state_{WasapiState::Stopped};
    uint64_t frames_rendered_{0};
    uint32_t underruns_{0};
    uint32_t recovery_attempts_{0};

    AudioResampler resampler_;
    std::vector<float> channel_staging_buffer_;
    std::vector<float> render_staging_buffer_;

#if defined(_WIN32)
    Microsoft::WRL::ComPtr<IMMDeviceEnumerator> enumerator_;
    Microsoft::WRL::ComPtr<IMMDevice> device_;
    Microsoft::WRL::ComPtr<IAudioClient> audio_client_;
    Microsoft::WRL::ComPtr<IAudioRenderClient> render_client_;
    UINT32 buffer_frame_count_{0};
    bool is_float_format_{true};
    uint32_t device_channels_{2};
    uint32_t device_sample_rate_{48000};
    uint16_t bits_per_sample_{32};
#endif
};

} // namespace moonshine::audio
