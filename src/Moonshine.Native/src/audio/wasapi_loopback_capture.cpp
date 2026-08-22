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

bool WasapiLoopbackCapture::read_samples_float(
    float* out_samples,
    uint32_t max_samples,
    uint32_t& out_read_samples,
    uint64_t& out_timestamp_qpc
) {
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_timestamp_qpc = static_cast<uint64_t>(now_ticks);

#if defined(_WIN32)
    if (_device_invalidated || !_capture_client) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(float));
        out_read_samples = count;
        _sample_counter += (count / _channels);
        _frame_counter++;
        return true;
    }

    UINT32 packet_length = 0;
    HRESULT hr = _capture_client->GetNextPacketSize(&packet_length);
    if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED) {
        _device_invalidated = true;
    }

    if (FAILED(hr) || packet_length == 0) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(float));
        out_read_samples = count;
        _sample_counter += (count / _channels);
        _frame_counter++;
        return true;
    }

    uint32_t target_channels = _channels;
    uint32_t src_channels = _device_channels > 0 ? _device_channels : target_channels;
    uint32_t total_samples_written = 0;

    while (packet_length > 0 && total_samples_written + target_channels <= max_samples) {
        BYTE* pData = nullptr;
        UINT32 numFramesRead = 0;
        DWORD flags = 0;
        UINT64 devPos = 0;
        UINT64 qpcPos = 0;

        hr = _capture_client->GetBuffer(&pData, &numFramesRead, &flags, &devPos, &qpcPos);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED) {
            _device_invalidated = true;
            break;
        }
        if (FAILED(hr) || !pData) {
            break;
        }

        if ((flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) && _frame_counter > 0) {
            _underruns++;
        }

        if (qpcPos != 0) {
            out_timestamp_qpc = qpcPos;
        }

        uint32_t frames_to_process = (std::min)(numFramesRead, (max_samples - total_samples_written) / target_channels);

        if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
            std::memset(out_samples + total_samples_written, 0, frames_to_process * target_channels * sizeof(float));
        } else {
            for (uint32_t f = 0; f < frames_to_process; ++f) {
                uint32_t dst_offset = total_samples_written + (f * target_channels);

                if (_is_float_format) {
                    const auto* src_ptr = reinterpret_cast<const float*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        float val = (ch < src_channels) ? src_ptr[ch] : 0.0f;
                        if (std::isnan(val) || std::isinf(val)) {
                            val = 0.0f;
                        } else {
                            val = std::clamp(val, -1.0f, 1.0f);
                        }
                        out_samples[dst_offset + ch] = val;
                    }
                } else if (_bits_per_sample == 16) {
                    const auto* src_ptr = reinterpret_cast<const int16_t*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = (ch < src_channels) ? (static_cast<float>(src_ptr[ch]) / 32768.0f) : 0.0f;
                    }
                } else if (_bits_per_sample == 24 || _bits_per_sample == 32) {
                    const auto* src_ptr = reinterpret_cast<const int32_t*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = (ch < src_channels) ? (static_cast<float>(src_ptr[ch]) / 2147483648.0f) : 0.0f;
                    }
                } else {
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = 0.0f;
                    }
                }
            }
        }

        total_samples_written += frames_to_process * target_channels;
        _capture_client->ReleaseBuffer(numFramesRead);

        if (total_samples_written >= max_samples) {
            break;
        }

        hr = _capture_client->GetNextPacketSize(&packet_length);
        if (FAILED(hr)) {
            break;
        }
    }

    if (total_samples_written == 0) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(float));
        total_samples_written = count;
    }

    out_read_samples = total_samples_written;
    _sample_counter += (total_samples_written / target_channels);
    _frame_counter++;

    return true;
#else
    uint32_t count = (std::min)(max_samples, static_cast<uint32_t>(240 * _channels));
    std::memset(out_samples, 0, count * sizeof(float));
    out_read_samples = count;
    _sample_counter += (count / _channels);
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
    if (!_initialized || !out_samples || max_samples == 0) {
        return false;
    }

    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_timestamp_qpc = static_cast<uint64_t>(now_ticks);

#if defined(_WIN32)
    if (_device_invalidated || !_capture_client) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(int16_t));
        out_read_samples = count;
        _sample_counter += (count / _channels);
        _frame_counter++;
        return true;
    }

    UINT32 packet_length = 0;
    HRESULT hr = _capture_client->GetNextPacketSize(&packet_length);
    if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED) {
        _device_invalidated = true;
    }

    if (FAILED(hr) || packet_length == 0) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(int16_t));
        out_read_samples = count;
        _sample_counter += (count / _channels);
        _frame_counter++;
        return true;
    }

    uint32_t target_channels = _channels;
    uint32_t src_channels = _device_channels > 0 ? _device_channels : target_channels;
    uint32_t total_samples_written = 0;

    while (packet_length > 0 && total_samples_written + target_channels <= max_samples) {
        BYTE* pData = nullptr;
        UINT32 numFramesRead = 0;
        DWORD flags = 0;
        UINT64 devPos = 0;
        UINT64 qpcPos = 0;

        hr = _capture_client->GetBuffer(&pData, &numFramesRead, &flags, &devPos, &qpcPos);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED) {
            _device_invalidated = true;
            break;
        }
        if (FAILED(hr) || !pData) {
            break;
        }

        if ((flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) && _frame_counter > 0) {
            _underruns++;
        }

        if (qpcPos != 0) {
            out_timestamp_qpc = qpcPos;
        }

        uint32_t frames_to_process = (std::min)(numFramesRead, (max_samples - total_samples_written) / target_channels);

        if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
            std::memset(out_samples + total_samples_written, 0, frames_to_process * target_channels * sizeof(int16_t));
        } else {
            for (uint32_t f = 0; f < frames_to_process; ++f) {
                uint32_t dst_offset = total_samples_written + (f * target_channels);

                if (_is_float_format) {
                    const auto* src_ptr = reinterpret_cast<const float*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        float val = (ch < src_channels) ? src_ptr[ch] : 0.0f;
                        if (std::isnan(val) || std::isinf(val)) {
                            val = 0.0f;
                        } else {
                            val = std::clamp(val, -1.0f, 1.0f);
                        }
                        out_samples[dst_offset + ch] = static_cast<int16_t>(val * 32767.0f);
                    }
                } else if (_bits_per_sample == 16) {
                    const auto* src_ptr = reinterpret_cast<const int16_t*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = (ch < src_channels) ? src_ptr[ch] : 0;
                    }
                } else if (_bits_per_sample == 24 || _bits_per_sample == 32) {
                    const auto* src_ptr = reinterpret_cast<const int32_t*>(pData) + (f * src_channels);
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = (ch < src_channels) ? static_cast<int16_t>(src_ptr[ch] >> 16) : 0;
                    }
                } else {
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = 0;
                    }
                }
            }
        }

        total_samples_written += frames_to_process * target_channels;
        _capture_client->ReleaseBuffer(numFramesRead);

        if (total_samples_written >= max_samples) {
            break;
        }

        hr = _capture_client->GetNextPacketSize(&packet_length);
        if (FAILED(hr)) {
            break;
        }
    }

    if (total_samples_written == 0) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 240 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(int16_t));
        total_samples_written = count;
    }

    out_read_samples = total_samples_written;
    _sample_counter += (total_samples_written / target_channels);
    _frame_counter++;

    return true;
#else
    uint32_t count = (std::min)(max_samples, static_cast<uint32_t>(240 * _channels));
    std::memset(out_samples, 0, count * sizeof(int16_t));
    out_read_samples = count;
    _sample_counter += (count / _channels);
    _frame_counter++;
    return true;
#endif
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
    _device_invalidated = false;

#if defined(_WIN32)
    if (_audio_client) {
        _audio_client->Stop();
    }
    _capture_client.Reset();
    _audio_client.Reset();
    _device.Reset();
    _enumerator.Reset();
#endif
}

} // namespace moonshine::audio
