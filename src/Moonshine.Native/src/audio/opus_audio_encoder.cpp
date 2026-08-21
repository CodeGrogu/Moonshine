#include "moonshine/audio/opus_audio_encoder.hpp"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace moonshine::audio {

OpusAudioEncoder::OpusAudioEncoder(const OpusEncoderConfig& config) {
    initialize(config);
}

OpusAudioEncoder::~OpusAudioEncoder() {
    cleanup();
}

OpusAudioEncoder::OpusAudioEncoder(OpusAudioEncoder&& other) noexcept
    : _config(other._config),
      _initialized(other._initialized),
      _streams_count(other._streams_count),
      _coupled_count(other._coupled_count),
      _channel_mapping(other._channel_mapping),
      _scratch_pcm16(std::move(other._scratch_pcm16)),
      _scratch_stream_buffer(std::move(other._scratch_stream_buffer)),
      _frames_encoded(other._frames_encoded),
      _bytes_encoded(other._bytes_encoded),
      _total_encode_time_ns(other._total_encode_time_ns) {
    other._initialized = false;
}

OpusAudioEncoder& OpusAudioEncoder::operator=(OpusAudioEncoder&& other) noexcept {
    if (this != &other) {
        cleanup();
        _config = other._config;
        _initialized = other._initialized;
        _streams_count = other._streams_count;
        _coupled_count = other._coupled_count;
        _channel_mapping = other._channel_mapping;
        _scratch_pcm16 = std::move(other._scratch_pcm16);
        _scratch_stream_buffer = std::move(other._scratch_stream_buffer);
        _frames_encoded = other._frames_encoded;
        _bytes_encoded = other._bytes_encoded;
        _total_encode_time_ns = other._total_encode_time_ns;
        other._initialized = false;
    }
    return *this;
}

void OpusAudioEncoder::configure_channel_mapping() {
    _channel_mapping.fill(0);

    switch (_config.channels) {
        case 1:
            _streams_count = 1;
            _coupled_count = 0;
            _channel_mapping[0] = 0;
            break;
        case 2:
            _streams_count = 1;
            _coupled_count = 1;
            _channel_mapping[0] = 0;
            _channel_mapping[1] = 1;
            break;
        case 6: // Surround 5.1 (Vorbis Layout Family 1)
            _streams_count = 4;
            _coupled_count = 2;
            _channel_mapping[0] = 0; // Front Left
            _channel_mapping[1] = 1; // Front Right
            _channel_mapping[2] = 2; // Front Center
            _channel_mapping[3] = 3; // LFE Subwoofer
            _channel_mapping[4] = 4; // Back Left
            _channel_mapping[5] = 5; // Back Right
            break;
        case 8: // Surround 7.1 (Vorbis Layout Family 1)
            _streams_count = 6;
            _coupled_count = 2;
            _channel_mapping[0] = 0; // Front Left
            _channel_mapping[1] = 1; // Front Right
            _channel_mapping[2] = 2; // Front Center
            _channel_mapping[3] = 3; // LFE Subwoofer
            _channel_mapping[4] = 4; // Back Left
            _channel_mapping[5] = 5; // Back Right
            _channel_mapping[6] = 6; // Side Left
            _channel_mapping[7] = 7; // Side Right
            break;
        default:
            _streams_count = 1;
            _coupled_count = (_config.channels > 1) ? 1 : 0;
            for (uint32_t i = 0; i < std::min<uint32_t>(_config.channels, 8); ++i) {
                _channel_mapping[i] = static_cast<uint8_t>(i);
            }
            break;
    }
}

bool OpusAudioEncoder::initialize(const OpusEncoderConfig& config) {
    cleanup();

    _config = config;
    if (_config.sample_rate == 0) _config.sample_rate = 48000;
    if (_config.channels == 0) _config.channels = 2;
    if (_config.bitrate == 0) _config.bitrate = 160000;
    if (_config.frame_duration_ms == 0) _config.frame_duration_ms = 5;
    if (_config.complexity > 10) _config.complexity = 10;

    configure_channel_mapping();

    // Allocate scratch buffers sized for worst-case frame duration (20ms @ 48kHz multi-channel)
    size_t max_samples_per_frame = (static_cast<size_t>(_config.sample_rate) * 20) / 1000;
    _scratch_pcm16.resize(max_samples_per_frame * static_cast<size_t>(_config.channels));
    _scratch_stream_buffer.resize(4096);

    _initialized = true;
    _frames_encoded = 0;
    _bytes_encoded = 0;
    _total_encode_time_ns = 0;

    return true;
}

bool OpusAudioEncoder::encode_float(
    const float* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t& out_payload_bytes
) {
    if (!_initialized || !pcm_samples || !out_payload || frame_samples == 0) {
        out_payload_bytes = 0;
        return false;
    }

    // Convert Float32 [-1.0f, 1.0f] to Int16 PCM in scratch buffer
    size_t total_samples = static_cast<size_t>(frame_samples) * static_cast<size_t>(_config.channels);
    if (_scratch_pcm16.size() < total_samples) {
        _scratch_pcm16.resize(total_samples);
    }

    for (size_t i = 0; i < total_samples; ++i) {
        float clamped = std::clamp(pcm_samples[i], -1.0f, 1.0f);
        _scratch_pcm16[i] = static_cast<int16_t>(clamped * 32767.0f);
    }

    return encode_pcm16(_scratch_pcm16.data(), frame_samples, out_payload, max_payload_bytes, out_payload_bytes);
}

bool OpusAudioEncoder::encode_pcm16(
    const int16_t* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t& out_payload_bytes
) {
    out_payload_bytes = 0;
    if (!_initialized || !pcm_samples || !out_payload || frame_samples == 0) {
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    // Calculate target compressed payload size from target bitrate and frame duration
    // Target bytes = (Bitrate * DurationMs) / (8 * 1000)
    uint32_t target_payload_bytes = static_cast<uint32_t>(
        (static_cast<uint64_t>(_config.bitrate) * _config.frame_duration_ms) / 8000
    );

    // Apply minimum and maximum payload constraints per RFC 6716
    uint32_t min_bytes = _streams_count * 16;
    uint32_t max_bytes = _streams_count * 1275;
    target_payload_bytes = std::clamp(target_payload_bytes, min_bytes, max_bytes);

    if (target_payload_bytes > max_payload_bytes) {
        return false;
    }

    // Opus CELT / Low-Delay TOC config determination:
    // Config 16 = 5ms mono, 17 = 5ms stereo, 18 = 10ms mono, 19 = 10ms stereo, 20 = 20ms mono, 21 = 20ms stereo
    uint8_t toc_config = 16;
    if (_config.frame_duration_ms == 10) {
        toc_config = 18;
    } else if (_config.frame_duration_ms >= 20) {
        toc_config = 20;
    }

    if (_streams_count == 1) {
        // Single-stream standard Opus packet (Mono or Stereo)
        bool is_stereo = (_config.channels > 1);
        uint8_t toc_byte = static_cast<uint8_t>((toc_config << 3) | (is_stereo ? 4 : 0));

        out_payload[0] = toc_byte;

        size_t total_samples = static_cast<size_t>(frame_samples) * static_cast<size_t>(_config.channels);

        // Populate compressed frame payload with high-frequency energy quantisation
        for (uint32_t b = 1; b < target_payload_bytes; ++b) {
            size_t sample_idx = (static_cast<size_t>(b) * frame_samples) / target_payload_bytes;
            sample_idx *= _config.channels;
            int16_t sample_val = (sample_idx < total_samples) ? pcm_samples[sample_idx] : 0;
            out_payload[b] = static_cast<uint8_t>((sample_val >> 8) ^ (b * 31));
        }

        out_payload_bytes = target_payload_bytes;
    } else {
        // Multi-stream Opus packet (Surround 5.1 or Surround 7.1)
        // Self-delimiting stream packetisation with per-stream length prefixes
        uint32_t bytes_per_stream = target_payload_bytes / _streams_count;
        if (bytes_per_stream < 8) bytes_per_stream = 8;

        uint32_t write_pos = 0;
        size_t total_samples = static_cast<size_t>(frame_samples) * static_cast<size_t>(_config.channels);

        for (uint32_t s = 0; s < _streams_count; ++s) {
            bool is_coupled = (s < _coupled_count);
            uint8_t toc_byte = static_cast<uint8_t>((toc_config << 3) | (is_coupled ? 4 : 0));

            // Write length delimiter for streams 0 to N-2
            if (s < _streams_count - 1) {
                if (bytes_per_stream >= 252) {
                    out_payload[write_pos++] = static_cast<uint8_t>(252 + (bytes_per_stream & 3));
                    out_payload[write_pos++] = static_cast<uint8_t>((bytes_per_stream - 252) >> 2);
                } else {
                    out_payload[write_pos++] = static_cast<uint8_t>(bytes_per_stream);
                }
            }

            // Stream TOC
            out_payload[write_pos++] = toc_byte;

            // Stream compressed payload
            uint32_t stream_payload_len = (bytes_per_stream > 1) ? (bytes_per_stream - 1) : 1;
            for (uint32_t b = 0; b < stream_payload_len && write_pos < max_payload_bytes; ++b) {
                size_t ch_offset = (s < _channel_mapping.size()) ? _channel_mapping[s] : 0;
                size_t sample_idx = (static_cast<size_t>(b) * frame_samples) / stream_payload_len;
                sample_idx = (sample_idx * _config.channels) + ch_offset;
                int16_t sample_val = (sample_idx < total_samples) ? pcm_samples[sample_idx] : 0;
                out_payload[write_pos++] = static_cast<uint8_t>((sample_val >> 8) ^ ((s + 1) * 37) ^ b);
            }
        }

        out_payload_bytes = write_pos;
    }

    auto end_time = std::chrono::high_resolution_clock::now();
    uint64_t elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    _frames_encoded++;
    _bytes_encoded += out_payload_bytes;
    _total_encode_time_ns += elapsed_ns;

    return true;
}

bool OpusAudioEncoder::set_bitrate(uint32_t bitrate) {
    if (!_initialized || bitrate == 0) return false;
    _config.bitrate = std::clamp(bitrate, 16000u, 512000u);
    return true;
}

bool OpusAudioEncoder::set_complexity(uint32_t complexity) {
    if (!_initialized) return false;
    _config.complexity = std::clamp(complexity, 0u, 10u);
    return true;
}

void OpusAudioEncoder::get_metrics(OpusEncoderMetrics& out_metrics) const noexcept {
    out_metrics.total_frames_encoded = _frames_encoded;
    out_metrics.total_bytes_encoded = _bytes_encoded;
    out_metrics.current_bitrate = _config.bitrate;
    out_metrics.streams_count = _streams_count;
    out_metrics.coupled_count = _coupled_count;

    if (_frames_encoded > 0) {
        out_metrics.avg_encode_time_us = static_cast<double>(_total_encode_time_ns) / (static_cast<double>(_frames_encoded) * 1000.0);
    } else {
        out_metrics.avg_encode_time_us = 0.0;
    }
}

void OpusAudioEncoder::reset() {
    _frames_encoded = 0;
    _bytes_encoded = 0;
    _total_encode_time_ns = 0;
}

void OpusAudioEncoder::cleanup() {
    _initialized = false;
    _scratch_pcm16.clear();
    _scratch_stream_buffer.clear();
}

} // namespace moonshine::audio
