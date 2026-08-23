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

/**
 * @brief Low-latency Windows Audio Session API (WASAPI) Shared Mode Microphone Capture.
 * Captures communications and microphone audio from the default input device,
 * supporting 48kHz mono and stereo with format conversion and channel downmixing.
 */
class WasapiMicCapture {
public:
    WasapiMicCapture(uint32_t sample_rate = 48000, uint32_t channels = 1, uint32_t buffer_duration_ms = 10);
    ~WasapiMicCapture();

    bool initialize();
    bool read_samples_float(
        float* out_samples,
        uint32_t max_samples,
        uint32_t& out_read_samples,
        uint64_t& out_timestamp_qpc
    );
    void cleanup();

    [[nodiscard]] bool is_capturing() const noexcept { return _initialized; }
    [[nodiscard]] bool is_active() const noexcept { return _initialized && !_device_invalidated; }
    [[nodiscard]] uint32_t sample_rate() const noexcept { return _sample_rate; }
    [[nodiscard]] uint32_t channels() const noexcept { return _channels; }
    [[nodiscard]] bool is_device_invalidated() const noexcept { return _device_invalidated; }

private:
    uint32_t _sample_rate{48000};
    uint32_t _channels{1};
    uint32_t _buffer_duration_ms{10};
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
    uint32_t _device_channels{1};
    uint32_t _device_sample_rate{48000};
    bool _is_float_format{true};
    uint16_t _bits_per_sample{32};
#endif
    mutable std::recursive_mutex _mutex{};
};

} // namespace moonshine::audio
