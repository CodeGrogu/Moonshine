#pragma once

#include <cstdint>
#include <vector>
#include <atomic>

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

private:
    uint32_t _sample_rate{48000};
    uint32_t _channels{2};
    uint32_t _buffer_duration_ms{5};
    bool _initialized{false};
    uint64_t _frame_counter{0};
    uint64_t _sample_counter{0};
    uint32_t _underruns{0};
    uint32_t _overruns{0};
    std::vector<float> _staging_buffer;
};

} // namespace moonshine::audio
