#pragma once

#include <cstdint>
#include <vector>
#include <atomic>
#include <mutex>

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

enum class AudioChannels : uint32_t {
    Stereo = 2,
    Surround51 = 6,
    Surround71 = 8
};

struct AudioCaptureMetrics {
    uint64_t total_frames_captured{0};
    uint64_t total_samples_captured{0};
    uint32_t underruns{0};
    uint32_t overruns{0};
    uint32_t buffer_duration_ms{5};
};

class WasapiLoopbackCapture {
public:
    WasapiLoopbackCapture(uint32_t sample_rate, uint32_t channels, uint32_t buffer_duration_ms);
    ~WasapiLoopbackCapture();

    bool initialize();
    bool recover();
    bool read_samples_float(
        float* out_samples,
        uint32_t max_samples,
        uint32_t& out_read_samples,
        uint64_t& out_timestamp_qpc
    );
    bool read_samples_pcm16(
        int16_t* out_samples,
        uint32_t max_samples,
        uint32_t& out_read_samples,
        uint64_t& out_timestamp_qpc
    );
    void get_metrics(AudioCaptureMetrics& out_metrics) const noexcept;
    void cleanup();

    [[nodiscard]] bool is_capturing() const noexcept { return _initialized; }
    [[nodiscard]] uint32_t sample_rate() const noexcept { return _sample_rate; }
    [[nodiscard]] uint32_t channels() const noexcept { return _channels; }
    [[nodiscard]] bool is_device_invalidated() const noexcept { return _device_invalidated; }

private:
    uint32_t _sample_rate{48000};
    uint32_t _channels{2};
    uint32_t _buffer_duration_ms{5};
    bool _initialized{false};
    bool _device_invalidated{false};
    uint64_t _frame_counter{0};
    uint64_t _sample_counter{0};
    uint32_t _underruns{0};
    uint32_t _overruns{0};

#if defined(_WIN32)
    Microsoft::WRL::ComPtr<IMMDeviceEnumerator> _enumerator;
    Microsoft::WRL::ComPtr<IMMDevice> _device;
    Microsoft::WRL::ComPtr<IAudioClient> _audio_client;
    Microsoft::WRL::ComPtr<IAudioCaptureClient> _capture_client;
    uint32_t _device_channels{2};
    uint32_t _device_sample_rate{48000};
    bool _is_float_format{true};
    uint16_t _bits_per_sample{32};
    double _resample_phase{0.0};
    std::vector<float> _last_src_frame{};
#endif
    mutable std::recursive_mutex _mutex{};
};

} // namespace moonshine::audio

