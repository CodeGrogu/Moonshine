#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <array>
#include <chrono>
#include <mutex>

struct OpusDecoder;
struct OpusMSDecoder;

namespace moonshine::audio {

/// <summary>
/// Performance and error telemetry for the Opus audio decoder.
/// </summary>
struct OpusDecoderMetrics {
    uint64_t total_frames_decoded{0};
    uint64_t total_samples_decoded{0};
    uint32_t decode_errors{0};
    uint32_t concealment_frames{0};
    double avg_decode_time_us{0.0};
    uint32_t streams_count{1};
    uint32_t coupled_count{1};
};

/// <summary>
/// High-performance, zero-allocation multi-channel Opus audio decoder.
/// Supports 48kHz Mono, Stereo, Surround 5.1 (6-channel Vorbis mapping), and Surround 7.1 (8-channel).
/// Implements packet loss concealment (PLC) and sub-microsecond local frame reconstruction.
/// </summary>
class OpusAudioDecoder {
public:
    OpusAudioDecoder() = default;
    OpusAudioDecoder(uint32_t sample_rate, uint32_t channels);
    ~OpusAudioDecoder();

    OpusAudioDecoder(const OpusAudioDecoder&) = delete;
    OpusAudioDecoder& operator=(const OpusAudioDecoder&) = delete;
    OpusAudioDecoder(OpusAudioDecoder&&) noexcept;
    OpusAudioDecoder& operator=(OpusAudioDecoder&&) noexcept;

    /// <summary>
    /// Initializes or reconfigures the decoder.
    /// </summary>
    bool initialize(uint32_t sample_rate, uint32_t channels);

    /// <summary>
    /// Decodes an Opus compressed packet into multi-channel Float32 PCM [-1.0f, 1.0f].
    /// </summary>
    bool decode_float(
        const uint8_t* opus_payload,
        uint32_t payload_bytes,
        float* out_pcm_samples,
        uint32_t max_samples,
        uint32_t& out_samples_decoded,
        int decode_fec
    );

    /// <summary>
    /// Decodes an Opus compressed packet into multi-channel Int16 PCM [-32768, 32767].
    /// </summary>
    bool decode_pcm16(
        const uint8_t* opus_payload,
        uint32_t payload_bytes,
        int16_t* out_pcm_samples,
        uint32_t max_samples,
        uint32_t& out_samples_decoded,
        int decode_fec
    );

    /// <summary>
    /// Resets internal decoder state, filter history, and metrics.
    /// </summary>
    void reset();

    /// <summary>
    /// Retrieves live performance metrics.
    /// </summary>
    void get_metrics(OpusDecoderMetrics& out_metrics) const noexcept;

    /// <summary>
    /// Releases decoder resources.
    /// </summary>
    void cleanup();

    [[nodiscard]] bool is_initialized() const noexcept { return _initialized; }
    [[nodiscard]] uint32_t sample_rate() const noexcept { return _sample_rate; }
    [[nodiscard]] uint32_t channels() const noexcept { return _channels; }
    [[nodiscard]] uint32_t streams_count() const noexcept { return _streams_count; }
    [[nodiscard]] uint32_t coupled_count() const noexcept { return _coupled_count; }

private:
    void configure_channel_mapping();

    uint32_t _sample_rate{48000};
    uint32_t _channels{2};
    bool _initialized{false};

    uint32_t _streams_count{1};
    uint32_t _coupled_count{1};
    std::array<uint8_t, 8> _channel_mapping{0, 1, 2, 3, 4, 5, 6, 7};

    OpusDecoder* _decoder{nullptr};
    OpusMSDecoder* _ms_decoder{nullptr};

    uint64_t _frames_decoded{0};
    uint64_t _samples_decoded{0};
    uint32_t _decode_errors{0};
    uint32_t _concealment_frames{0};
    uint64_t _total_decode_time_ns{0};
    mutable std::recursive_mutex _mutex{};
};

} // namespace moonshine::audio
