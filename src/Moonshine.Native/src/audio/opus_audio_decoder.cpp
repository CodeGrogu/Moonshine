#include "moonshine/audio/opus_audio_decoder.hpp"

#include <opus.h>
#include <opus_multistream.h>

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
      _decoder(other._decoder),
      _ms_decoder(other._ms_decoder),
      _frames_decoded(other._frames_decoded),
      _samples_decoded(other._samples_decoded),
      _decode_errors(other._decode_errors),
      _concealment_frames(other._concealment_frames),
      _total_decode_time_ns(other._total_decode_time_ns) {
    other._decoder = nullptr;
    other._ms_decoder = nullptr;
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
        _decoder = other._decoder;
        _ms_decoder = other._ms_decoder;
        _frames_decoded = other._frames_decoded;
        _samples_decoded = other._samples_decoded;
        _decode_errors = other._decode_errors;
        _concealment_frames = other._concealment_frames;
        _total_decode_time_ns = other._total_decode_time_ns;
        other._decoder = nullptr;
        other._ms_decoder = nullptr;
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
        case 6: // Surround 5.1 (Vorbis Layout Family 1: FL, C, FR, RL, RR, LFE)
            _streams_count = 4;
            _coupled_count = 2;
            _channel_mapping[0] = 0; // FL
            _channel_mapping[1] = 4; // C
            _channel_mapping[2] = 1; // FR
            _channel_mapping[3] = 2; // RL
            _channel_mapping[4] = 3; // RR
            _channel_mapping[5] = 5; // LFE
            break;
        case 8: // Surround 7.1 (Vorbis Layout Family 1: FL, C, FR, RL, RR, SL, SR, LFE)
            _streams_count = 5;
            _coupled_count = 3;
            _channel_mapping[0] = 0; // FL
            _channel_mapping[1] = 6; // C
            _channel_mapping[2] = 1; // FR
            _channel_mapping[3] = 2; // RL
            _channel_mapping[4] = 3; // RR
            _channel_mapping[5] = 4; // SL
            _channel_mapping[6] = 5; // SR
            _channel_mapping[7] = 7; // LFE
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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    cleanup();

    _sample_rate = (sample_rate == 0) ? 48000 : sample_rate;
    _channels = (channels == 0) ? 2 : channels;
    if (_channels != 1 && _channels != 2 && _channels != 6 && _channels != 8) {
        _channels = 2;
    }

    configure_channel_mapping();

    int err = OPUS_OK;
    if (_channels <= 2) {
        _decoder = opus_decoder_create(
            static_cast<opus_int32>(_sample_rate),
            static_cast<int>(_channels),
            &err
        );
        if (err != OPUS_OK || !_decoder) {
            cleanup();
            return false;
        }
    } else {
        _ms_decoder = opus_multistream_decoder_create(
            static_cast<opus_int32>(_sample_rate),
            static_cast<int>(_channels),
            static_cast<int>(_streams_count),
            static_cast<int>(_coupled_count),
            _channel_mapping.data(),
            &err
        );
        if (err != OPUS_OK || !_ms_decoder) {
            cleanup();
            return false;
        }
    }

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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_samples_decoded = 0;
    if (!_initialized || !out_pcm_samples || max_samples == 0) {
        _decode_errors++;
        return false;
    }

    int frame_size_per_channel = static_cast<int>(max_samples / _channels);
    if (frame_size_per_channel <= 0) {
        _decode_errors++;
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    int result = 0;
    bool is_loss = (opus_payload == nullptr || payload_bytes == 0);

    if (_decoder) {
        result = opus_decode_float(
            _decoder,
            is_loss ? nullptr : opus_payload,
            is_loss ? 0 : static_cast<opus_int32>(payload_bytes),
            out_pcm_samples,
            frame_size_per_channel,
            decode_fec
        );
    } else if (_ms_decoder) {
        result = opus_multistream_decode_float(
            _ms_decoder,
            is_loss ? nullptr : opus_payload,
            is_loss ? 0 : static_cast<opus_int32>(payload_bytes),
            out_pcm_samples,
            frame_size_per_channel,
            decode_fec
        );
    } else {
        _decode_errors++;
        return false;
    }

    if (result < 0) {
        _decode_errors++;
        return false;
    }

    out_samples_decoded = static_cast<uint32_t>(result) * _channels;

    auto end_time = std::chrono::high_resolution_clock::now();
    uint64_t elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    if (is_loss || decode_fec != 0) {
        _concealment_frames++;
    }
    _frames_decoded++;
    _samples_decoded += out_samples_decoded;
    _total_decode_time_ns += elapsed_ns;

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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_samples_decoded = 0;
    if (!_initialized || !out_pcm_samples || max_samples == 0) {
        _decode_errors++;
        return false;
    }

    int frame_size_per_channel = static_cast<int>(max_samples / _channels);
    if (frame_size_per_channel <= 0) {
        _decode_errors++;
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    int result = 0;
    bool is_loss = (opus_payload == nullptr || payload_bytes == 0);

    if (_decoder) {
        result = opus_decode(
            _decoder,
            is_loss ? nullptr : opus_payload,
            is_loss ? 0 : static_cast<opus_int32>(payload_bytes),
            out_pcm_samples,
            frame_size_per_channel,
            decode_fec
        );
    } else if (_ms_decoder) {
        result = opus_multistream_decode(
            _ms_decoder,
            is_loss ? nullptr : opus_payload,
            is_loss ? 0 : static_cast<opus_int32>(payload_bytes),
            out_pcm_samples,
            frame_size_per_channel,
            decode_fec
        );
    } else {
        _decode_errors++;
        return false;
    }

    if (result < 0) {
        _decode_errors++;
        return false;
    }

    out_samples_decoded = static_cast<uint32_t>(result) * _channels;

    auto end_time = std::chrono::high_resolution_clock::now();
    uint64_t elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    if (is_loss || decode_fec != 0) {
        _concealment_frames++;
    }
    _frames_decoded++;
    _samples_decoded += out_samples_decoded;
    _total_decode_time_ns += elapsed_ns;

    return true;
}

void OpusAudioDecoder::get_metrics(OpusDecoderMetrics& out_metrics) const noexcept {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_metrics.total_frames_decoded = _frames_decoded;
    out_metrics.total_samples_decoded = _samples_decoded;
    out_metrics.decode_errors = _decode_errors;
    out_metrics.concealment_frames = _concealment_frames;
    out_metrics.streams_count = _streams_count;
    out_metrics.coupled_count = _coupled_count;

    if (_frames_decoded > 0) {
        out_metrics.avg_decode_time_us = static_cast<double>(_total_decode_time_ns) / (static_cast<double>(_frames_decoded) * 1000.0);
    } else {
        out_metrics.avg_decode_time_us = 0.0;
    }
}

void OpusAudioDecoder::reset() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (_decoder) {
        opus_decoder_ctl(_decoder, OPUS_RESET_STATE);
    }
    if (_ms_decoder) {
        opus_multistream_decoder_ctl(_ms_decoder, OPUS_RESET_STATE);
    }
    _frames_decoded = 0;
    _samples_decoded = 0;
    _decode_errors = 0;
    _concealment_frames = 0;
    _total_decode_time_ns = 0;
}

void OpusAudioDecoder::cleanup() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    _initialized = false;
    if (_decoder) {
        opus_decoder_destroy(_decoder);
        _decoder = nullptr;
    }
    if (_ms_decoder) {
        opus_multistream_decoder_destroy(_ms_decoder);
        _ms_decoder = nullptr;
    }
}

} // namespace moonshine::audio
