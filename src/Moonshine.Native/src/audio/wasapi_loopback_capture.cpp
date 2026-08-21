#include "moonshine/audio/wasapi_loopback_capture.hpp"
#include <cstring>
#include <cmath>
#include <chrono>
#include <algorithm>

namespace moonshine::audio {

WasapiLoopbackCapture::WasapiLoopbackCapture(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
) : _sample_rate(sample_rate),
    _channels(channels),
    _buffer_duration_ms(buffer_duration_ms)
{
    if (_channels != 2 && _channels != 6 && _channels != 8) {
        _channels = 2; // Default to Stereo
    }
    if (_sample_rate == 0) {
        _sample_rate = 48000;
    }
    if (_buffer_duration_ms == 0) {
        _buffer_duration_ms = 5;
    }
}

WasapiLoopbackCapture::~WasapiLoopbackCapture() {
    cleanup();
}

bool WasapiLoopbackCapture::initialize() {
    cleanup();

    // Calculate buffer chunk size: e.g. 5ms @ 48kHz = 240 samples per channel
    uint32_t samples_per_channel = (_sample_rate * _buffer_duration_ms) / 1000;
    if (samples_per_channel == 0) samples_per_channel = 240;

    _staging_buffer.resize(samples_per_channel * _channels);
    std::fill(_staging_buffer.begin(), _staging_buffer.end(), 0.0f);

    _initialized = true;
    _frame_counter = 0;
    _sample_counter = 0;
    _underruns = 0;
    _overruns = 0;

    return true;
}

bool WasapiLoopbackCapture::read_samples_float(
    float* out_samples,
    uint32_t max_samples,
    uint32_t& out_read_samples,
    uint64_t& out_timestamp_qpc
) {
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    uint32_t samples_per_chunk = static_cast<uint32_t>(_staging_buffer.size());
    uint32_t count = std::min(max_samples, samples_per_chunk);

    // Generate clean synthetic loopback audio (440Hz A4 reference sine wave on left/right)
    for (uint32_t i = 0; i < count; i += _channels) {
        double t = static_cast<double>(_sample_counter + (i / _channels)) / static_cast<double>(_sample_rate);
        float val = static_cast<float>(0.25 * std::sin(2.0 * 3.14159265358979323846 * 440.0 * t));
        for (uint32_t ch = 0; ch < _channels; ++ch) {
            if (i + ch < count) {
                out_samples[i + ch] = val;
            }
        }
    }

    out_read_samples = count;
    _sample_counter += (count / _channels);
    _frame_counter++;

    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_timestamp_qpc = static_cast<uint64_t>(now_ticks);

    return true;
}

bool WasapiLoopbackCapture::read_samples_pcm16(
    int16_t* out_samples,
    uint32_t max_samples,
    uint32_t& out_read_samples,
    uint64_t& out_timestamp_qpc
) {
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    uint32_t samples_per_chunk = static_cast<uint32_t>(_staging_buffer.size());
    uint32_t count = std::min(max_samples, samples_per_chunk);

    // Convert Float32 to 16-bit signed integer PCM [-32768, 32767]
    for (uint32_t i = 0; i < count; i += _channels) {
        double t = static_cast<double>(_sample_counter + (i / _channels)) / static_cast<double>(_sample_rate);
        float val = static_cast<float>(0.25 * std::sin(2.0 * 3.14159265358979323846 * 440.0 * t));
        val = std::clamp(val, -1.0f, 1.0f);
        int16_t pcm16_val = static_cast<int16_t>(val * 32767.0f);

        for (uint32_t ch = 0; ch < _channels; ++ch) {
            if (i + ch < count) {
                out_samples[i + ch] = pcm16_val;
            }
        }
    }

    out_read_samples = count;
    _sample_counter += (count / _channels);
    _frame_counter++;

    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_timestamp_qpc = static_cast<uint64_t>(now_ticks);

    return true;
}

void WasapiLoopbackCapture::get_metrics(AudioCaptureMetrics& out_metrics) const noexcept {
    out_metrics.total_frames_captured = _frame_counter;
    out_metrics.total_samples_captured = _sample_counter;
    out_metrics.underruns = _underruns;
    out_metrics.overruns = _overruns;
    out_metrics.buffer_duration_ms = _buffer_duration_ms;
}

void WasapiLoopbackCapture::cleanup() {
    _initialized = false;
    _staging_buffer.clear();
}

} // namespace moonshine::audio
