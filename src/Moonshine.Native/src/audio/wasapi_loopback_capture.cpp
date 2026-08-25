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
    _buffer_duration_ms(buffer_duration_ms),
    _resampler(sample_rate == 0 ? 48000 : sample_rate, sample_rate == 0 ? 48000 : sample_rate, channels == 0 ? 2 : channels)
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

    _resampler.Configure(_device_sample_rate > 0 ? _device_sample_rate : _sample_rate, _sample_rate, _channels);
    _initialized = true;
    _device_invalidated = false;
    _frame_counter = 0;
    _sample_counter = 0;
    _underruns = 0;
    _overruns = 0;

    return true;
#else
    _resampler.Configure(_sample_rate, _sample_rate, _channels);
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

    _resampler.Configure(_device_sample_rate > 0 ? _device_sample_rate : _sample_rate, _sample_rate, _channels);
    _device_invalidated = false;
    _initialized = true;
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

    const uint32_t src_channels = _device_channels > 0 ? _device_channels : target_channels;

    while (packet_length > 0) {
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

        // 1. Extract raw device float samples
        _raw_capture_buffer.resize(static_cast<size_t>(numFramesRead) * src_channels);
        if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
            std::memset(_raw_capture_buffer.data(), 0, _raw_capture_buffer.size() * sizeof(float));
        } else {
            for (uint32_t f = 0; f < numFramesRead; ++f) {
                for (uint32_t ch = 0; ch < src_channels; ++ch) {
                    float val = 0.0f;
                    if (_is_float_format) {
                        const auto* ptr = reinterpret_cast<const float*>(pData) + (f * src_channels);
                        val = ptr[ch];
                    } else if (_bits_per_sample == 16) {
                        const auto* ptr = reinterpret_cast<const int16_t*>(pData) + (f * src_channels);
                        val = static_cast<float>(ptr[ch]) / 32768.0f;
                    } else if (_bits_per_sample == 24 || _bits_per_sample == 32) {
                        const auto* ptr = reinterpret_cast<const int32_t*>(pData) + (f * src_channels);
                        val = static_cast<float>(ptr[ch]) / 2147483648.0f;
                    }
                    if (std::isnan(val) || std::isinf(val)) val = 0.0f;
                    _raw_capture_buffer[f * src_channels + ch] = std::clamp(val, -1.0f, 1.0f);
                }
            }
        }

        // 2. Channel conversion (device_channels -> target_channels)
        _channel_staging_buffer.resize(static_cast<size_t>(numFramesRead) * target_channels);
        ChannelConverter::Convert(
            _raw_capture_buffer.data(),
            src_channels,
            numFramesRead,
            _channel_staging_buffer.data(),
            target_channels
        );

        // 3. Push converted samples to resampler FIFO
        _resampler.PushInput(_channel_staging_buffer.data(), numFramesRead);

        hr = _capture_client->ReleaseBuffer(numFramesRead);
        if (FAILED(hr)) {
            _device_invalidated = true;
            break;
        }

        hr = _capture_client->GetNextPacketSize(&packet_length);
        if (FAILED(hr)) {
            _device_invalidated = true;
            break;
        }
    }

    // Drain up to max_samples from resampler FIFO
    size_t target_frames = max_samples / target_channels;
    size_t generated_frames = _resampler.Resample(out_samples, target_frames);
    uint32_t total_samples_written = static_cast<uint32_t>(generated_frames * target_channels);

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
    _resampler.Reset();
    _raw_capture_buffer.clear();
    _channel_staging_buffer.clear();
#endif
}

} // namespace moonshine::audio
