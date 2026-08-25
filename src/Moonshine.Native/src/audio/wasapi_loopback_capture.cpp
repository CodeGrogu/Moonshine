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
    if (_channels != 1 && _channels != 2 && _channels != 6 && _channels != 8) {
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
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    cleanup();

#if defined(_WIN32)
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(_enumerator.GetAddressOf())
    );
    if (SUCCEEDED(hr) && _enumerator) {
        hr = _enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &_device);
        if (FAILED(hr) || !_device) {
            hr = _enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &_device);
        }
        if (SUCCEEDED(hr) && _device) {
            hr = _device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(_audio_client.GetAddressOf()));
            if (SUCCEEDED(hr) && _audio_client) {
                WAVEFORMATEX* pMixFormat = nullptr;
                hr = _audio_client->GetMixFormat(&pMixFormat);
                if (SUCCEEDED(hr) && pMixFormat) {
                    _device_channels = pMixFormat->nChannels;
                    _device_sample_rate = pMixFormat->nSamplesPerSec;
                    _bits_per_sample = pMixFormat->wBitsPerSample;

                    _is_float_format = false;
                    if (pMixFormat->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) {
                        _is_float_format = true;
                    } else if (pMixFormat->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
                        auto* pExt = reinterpret_cast<WAVEFORMATEXTENSIBLE*>(pMixFormat);
                        if (IsEqualGUID(pExt->SubFormat, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)) {
                            _is_float_format = true;
                        }
                    }

                    REFERENCE_TIME hnsBufferDuration = static_cast<REFERENCE_TIME>(_buffer_duration_ms) * 10000;
                    if (hnsBufferDuration < 50000) hnsBufferDuration = 50000; // Minimum 5ms buffer

                    hr = _audio_client->Initialize(
                        AUDCLNT_SHAREMODE_SHARED,
                        AUDCLNT_STREAMFLAGS_LOOPBACK,
                        hnsBufferDuration,
                        0,
                        pMixFormat,
                        nullptr
                    );

                    CoTaskMemFree(pMixFormat);

                    if (SUCCEEDED(hr)) {
                        hr = _audio_client->GetService(__uuidof(IAudioCaptureClient), reinterpret_cast<void**>(_capture_client.GetAddressOf()));
                        if (SUCCEEDED(hr) && _capture_client) {
                            _audio_client->Start();
                        }
                    }
                }
            }
        }
    }

    _initialized = true;
    _device_invalidated = false;
    _frame_counter = 0;
    _sample_counter = 0;
    _underruns = 0;
    _overruns = 0;
    _resample_phase = 0.0;
    _last_src_frame.assign(_channels, 0.0f);

    return true;
#else
    _initialized = true;
    _device_invalidated = false;
    _frame_counter = 0;
    _sample_counter = 0;
    _underruns = 0;
    _overruns = 0;
    return true;
#endif
}

bool WasapiLoopbackCapture::recover() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);

#if defined(_WIN32)
    if (_audio_client) {
        _audio_client->Stop();
    }
    _capture_client.Reset();
    _audio_client.Reset();
    _device.Reset();

    if (!_enumerator) {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr_enum = CoCreateInstance(
            __uuidof(MMDeviceEnumerator),
            nullptr,
            CLSCTX_ALL,
            __uuidof(IMMDeviceEnumerator),
            reinterpret_cast<void**>(_enumerator.GetAddressOf())
        );
        if (FAILED(hr_enum) || !_enumerator) {
            _device_invalidated = true;
            return false;
        }
    }

    HRESULT hr = _enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &_device);
    if (FAILED(hr) || !_device) {
        hr = _enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &_device);
    }
    if (FAILED(hr) || !_device) {
        _device_invalidated = true;
        return false;
    }

    hr = _device->Activate(
        __uuidof(IAudioClient),
        CLSCTX_ALL,
        nullptr,
        reinterpret_cast<void**>(_audio_client.GetAddressOf())
    );
    if (FAILED(hr) || !_audio_client) {
        _device_invalidated = true;
        return false;
    }

    WAVEFORMATEX* pMixFormat = nullptr;
    hr = _audio_client->GetMixFormat(&pMixFormat);
    if (FAILED(hr) || !pMixFormat) {
        _device_invalidated = true;
        return false;
    }

    _device_channels = pMixFormat->nChannels;
    _device_sample_rate = pMixFormat->nSamplesPerSec;
    _bits_per_sample = pMixFormat->wBitsPerSample;

    _is_float_format = false;
    if (pMixFormat->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) {
        _is_float_format = true;
    } else if (pMixFormat->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
        auto* pExt = reinterpret_cast<WAVEFORMATEXTENSIBLE*>(pMixFormat);
        if (IsEqualGUID(pExt->SubFormat, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)) {
            _is_float_format = true;
        }
    }

    REFERENCE_TIME hnsBufferDuration = static_cast<REFERENCE_TIME>(_buffer_duration_ms) * 10000;
    if (hnsBufferDuration < 50000) hnsBufferDuration = 50000;

    hr = _audio_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK,
        hnsBufferDuration,
        0,
        pMixFormat,
        nullptr
    );
    CoTaskMemFree(pMixFormat);

    if (FAILED(hr)) {
        _device_invalidated = true;
        return false;
    }

    hr = _audio_client->GetService(
        __uuidof(IAudioCaptureClient),
        reinterpret_cast<void**>(_capture_client.GetAddressOf())
    );
    if (FAILED(hr) || !_capture_client) {
        _device_invalidated = true;
        return false;
    }

    hr = _audio_client->Start();
    if (FAILED(hr)) {
        _device_invalidated = true;
        return false;
    }

    _device_invalidated = false;
    _initialized = true;
    _resample_phase = 0.0;
    _last_src_frame.assign(_channels, 0.0f);
    return true;
#else
    _device_invalidated = false;
    _initialized = true;
    return true;
#endif
}

bool WasapiLoopbackCapture::read_samples_float(
    float* out_samples,
    uint32_t max_samples,
    uint32_t& out_read_samples,
    uint64_t& out_timestamp_qpc
) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_timestamp_qpc = static_cast<uint64_t>(now_ticks);

#if defined(_WIN32)
    const uint32_t target_channels = _channels;
    const uint32_t default_chunk = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * target_channels;
    const uint32_t fallback_count = (std::min)(max_samples, default_chunk == 0 ? 240 * target_channels : default_chunk);

    if (_device_invalidated || !_capture_client) {
        std::memset(out_samples, 0, fallback_count * sizeof(float));
        out_read_samples = fallback_count;
        _sample_counter += (fallback_count / target_channels);
        _frame_counter++;
        return true;
    }

    UINT32 packet_length = 0;
    HRESULT hr = _capture_client->GetNextPacketSize(&packet_length);
    if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
        hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
        hr == AUDCLNT_E_UNSUPPORTED_FORMAT || hr == AUDCLNT_E_DEVICE_IN_USE || 
        hr == AUDCLNT_E_BUFFER_ERROR) {
        _device_invalidated = true;
    }

    if (FAILED(hr) || packet_length == 0) {
        std::memset(out_samples, 0, fallback_count * sizeof(float));
        out_read_samples = fallback_count;
        _sample_counter += (fallback_count / target_channels);
        _frame_counter++;
        return true;
    }

    const uint32_t src_channels = _device_channels > 0 ? _device_channels : target_channels;
    const uint32_t src_rate = _device_sample_rate > 0 ? _device_sample_rate : _sample_rate;
    const uint32_t dst_rate = _sample_rate > 0 ? _sample_rate : 48000;
    const bool needs_resample = (src_rate != dst_rate);
    const double resample_ratio = (dst_rate > 0) ? (static_cast<double>(src_rate) / static_cast<double>(dst_rate)) : 1.0;

    if (_last_src_frame.size() != target_channels) {
        _last_src_frame.assign(target_channels, 0.0f);
    }

    uint32_t total_samples_written = 0;

    while (packet_length > 0 && total_samples_written + target_channels <= max_samples) {
        BYTE* pData = nullptr;
        UINT32 numFramesRead = 0;
        DWORD flags = 0;
        UINT64 devPos = 0;
        UINT64 qpcPos = 0;

        hr = _capture_client->GetBuffer(&pData, &numFramesRead, &flags, &devPos, &qpcPos);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
            hr == AUDCLNT_E_UNSUPPORTED_FORMAT || hr == AUDCLNT_E_DEVICE_IN_USE || 
            hr == AUDCLNT_E_BUFFER_ERROR) {
            _device_invalidated = true;
            break;
        }
        if (FAILED(hr) || !pData || numFramesRead == 0) {
            break;
        }

        if ((flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) && _frame_counter > 0) {
            _underruns++;
        }

        if (qpcPos != 0) {
            out_timestamp_qpc = qpcPos;
        }

        auto extract_src_sample = [&](uint32_t frame_idx, uint32_t ch_idx) -> float {
            if (ch_idx >= src_channels) return 0.0f;
            float val = 0.0f;
            if (_is_float_format) {
                const auto* ptr = reinterpret_cast<const float*>(pData) + (frame_idx * src_channels);
                val = ptr[ch_idx];
            } else if (_bits_per_sample == 16) {
                const auto* ptr = reinterpret_cast<const int16_t*>(pData) + (frame_idx * src_channels);
                val = static_cast<float>(ptr[ch_idx]) / 32768.0f;
            } else if (_bits_per_sample == 24 || _bits_per_sample == 32) {
                const auto* ptr = reinterpret_cast<const int32_t*>(pData) + (frame_idx * src_channels);
                val = static_cast<float>(ptr[ch_idx]) / 2147483648.0f;
            }
            if (std::isnan(val) || std::isinf(val)) return 0.0f;
            return std::clamp(val, -1.0f, 1.0f);
        };

        auto map_src_to_target_channel = [&](uint32_t frame_idx, uint32_t target_ch) -> float {
            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) return 0.0f;
            if (target_channels == 1) {
                if (src_channels == 1) return extract_src_sample(frame_idx, 0);
                return std::clamp(0.5f * (extract_src_sample(frame_idx, 0) + extract_src_sample(frame_idx, 1)), -1.0f, 1.0f);
            }
            if (target_channels == 2) {
                if (src_channels == 1) return extract_src_sample(frame_idx, 0);
                return extract_src_sample(frame_idx, target_ch < src_channels ? target_ch : 0);
            }
            return (target_ch < src_channels) ? extract_src_sample(frame_idx, target_ch) : 0.0f;
        };

        if (!needs_resample) {
            uint32_t frames_to_process = (std::min)(numFramesRead, (max_samples - total_samples_written) / target_channels);
            for (uint32_t f = 0; f < frames_to_process; ++f) {
                uint32_t dst_offset = total_samples_written + (f * target_channels);
                for (uint32_t ch = 0; ch < target_channels; ++ch) {
                    float val = map_src_to_target_channel(f, ch);
                    out_samples[dst_offset + ch] = val;
                    _last_src_frame[ch] = val;
                }
            }
            total_samples_written += frames_to_process * target_channels;
        } else {
            // High-quality linear resampling between 44.1 kHz, 48 kHz, 96 kHz, etc.
            while (total_samples_written + target_channels <= max_samples) {
                double src_pos = _resample_phase;
                if (src_pos >= static_cast<double>(numFramesRead)) {
                    break;
                }

                auto idx = static_cast<uint32_t>(src_pos);
                double frac = src_pos - static_cast<double>(idx);

                for (uint32_t ch = 0; ch < target_channels; ++ch) {
                    float s0 = (idx == 0 && _resample_phase < 1.0) ? _last_src_frame[ch] : map_src_to_target_channel(idx > 0 ? idx - 1 : 0, ch);
                    float s1 = map_src_to_target_channel(idx, ch);
                    float interpolated = static_cast<float>((1.0 - frac) * s0 + frac * s1);
                    if (std::isnan(interpolated) || std::isinf(interpolated)) interpolated = 0.0f;
                    out_samples[total_samples_written + ch] = std::clamp(interpolated, -1.0f, 1.0f);
                }

                total_samples_written += target_channels;
                _resample_phase += resample_ratio;
            }

            if (numFramesRead > 0) {
                for (uint32_t ch = 0; ch < target_channels; ++ch) {
                    _last_src_frame[ch] = map_src_to_target_channel(numFramesRead - 1, ch);
                }
            }
            _resample_phase -= static_cast<double>(numFramesRead);
            if (_resample_phase < 0.0 || std::isnan(_resample_phase) || std::isinf(_resample_phase)) {
                _resample_phase = 0.0;
            }
        }

        hr = _capture_client->ReleaseBuffer(numFramesRead);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
            hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
            _device_invalidated = true;
        }

        if (total_samples_written >= max_samples) {
            break;
        }

        hr = _capture_client->GetNextPacketSize(&packet_length);
        if (FAILED(hr)) {
            if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
                hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
                hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
                _device_invalidated = true;
            }
            break;
        }
    }

    if (total_samples_written == 0) {
        std::memset(out_samples, 0, fallback_count * sizeof(float));
        total_samples_written = fallback_count;
    }

    out_read_samples = total_samples_written;
    _sample_counter += (total_samples_written / target_channels);
    _frame_counter++;

    return true;
#else
    const uint32_t target_channels = _channels;
    const uint32_t count = (std::min)(max_samples, static_cast<uint32_t>(240 * target_channels));
    std::memset(out_samples, 0, count * sizeof(float));
    out_read_samples = count;
    _sample_counter += (count / target_channels);
    _frame_counter++;
    return true;
#endif
}

bool WasapiLoopbackCapture::read_samples_pcm16(
    int16_t* out_samples,
    uint32_t max_samples,
    uint32_t& out_read_samples,
    uint64_t& out_timestamp_qpc
) {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    std::vector<float> float_buffer(max_samples);
    uint32_t samples_read = 0;
    bool ok = read_samples_float(float_buffer.data(), max_samples, samples_read, out_timestamp_qpc);
    if (!ok) {
        return false;
    }

    for (uint32_t i = 0; i < samples_read; ++i) {
        float val = float_buffer[i];
        if (std::isnan(val) || std::isinf(val)) val = 0.0f;
        val = std::clamp(val, -1.0f, 1.0f);
        out_samples[i] = static_cast<int16_t>(val * 32767.0f);
    }

    out_read_samples = samples_read;
    return true;
}

void WasapiLoopbackCapture::get_metrics(AudioCaptureMetrics& out_metrics) const noexcept {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    out_metrics.total_frames_captured = _frame_counter;
    out_metrics.total_samples_captured = _sample_counter;
    out_metrics.underruns = _underruns;
    out_metrics.overruns = _overruns;
    out_metrics.buffer_duration_ms = _buffer_duration_ms;
}

void WasapiLoopbackCapture::cleanup() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    _initialized = false;
    _device_invalidated = false;

#if defined(_WIN32)
    if (_audio_client) {
        _audio_client->Stop();
    }
    _capture_client.Reset();
    _audio_client.Reset();
    _device.Reset();
    _enumerator.Reset();
    _resample_phase = 0.0;
    _last_src_frame.clear();
#endif
}

} // namespace moonshine::audio

