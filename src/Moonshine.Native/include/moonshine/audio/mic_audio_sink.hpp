#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <deque>
#include <mutex>
#include <atomic>

#include "moonshine/audio/opus_audio_decoder.hpp"

namespace moonshine::audio {

/// <summary>
/// Configuration for the Host Microphone Virtual Audio Sink.
/// </summary>
struct MicSinkConfig {
    uint32_t sample_rate{48000};
    uint32_t channels{1}; // Mono default for voice passthrough
    uint32_t target_latency_ms{10}; // Sub-15ms target
    float gain_multiplier{1.0f};
    float noise_gate_threshold_db{-50.0f};
    bool is_muted{false};
};

/// <summary>
/// Performance and jitter metrics for microphone passthrough.
/// </summary>
struct MicSinkMetrics {
    uint64_t total_packets_received{0};
    uint64_t total_samples_rendered{0};
    uint32_t loss_count{0};
    uint32_t drift_corrections{0};
    double current_jitter_ms{0.0};
};

/// <summary>
/// A decoded voice frame stored in the adaptive jitter buffer.
/// </summary>
struct DecodedVoicePacket {
    uint16_t sequence_number{0};
    uint32_t timestamp{0};
    std::vector<float> pcm_samples{};
};

/// <summary>
/// High-performance, low-latency Host Microphone Virtual Audio Sink.
/// Decodes incoming client Opus microphone packets, manages adaptive jitter buffering (<10ms),
/// applies clock drift compensation, noise gating, gain normalisation, and outputs clean PCM.
/// </summary>
class MicAudioSink {
public:
    MicAudioSink() = default;
    explicit MicAudioSink(const MicSinkConfig& config);
    ~MicAudioSink();

    MicAudioSink(const MicAudioSink&) = delete;
    MicAudioSink& operator=(const MicAudioSink&) = delete;
    MicAudioSink(MicAudioSink&&) noexcept;
    MicAudioSink& operator=(MicAudioSink&&) noexcept;

    /// <summary>
    /// Initializes or reconfigures the microphone audio sink.
    /// </summary>
    bool initialize(const MicSinkConfig& config);

    /// <summary>
    /// Pushes an incoming Opus microphone RTP payload into the jitter buffer.
    /// </summary>
    bool push_opus_packet(
        const uint8_t* payload,
        uint32_t payload_len,
        uint32_t timestamp,
        uint16_t sequence_number
    );

    /// <summary>
    /// Pulls decoded and processed PCM samples for injection into virtual audio devices.
    /// </summary>
    bool pull_pcm(
        float* out_pcm,
        uint32_t max_samples,
        uint32_t& out_samples_read
    );

    /// <summary>
    /// Sets microphone input gain multiplier.
    /// </summary>
    void set_gain(float gain) noexcept;

    /// <summary>
    /// Sets microphone mute state.
    /// </summary>
    void set_mute(bool muted) noexcept;

    /// <summary>
    /// Retrieves current microphone sink metrics.
    /// </summary>
    void get_metrics(MicSinkMetrics& out_metrics) const noexcept;

    [[nodiscard]] bool is_initialized() const noexcept { return _initialized; }
    [[nodiscard]] uint32_t sample_rate() const noexcept { return _config.sample_rate; }
    [[nodiscard]] uint32_t channels() const noexcept { return _config.channels; }
    [[nodiscard]] float gain() const noexcept { return _gain.load(std::memory_order_relaxed); }
    [[nodiscard]] bool is_muted() const noexcept { return _muted.load(std::memory_order_relaxed); }

    void cleanup();

private:
    void apply_noise_gate_and_gain(float* samples, size_t count);
    void apply_clock_drift_compensation();

    MicSinkConfig _config{};
    bool _initialized{false};
    std::atomic<float> _gain{1.0f};
    std::atomic<bool> _muted{false};

    OpusAudioDecoder _decoder{};

    mutable std::mutex _buffer_mutex{};
    std::deque<DecodedVoicePacket> _jitter_queue{};
    std::vector<float> _staging_pcm{};

    uint16_t _last_seq{0};
    bool _has_first_packet{false};

    // Telemetry
    uint64_t _packets_received{0};
    uint64_t _samples_rendered{0};
    uint32_t _loss_count{0};
    uint32_t _drift_corrections{0};
    double _jitter_estimate_ms{0.0};
};

} // namespace moonshine::audio
