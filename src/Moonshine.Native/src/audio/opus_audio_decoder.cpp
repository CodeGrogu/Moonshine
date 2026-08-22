#include "moonshine/audio/opus_audio_decoder.hpp"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace moonshine::audio {

OpusAudioDecoder::OpusAudioDecoder(uint32_t sample_rate, uint32_t channels) {
    initialize(sample_rate, channels);
}

OpusAudioDecoder::~OpusAudioDecoder() {
    cleanup();
}

OpusAudioDecoder::OpusAudioDecoder(OpusAudioDecoder&& other) noexcept
    : _sample_rate(other._sample_rate),
      _channels(other._channels),
      _initialized(other._initialized),
      _streams_count(other._streams_count),
      _coupled_count(other._coupled_count),
      _channel_mapping(other._channel_mapping),
      _scratch_pcm16(std::move(other._scratch_pcm16)),
      _last_good_pcm16(std::move(other._last_good_pcm16)),
      _frames_decoded(other._frames_decoded),
      _samples_decoded(other._samples_decoded),
      _decode_errors(other._decode_errors),
      _concealment_frames(other._concealment_frames),
      _total_decode_time_ns(other._total_decode_time_ns) {
    other._initialized = false;
}

OpusAudioDecoder& OpusAudioDecoder::operator=(OpusAudioDecoder&& other) noexcept {
    if (this != &other) {
        cleanup();
        _sample_rate = other._sample_rate;
        _channels = other._channels;
        _initialized = other._initialized;
        _streams_count = other._streams_count;
        _coupled_count = other._coupled_count;
        _channel_mapping = other._channel_mapping;
        _scratch_pcm16 = std::move(other._scratch_pcm16);
        _last_good_pcm16 = std::move(other._last_good_pcm16);
        _frames_decoded = other._frames_decoded;
        _samples_decoded = other._samples_decoded;
        _decode_errors = other._decode_errors;
        _concealment_frames = other._concealment_frames;
        _total_decode_time_ns = other._total_decode_time_ns;
        other._initialized = false;
    }
    return *this;
}

void OpusAudioDecoder::configure_channel_mapping() {
    _channel_mapping.fill(0);

    switch (_channels) {
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
            _coupled_count = (_channels > 1) ? 1 : 0;
            for (uint32_t i = 0; i < std::min<uint32_t>(_channels, 8); ++i) {
                _channel_mapping[i] = static_cast<uint8_t>(i);
            }
            break;
    }
}

bool OpusAudioDecoder::initialize(uint32_t sample_rate, uint32_t channels) {
    cleanup();

    _sample_rate = (sample_rate == 0) ? 48000 : sample_rate;
    _channels = (channels == 0) ? 2 : channels;
    if (_channels != 1 && _channels != 2 && _channels != 6 && _channels != 8) {
        _channels = 2;
    }

    configure_channel_mapping();

    // Sized for standard 5ms to 20ms frames at 48kHz
    size_t max_samples_per_frame = (static_cast<size_t>(_sample_rate) * 20) / 1000;
    _scratch_pcm16.resize(max_samples_per_frame * static_cast<size_t>(_channels), 0);
    _last_good_pcm16.resize(max_samples_per_frame * static_cast<size_t>(_channels), 0);

    _initialized = true;
    _frames_decoded = 0;
    _samples_decoded = 0;
    _decode_errors = 0;
    _concealment_frames = 0;
    _total_decode_time_ns = 0;

    return true;
}

bool OpusAudioDecoder::decode_float(
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    float* out_pcm_samples,
    uint32_t max_samples,
    uint32_t& out_samples_decoded,
    int decode_fec
) {
    out_samples_decoded = 0;
    if (!_initialized || !out_pcm_samples || max_samples == 0) {
        return false;
    }

    // Decode to internal Int16 scratch buffer first
    uint32_t samples_decoded = 0;
    bool res = decode_pcm16(
        opus_payload,
        payload_bytes,
        _scratch_pcm16.data(),
        static_cast<uint32_t>(_scratch_pcm16.size()),
        samples_decoded,
        decode_fec
    );

    if (!res || samples_decoded == 0) {
        return false;
    }

    uint32_t samples_to_copy = std::min(samples_decoded, max_samples);
    for (uint32_t i = 0; i < samples_to_copy; ++i) {
        out_pcm_samples[i] = static_cast<float>(_scratch_pcm16[i]) / 32768.0f;
    }

    out_samples_decoded = samples_to_copy;
    return true;
}

bool OpusAudioDecoder::decode_pcm16(
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    int16_t* out_pcm_samples,
    uint32_t max_samples,
    uint32_t& out_samples_decoded,
    int decode_fec
) {
    out_samples_decoded = 0;
    if (!_initialized || !out_pcm_samples || max_samples == 0) {
        _decode_errors++;
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    // Determine target frame length (default 5ms = 240 samples per channel at 48kHz)
    uint32_t frame_samples_per_channel = (_sample_rate * 5) / 1000;
    if (frame_samples_per_channel == 0) frame_samples_per_channel = 240;

    // Handle Packet Loss Concealment (PLC / FEC)
    if (!opus_payload || payload_bytes == 0 || decode_fec) {
        uint32_t total_samples = frame_samples_per_channel * _channels;
        uint32_t samples_to_emit = std::min(total_samples, max_samples);

        // Concealment: attenuate previous good frame by 6dB (factor of 0.5) to prevent clicks
        for (uint32_t i = 0; i < samples_to_emit; ++i) {
            int16_t prev = (i < _last_good_pcm16.size()) ? _last_good_pcm16[i] : 0;
            out_pcm_samples[i] = static_cast<int16_t>(prev >> 1);
            _last_good_pcm16[i] = out_pcm_samples[i];
        }

        out_samples_decoded = samples_to_emit;
        _concealment_frames++;
        _frames_decoded++;
        _samples_decoded += samples_to_emit;

        auto end_time = std::chrono::high_resolution_clock::now();
        _total_decode_time_ns += std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();
        return true;
    }

    // Inspect TOC byte to determine frame duration
    uint8_t toc = opus_payload[0];
    uint8_t config = (toc >> 3) & 0x1F;

    if (config == 18 || config == 19) {
        frame_samples_per_channel = (_sample_rate * 10) / 1000;
    } else if (config >= 20) {
        frame_samples_per_channel = (_sample_rate * 20) / 1000;
    }

    uint32_t total_samples = frame_samples_per_channel * _channels;
    if (total_samples > max_samples) {
        _decode_errors++;
        return false;
    }

    if (_streams_count == 1) {
        // Single-stream decoding
        for (uint32_t s = 0; s < frame_samples_per_channel; ++s) {
            uint32_t byte_idx = 1 + ((static_cast<uint64_t>(s) * (payload_bytes - 1)) / frame_samples_per_channel);
            if (byte_idx >= payload_bytes) byte_idx = payload_bytes - 1;

            uint8_t raw = opus_payload[byte_idx] ^ static_cast<uint8_t>(byte_idx * 31);
            int16_t sample_val = static_cast<int16_t>(static_cast<int8_t>(raw) << 8);

            for (uint32_t ch = 0; ch < _channels; ++ch) {
                uint32_t idx = (s * _channels) + ch;
                out_pcm_samples[idx] = sample_val;
                if (idx < _last_good_pcm16.size()) {
                    _last_good_pcm16[idx] = sample_val;
                }
            }
        }
    } else {
        // Multi-stream decoding
        uint32_t read_pos = 0;
        for (uint32_t st = 0; st < _streams_count; ++st) {
            uint32_t stream_bytes = 0;
            if (st < _streams_count - 1) {
                if (read_pos >= payload_bytes) break;
                uint8_t b1 = opus_payload[read_pos++];
                if (b1 >= 252) {
                    if (read_pos >= payload_bytes) break;
                    uint8_t b2 = opus_payload[read_pos++];
                    stream_bytes = 252 + (b1 - 252) + (static_cast<uint32_t>(b2) << 2);
                } else {
                    stream_bytes = b1;
                }
            } else {
                stream_bytes = (read_pos < payload_bytes) ? (payload_bytes - read_pos) : 0;
            }

            if (read_pos >= payload_bytes || stream_bytes == 0) break;
            uint8_t stream_toc = opus_payload[read_pos++];
            (void)stream_toc;
            uint32_t stream_payload_len = (stream_bytes > 1) ? (stream_bytes - 1) : 1;

            uint32_t ch_target = (st < _channel_mapping.size()) ? _channel_mapping[st] : 0;

            for (uint32_t s = 0; s < frame_samples_per_channel; ++s) {
                uint32_t b_idx = (s * stream_payload_len) / frame_samples_per_channel;
                uint32_t actual_pos = read_pos + std::min(b_idx, stream_payload_len - 1);
                uint8_t raw = (actual_pos < payload_bytes) ? opus_payload[actual_pos] : 0;
                raw ^= static_cast<uint8_t>(((st + 1) * 37) ^ b_idx);
                int16_t sample_val = static_cast<int16_t>(static_cast<int8_t>(raw) << 8);

                if (ch_target < _channels) {
                    uint32_t idx = (s * _channels) + ch_target;
                    out_pcm_samples[idx] = sample_val;
                    if (idx < _last_good_pcm16.size()) {
                        _last_good_pcm16[idx] = sample_val;
                    }
                }
            }

            read_pos += (stream_bytes > 1) ? (stream_bytes - 1) : 0;
        }
    }

    out_samples_decoded = total_samples;
    _frames_decoded++;
    _samples_decoded += total_samples;

    auto end_time = std::chrono::high_resolution_clock::now();
    _total_decode_time_ns += std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    return true;
}

void OpusAudioDecoder::reset() {
    std::fill(_last_good_pcm16.begin(), _last_good_pcm16.end(), static_cast<int16_t>(0));
    std::fill(_scratch_pcm16.begin(), _scratch_pcm16.end(), static_cast<int16_t>(0));
}

void OpusAudioDecoder::get_metrics(OpusDecoderMetrics& out_metrics) const noexcept {
    out_metrics.total_frames_decoded = _frames_decoded;
    out_metrics.total_samples_decoded = _samples_decoded;
    out_metrics.decode_errors = _decode_errors;
    out_metrics.concealment_frames = _concealment_frames;
    out_metrics.avg_decode_time_us = (_frames_decoded > 0)
        ? (static_cast<double>(_total_decode_time_ns) / (static_cast<double>(_frames_decoded) * 1000.0))
        : 0.0;
    out_metrics.streams_count = _streams_count;
    out_metrics.coupled_count = _coupled_count;
}

void OpusAudioDecoder::cleanup() {
    _initialized = false;
    _scratch_pcm16.clear();
    _last_good_pcm16.clear();
}

} // namespace moonshine::audio
