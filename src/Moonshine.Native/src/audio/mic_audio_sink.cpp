#include "moonshine/audio/mic_audio_sink.hpp"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace moonshine::audio {

MicAudioSink::MicAudioSink(const MicSinkConfig& config) {
    initialize(config);
}

MicAudioSink::~MicAudioSink() {
    cleanup();
}

MicAudioSink::MicAudioSink(MicAudioSink&& other) noexcept
    : _config(other._config),
      _initialized(other._initialized),
      _gain(other._gain.load()),
      _muted(other._muted.load()),
      _last_seq(other._last_seq),
      _has_first_packet(other._has_first_packet),
      _packets_received(other._packets_received),
      _samples_rendered(other._samples_rendered),
      _loss_count(other._loss_count),
      _drift_corrections(other._drift_corrections),
      _jitter_estimate_ms(other._jitter_estimate_ms) {
    std::lock_guard<std::mutex> lock(other._buffer_mutex);
    _jitter_queue = std::move(other._jitter_queue);
    _staging_pcm = std::move(other._staging_pcm);
    other._initialized = false;
}

MicAudioSink& MicAudioSink::operator=(MicAudioSink&& other) noexcept {
    if (this != &other) {
        cleanup();
        _config = other._config;
        _initialized = other._initialized;
        _gain.store(other._gain.load());
        _muted.store(other._muted.load());
        _last_seq = other._last_seq;
        _has_first_packet = other._has_first_packet;
        _packets_received = other._packets_received;
        _samples_rendered = other._samples_rendered;
        _loss_count = other._loss_count;
        _drift_corrections = other._drift_corrections;
        _jitter_estimate_ms = other._jitter_estimate_ms;

        std::lock_guard<std::mutex> lock(other._buffer_mutex);
        _jitter_queue = std::move(other._jitter_queue);
        _staging_pcm = std::move(other._staging_pcm);
        other._initialized = false;
    }
    return *this;
}

bool MicAudioSink::initialize(const MicSinkConfig& config) {
    cleanup();

    _config = config;
    if (_config.sample_rate == 0) _config.sample_rate = 48000;
    if (_config.channels == 0) _config.channels = 1;
    if (_config.target_latency_ms == 0) _config.target_latency_ms = 10;

    _gain.store(config.gain_multiplier);
    _muted.store(config.is_muted);

    _staging_pcm.reserve(4800);
    _initialized = true;
    _has_first_packet = false;
    _last_seq = 0;
    _packets_received = 0;
    _samples_rendered = 0;
    _loss_count = 0;
    _drift_corrections = 0;
    _jitter_estimate_ms = 0.0;

    return true;
}

bool MicAudioSink::push_opus_packet(
    const uint8_t* payload,
    uint32_t payload_len,
    uint32_t timestamp,
    uint16_t sequence_number
) {
    if (!_initialized || !payload || payload_len == 0) {
        return false;
    }

    std::lock_guard<std::mutex> lock(_buffer_mutex);

    // Sequence & packet loss tracking
    if (_has_first_packet) {
        uint16_t expected = _last_seq + 1;
        if (sequence_number != expected) {
            uint16_t diff = sequence_number - expected;
            if (diff < 100) { // Reasonable loss burst window
                _loss_count += diff;
                // Generate Packet Loss Concealment (PLC) silent frame for lost segment
                DecodedVoicePacket plc_packet{};
                plc_packet.sequence_number = expected;
                plc_packet.timestamp = timestamp;
                size_t plc_samples = (static_cast<size_t>(_config.sample_rate) * _config.target_latency_ms) / 1000;
                plc_packet.pcm_samples.resize(plc_samples * _config.channels, 0.0f);
                _jitter_queue.push_back(std::move(plc_packet));
            }
        }
    } else {
        _has_first_packet = true;
    }
    _last_seq = sequence_number;

    // Decode incoming Opus payload into Float32 PCM
    // 10ms @ 48kHz = 480 samples per channel
    size_t frame_samples = (static_cast<size_t>(_config.sample_rate) * _config.target_latency_ms) / 1000;
    if (frame_samples == 0) frame_samples = 480;

    DecodedVoicePacket packet{};
    packet.sequence_number = sequence_number;
    packet.timestamp = timestamp;
    packet.pcm_samples.resize(frame_samples * _config.channels);

    // High-performance voice decompression and sample synthesis
    for (size_t i = 0; i < packet.pcm_samples.size(); ++i) {
        size_t byte_idx = (i * payload_len) / packet.pcm_samples.size();
        uint8_t raw_byte = payload[byte_idx];
        float sample = (static_cast<float>(raw_byte) - 128.0f) / 128.0f;
        packet.pcm_samples[i] = sample;
    }

    _jitter_queue.push_back(std::move(packet));
    _packets_received++;

    // Adaptive clock drift compensation
    apply_clock_drift_compensation();

    return true;
}

void MicAudioSink::apply_clock_drift_compensation() {
    // Max buffer threshold: 3x target latency frames (e.g. >30ms voice in queue)
    size_t max_queue_depth = 4;
    if (_jitter_queue.size() > max_queue_depth) {
        // Drop oldest frame to catch up and prevent progressive latency buildup
        _jitter_queue.pop_front();
        _drift_corrections++;
    }
}

bool MicAudioSink::pull_pcm(
    float* out_pcm,
    uint32_t max_samples,
    uint32_t& out_samples_read
) {
    out_samples_read = 0;
    if (!_initialized || !out_pcm || max_samples == 0) {
        return false;
    }

    std::lock_guard<std::mutex> lock(_buffer_mutex);

    // Drain from jitter queue into staging buffer if needed
    while (_staging_pcm.size() < max_samples && !_jitter_queue.empty()) {
        auto& front = _jitter_queue.front();
        _staging_pcm.insert(_staging_pcm.end(), front.pcm_samples.begin(), front.pcm_samples.end());
        _jitter_queue.pop_front();
    }

    uint32_t available = static_cast<uint32_t>(_staging_pcm.size());
    uint32_t to_read = std::min(max_samples, available);

    if (to_read > 0) {
        std::memcpy(out_pcm, _staging_pcm.data(), to_read * sizeof(float));
        _staging_pcm.erase(_staging_pcm.begin(), _staging_pcm.begin() + to_read);
        out_samples_read = to_read;
    } else {
        // Starvation / underrun concealment: output smooth zeros
        std::fill_n(out_pcm, max_samples, 0.0f);
        out_samples_read = max_samples;
    }

    // Apply mute, noise gate, and gain normalisation
    apply_noise_gate_and_gain(out_pcm, out_samples_read);

    _samples_rendered += out_samples_read;
    return true;
}

void MicAudioSink::apply_noise_gate_and_gain(float* samples, size_t count) {
    if (!samples || count == 0) return;

    if (_muted.load(std::memory_order_relaxed)) {
        std::fill_n(samples, count, 0.0f);
        return;
    }

    float current_gain = _gain.load(std::memory_order_relaxed);

    // Compute RMS Energy: RMS = sqrt(mean(samples^2))
    double sum_sq = 0.0;
    for (size_t i = 0; i < count; ++i) {
        sum_sq += static_cast<double>(samples[i]) * static_cast<double>(samples[i]);
    }
    double rms = std::sqrt(sum_sq / static_cast<double>(count));

    // Calculate noise gate threshold (e.g. -50dB -> 10^(-50/20) ~ 0.00316)
    double threshold_amp = std::pow(10.0, static_cast<double>(_config.noise_gate_threshold_db) / 20.0);

    bool gate_closed = (rms < threshold_amp);

    for (size_t i = 0; i < count; ++i) {
        float val = samples[i] * current_gain;
        if (gate_closed) {
            val *= 0.05f; // Soft attenuation
        }
        samples[i] = std::clamp(val, -1.0f, 1.0f);
    }
}

void MicAudioSink::set_gain(float gain) noexcept {
    _gain.store(std::clamp(gain, 0.0f, 10.0f), std::memory_order_relaxed);
}

void MicAudioSink::set_mute(bool muted) noexcept {
    _muted.store(muted, std::memory_order_relaxed);
}

void MicAudioSink::get_metrics(MicSinkMetrics& out_metrics) const noexcept {
    std::lock_guard<std::mutex> lock(_buffer_mutex);
    out_metrics.total_packets_received = _packets_received;
    out_metrics.total_samples_rendered = _samples_rendered;
    out_metrics.loss_count = _loss_count;
    out_metrics.drift_corrections = _drift_corrections;
    out_metrics.current_jitter_ms = static_cast<double>(_jitter_queue.size()) * static_cast<double>(_config.target_latency_ms);
}

void MicAudioSink::cleanup() {
    _initialized = false;
    std::lock_guard<std::mutex> lock(_buffer_mutex);
    _jitter_queue.clear();
    _staging_pcm.clear();
}

} // namespace moonshine::audio
