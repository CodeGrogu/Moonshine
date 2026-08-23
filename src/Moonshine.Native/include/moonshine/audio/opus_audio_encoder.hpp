#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <array>
#include <chrono>
#include <mutex>
#include <span>

struct OpusEncoder;
struct OpusMSEncoder;

namespace moonshine::audio {

/// <summary>
/// Opus application profile for encoder tuning.
/// </summary>
enum class OpusApplication : uint32_t {
    Voip = 2048,
    Audio = 2049,
    RestrictedLowDelay = 2051
};

/// <summary>
/// Configuration for the Opus audio encoder.
/// </summary>
struct OpusEncoderConfig {
    uint32_t sample_rate{48000};
    uint32_t channels{2};
    uint32_t bitrate{160000};
    uint32_t frame_duration_ms{5};
    uint32_t complexity{8};
    bool use_vbr{true};
    OpusApplication application{OpusApplication::RestrictedLowDelay};
};

/// <summary>
/// Performance telemetry for the Opus encoder.
/// </summary>
struct OpusEncoderMetrics {
    uint64_t total_frames_encoded{0};
    uint64_t total_bytes_encoded{0};
    double avg_encode_time_us{0.0};
    uint32_t current_bitrate{0};
    uint32_t streams_count{1};
    uint32_t coupled_count{1};
};

/// <summary>
/// High-performance, zero-allocation multi-channel Opus audio encoder.
/// Supports 48kHz Mono, Stereo, Surround 5.1 (6-channel Vorbis mapping), and Surround 7.1 (8-channel).
/// Optimized for sub-1ms compression latency in streaming hot paths.
/// </summary>
class OpusAudioEncoder {
public:
    OpusAudioEncoder() = default;
    explicit OpusAudioEncoder(const OpusEncoderConfig& config);
    ~OpusAudioEncoder();

    OpusAudioEncoder(const OpusAudioEncoder&) = delete;
    OpusAudioEncoder& operator=(const OpusAudioEncoder&) = delete;
    OpusAudioEncoder(OpusAudioEncoder&&) noexcept;
    OpusAudioEncoder& operator=(OpusAudioEncoder&&) noexcept;

    /// <summary>
    /// Initializes or reconfigures the encoder.
    /// </summary>
    bool initialize(const OpusEncoderConfig& config);

    /// <summary>
    /// Encodes a multi-channel Float32 PCM audio frame into an Opus packet with zero allocations.
    /// </summary>
    bool encode_float(
        const float* pcm_samples,
        uint32_t frame_samples,
        uint8_t* out_payload,
        uint32_t max_payload_bytes,
        uint32_t& out_payload_bytes
    );

    /// <summary>
    /// Encodes a multi-channel Int16 PCM audio frame into an Opus packet with zero allocations.
    /// </summary>
    bool encode_pcm16(
        const int16_t* pcm_samples,
        uint32_t frame_samples,
        uint8_t* out_payload,
        uint32_t max_payload_bytes,
        uint32_t& out_payload_bytes
    );

    /// <summary>
    /// Dynamically adjusts target encoding bitrate at runtime.
    /// </summary>
    bool set_bitrate(uint32_t bitrate);

    /// <summary>
    /// Dynamically adjusts encoding complexity (0-10) at runtime.
    /// </summary>
    bool set_complexity(uint32_t complexity);

    /// <summary>
    /// Retrieves live encoder performance metrics.
    /// </summary>
    void get_metrics(OpusEncoderMetrics& out_metrics) const noexcept;

    /// <summary>
    /// Resets internal encoder state and telemetry.
    /// </summary>
    void reset();

    [[nodiscard]] bool is_initialized() const noexcept { return _initialized; }
    [[nodiscard]] uint32_t channels() const noexcept { return _config.channels; }
    [[nodiscard]] uint32_t sample_rate() const noexcept { return _config.sample_rate; }
    [[nodiscard]] uint32_t bitrate() const noexcept { return _config.bitrate; }
    [[nodiscard]] uint32_t streams_count() const noexcept { return _streams_count; }
    [[nodiscard]] uint32_t coupled_count() const noexcept { return _coupled_count; }

private:
    void cleanup();
    void configure_channel_mapping();

    OpusEncoderConfig _config{};
    bool _initialized{false};
    uint32_t _streams_count{1};
    uint32_t _coupled_count{1};
    std::array<uint8_t, 8> _channel_mapping{};

    OpusEncoder* _encoder{nullptr};
    OpusMSEncoder* _ms_encoder{nullptr};
    mutable std::recursive_mutex _mutex{};

    // Telemetry
    uint64_t _frames_encoded{0};
    uint64_t _bytes_encoded{0};
    uint64_t _total_encode_time_ns{0};
};

} // namespace moonshine::audio

