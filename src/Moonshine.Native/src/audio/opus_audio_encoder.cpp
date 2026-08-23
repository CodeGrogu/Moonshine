#include "moonshine/audio/opus_audio_encoder.hpp"

#include <opus.h>
#include <opus_multistream.h>

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
      _encoder(other._encoder),
      _ms_encoder(other._ms_encoder),
      _frames_encoded(other._frames_encoded),
      _bytes_encoded(other._bytes_encoded),
      _total_encode_time_ns(other._total_encode_time_ns) {
    other._encoder = nullptr;
    other._ms_encoder = nullptr;
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
        _encoder = other._encoder;
        _ms_encoder = other._ms_encoder;
        _frames_encoded = other._frames_encoded;
        _bytes_encoded = other._bytes_encoded;
        _total_encode_time_ns = other._total_encode_time_ns;
        other._encoder = nullptr;
        other._ms_encoder = nullptr;
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
            _coupled_count = (_config.channels > 1) ? 1 : 0;
            for (uint32_t i = 0; i < std::min<uint32_t>(_config.channels, 8); ++i) {
                _channel_mapping[i] = static_cast<uint8_t>(i);
            }
            break;
    }
}

bool OpusAudioEncoder::initialize(const OpusEncoderConfig& config) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    cleanup();

    _config = config;
    if (_config.sample_rate == 0) _config.sample_rate = 48000;
    if (_config.channels == 0) _config.channels = 2;
    if (_config.bitrate == 0) _config.bitrate = 160000;
    if (_config.frame_duration_ms == 0) _config.frame_duration_ms = 5;
    if (_config.complexity > 10) _config.complexity = 10;

    configure_channel_mapping();

    int opus_app = OPUS_APPLICATION_RESTRICTED_LOWDELAY;
    switch (_config.application) {
        case OpusApplication::Voip:
            opus_app = OPUS_APPLICATION_VOIP;
            break;
        case OpusApplication::Audio:
            opus_app = OPUS_APPLICATION_AUDIO;
            break;
        case OpusApplication::RestrictedLowDelay:
        default:
            opus_app = OPUS_APPLICATION_RESTRICTED_LOWDELAY;
            break;
    }

    int err = OPUS_OK;
    if (_config.channels <= 2) {
        _encoder = opus_encoder_create(
            static_cast<opus_int32>(_config.sample_rate),
            static_cast<int>(_config.channels),
            opus_app,
            &err
        );
        if (err != OPUS_OK || !_encoder) {
            cleanup();
            return false;
        }

        opus_encoder_ctl(_encoder, OPUS_SET_BITRATE(static_cast<opus_int32>(_config.bitrate)));
        opus_encoder_ctl(_encoder, OPUS_SET_COMPLEXITY(static_cast<opus_int32>(_config.complexity)));
        opus_encoder_ctl(_encoder, OPUS_SET_VBR(_config.use_vbr ? 1 : 0));
        opus_encoder_ctl(_encoder, OPUS_SET_SIGNAL(opus_app == OPUS_APPLICATION_VOIP ? OPUS_SIGNAL_VOICE : OPUS_SIGNAL_MUSIC));
    } else {
        _ms_encoder = opus_multistream_encoder_create(
            static_cast<opus_int32>(_config.sample_rate),
            static_cast<int>(_config.channels),
            static_cast<int>(_streams_count),
            static_cast<int>(_coupled_count),
            _channel_mapping.data(),
            opus_app,
            &err
        );
        if (err != OPUS_OK || !_ms_encoder) {
            cleanup();
            return false;
        }

        opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_BITRATE(static_cast<opus_int32>(_config.bitrate)));
        opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_COMPLEXITY(static_cast<opus_int32>(_config.complexity)));
        opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_VBR(_config.use_vbr ? 1 : 0));
        opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_SIGNAL(opus_app == OPUS_APPLICATION_VOIP ? OPUS_SIGNAL_VOICE : OPUS_SIGNAL_MUSIC));
    }

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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_payload_bytes = 0;
    if (!_initialized || !pcm_samples || !out_payload || frame_samples == 0 || max_payload_bytes == 0) {
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    opus_int32 result = 0;
    if (_encoder) {
        result = opus_encode_float(
            _encoder,
            pcm_samples,
            static_cast<int>(frame_samples),
            out_payload,
            static_cast<opus_int32>(max_payload_bytes)
        );
    } else if (_ms_encoder) {
        result = opus_multistream_encode_float(
            _ms_encoder,
            pcm_samples,
            static_cast<int>(frame_samples),
            out_payload,
            static_cast<opus_int32>(max_payload_bytes)
        );
    } else {
        return false;
    }

    if (result < 0) {
        return false;
    }

    out_payload_bytes = static_cast<uint32_t>(result);

    auto end_time = std::chrono::high_resolution_clock::now();
    uint64_t elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    _frames_encoded++;
    _bytes_encoded += out_payload_bytes;
    _total_encode_time_ns += elapsed_ns;

    return true;
}

bool OpusAudioEncoder::encode_pcm16(
    const int16_t* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t& out_payload_bytes
) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_payload_bytes = 0;
    if (!_initialized || !pcm_samples || !out_payload || frame_samples == 0 || max_payload_bytes == 0) {
        return false;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    opus_int32 result = 0;
    if (_encoder) {
        result = opus_encode(
            _encoder,
            pcm_samples,
            static_cast<int>(frame_samples),
            out_payload,
            static_cast<opus_int32>(max_payload_bytes)
        );
    } else if (_ms_encoder) {
        result = opus_multistream_encode(
            _ms_encoder,
            pcm_samples,
            static_cast<int>(frame_samples),
            out_payload,
            static_cast<opus_int32>(max_payload_bytes)
        );
    } else {
        return false;
    }

    if (result < 0) {
        return false;
    }

    out_payload_bytes = static_cast<uint32_t>(result);

    auto end_time = std::chrono::high_resolution_clock::now();
    uint64_t elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(end_time - start_time).count();

    _frames_encoded++;
    _bytes_encoded += out_payload_bytes;
    _total_encode_time_ns += elapsed_ns;

    return true;
}

bool OpusAudioEncoder::set_bitrate(uint32_t bitrate) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (!_initialized || bitrate == 0) return false;
    _config.bitrate = std::clamp(bitrate, 16000u, 512000u);
    if (_encoder) {
        return opus_encoder_ctl(_encoder, OPUS_SET_BITRATE(static_cast<opus_int32>(_config.bitrate))) == OPUS_OK;
    }
    if (_ms_encoder) {
        return opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_BITRATE(static_cast<opus_int32>(_config.bitrate))) == OPUS_OK;
    }
    return false;
}

bool OpusAudioEncoder::set_complexity(uint32_t complexity) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (!_initialized) return false;
    _config.complexity = std::clamp(complexity, 0u, 10u);
    if (_encoder) {
        return opus_encoder_ctl(_encoder, OPUS_SET_COMPLEXITY(static_cast<opus_int32>(_config.complexity))) == OPUS_OK;
    }
    if (_ms_encoder) {
        return opus_multistream_encoder_ctl(_ms_encoder, OPUS_SET_COMPLEXITY(static_cast<opus_int32>(_config.complexity))) == OPUS_OK;
    }
    return false;
}

void OpusAudioEncoder::get_metrics(OpusEncoderMetrics& out_metrics) const noexcept {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (_encoder) {
        opus_encoder_ctl(_encoder, OPUS_RESET_STATE);
    }
    if (_ms_encoder) {
        opus_multistream_encoder_ctl(_ms_encoder, OPUS_RESET_STATE);
    }
    _frames_encoded = 0;
    _bytes_encoded = 0;
    _total_encode_time_ns = 0;
}

void OpusAudioEncoder::cleanup() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    _initialized = false;
    if (_encoder) {
        opus_encoder_destroy(_encoder);
        _encoder = nullptr;
    }
    if (_ms_encoder) {
        opus_multistream_encoder_destroy(_ms_encoder);
        _ms_encoder = nullptr;
    }
}

} // namespace moonshine::audio
