#include "moonshine/audio/wasapi_mic_capture.hpp"
#include <cstring>
#include <cmath>
#include <chrono>
#include <algorithm>

namespace moonshine::audio {

WasapiMicCapture::WasapiMicCapture(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
) : _sample_rate(sample_rate),
    _channels(channels),
    _buffer_duration_ms(buffer_duration_ms)
{
    if (_channels != 1 && _channels != 2) {
        _channels = 1; // Default to Mono for microphone capture
    }
    if (_sample_rate == 0) {
        _sample_rate = 48000;
    }
    if (_buffer_duration_ms == 0) {
        _buffer_duration_ms = 10;
    }
}

WasapiMicCapture::~WasapiMicCapture() {
    cleanup();
}

bool WasapiMicCapture::initialize() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);
    cleanup();

#if defined(_WIN32)
    HRESULT hr_com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (SUCCEEDED(hr_com)) {
        _com_initialized = true;
    } else if (hr_com != RPC_E_CHANGED_MODE) {
        cleanup();
        _initialized = false;
        return false;
    }

    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(_enumerator.GetAddressOf())
    );
    if (FAILED(hr) || !_enumerator) {
        cleanup();
        _initialized = false;
        return false;
    }

    hr = _enumerator->GetDefaultAudioEndpoint(eCapture, eCommunications, &_device);
    if (FAILED(hr) || !_device) {
        hr = _enumerator->GetDefaultAudioEndpoint(eCapture, eConsole, &_device);
    }
    if (FAILED(hr) || !_device) {
        cleanup();
        _initialized = false;
        return false;
    }

    hr = _device->Activate(
        __uuidof(IAudioClient),
        CLSCTX_ALL,
        nullptr,
        reinterpret_cast<void**>(_audio_client.GetAddressOf())
    );
    if (FAILED(hr) || !_audio_client) {
        cleanup();
        _initialized = false;
        return false;
    }

    WAVEFORMATEX* pMixFormat = nullptr;
    hr = _audio_client->GetMixFormat(&pMixFormat);
    if (FAILED(hr) || !pMixFormat) {
        cleanup();
        _initialized = false;
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
    if (hnsBufferDuration < 50000) hnsBufferDuration = 50000; // Minimum 5ms buffer

    hr = _audio_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        0,
        hnsBufferDuration,
        0,
        pMixFormat,
        nullptr
    );
    CoTaskMemFree(pMixFormat);
    if (FAILED(hr)) {
        cleanup();
        _initialized = false;
        return false;
    }

    hr = _audio_client->GetService(
        __uuidof(IAudioCaptureClient),
        reinterpret_cast<void**>(_capture_client.GetAddressOf())
    );
    if (FAILED(hr) || !_capture_client) {
        cleanup();
        _initialized = false;
        return false;
    }

    hr = _audio_client->Start();
    if (FAILED(hr)) {
        cleanup();
        _initialized = false;
        return false;
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

bool WasapiMicCapture::recover() {
    std::lock_guard<std::recursive_mutex> lock(_mutex);

#if defined(_WIN32)
    if (_audio_client) {
        _audio_client->Stop();
    }
    _capture_client.Reset();
    _audio_client.Reset();
    _device.Reset();

    if (!_enumerator) {
        HRESULT hr_com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (SUCCEEDED(hr_com)) {
            _com_initialized = true;
        }
        HRESULT hr_enum = CoCreateInstance(
            __uuidof(MMDeviceEnumerator),
            nullptr,
            CLSCTX_ALL,
            __uuidof(IMMDeviceEnumerator),
            reinterpret_cast<void**>(_enumerator.GetAddressOf())
        );
        if (FAILED(hr_enum) || !_enumerator) {
            _initialized = false;
            return false;
        }
    }

    HRESULT hr = _enumerator->GetDefaultAudioEndpoint(eCapture, eCommunications, &_device);
    if (FAILED(hr) || !_device) {
        hr = _enumerator->GetDefaultAudioEndpoint(eCapture, eConsole, &_device);
    }
    if (FAILED(hr) || !_device) {
        _initialized = false;
        return false;
    }

    hr = _device->Activate(
        __uuidof(IAudioClient),
        CLSCTX_ALL,
        nullptr,
        reinterpret_cast<void**>(_audio_client.GetAddressOf())
    );
    if (FAILED(hr) || !_audio_client) {
        _initialized = false;
        return false;
    }

    WAVEFORMATEX* pMixFormat = nullptr;
    hr = _audio_client->GetMixFormat(&pMixFormat);
    if (FAILED(hr) || !pMixFormat) {
        _initialized = false;
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
    if (hnsBufferDuration < 50000) hnsBufferDuration = 50000; // Minimum 5ms buffer

    hr = _audio_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        0,
        hnsBufferDuration,
        0,
        pMixFormat,
        nullptr
    );
    CoTaskMemFree(pMixFormat);
    if (FAILED(hr)) {
        _initialized = false;
        return false;
    }

    hr = _audio_client->GetService(
        __uuidof(IAudioCaptureClient),
        reinterpret_cast<void**>(_capture_client.GetAddressOf())
    );
    if (FAILED(hr) || !_capture_client) {
        _initialized = false;
        return false;
    }

    hr = _audio_client->Start();
    if (FAILED(hr)) {
        _initialized = false;
        return false;
    }

    _device_invalidated = false;
    _initialized = true;
    return true;
#else
    _device_invalidated = false;
    _initialized = true;
    return true;
#endif
}

bool WasapiMicCapture::read_samples_float(
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
    if (_device_invalidated || !_capture_client) {
        uint32_t chunk_samples = static_cast<uint32_t>((static_cast<uint64_t>(_sample_rate) * _buffer_duration_ms) / 1000) * _channels;
        if (chunk_samples == 0) chunk_samples = 480 * _channels;
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
        if (chunk_samples == 0) chunk_samples = 480 * _channels;
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

                auto get_src_sample = [&](uint32_t c) -> float {
                    if (c >= src_channels) return 0.0f;
                    float val = 0.0f;
                    if (_is_float_format) {
                        const auto* src_ptr = reinterpret_cast<const float*>(pData) + (f * src_channels);
                        val = src_ptr[c];
                    } else if (_bits_per_sample == 16) {
                        const auto* src_ptr = reinterpret_cast<const int16_t*>(pData) + (f * src_channels);
                        val = static_cast<float>(src_ptr[c]) / 32768.0f;
                    } else if (_bits_per_sample == 24 || _bits_per_sample == 32) {
                        const auto* src_ptr = reinterpret_cast<const int32_t*>(pData) + (f * src_channels);
                        val = static_cast<float>(src_ptr[c]) / 2147483648.0f;
                    }
                    if (std::isnan(val) || std::isinf(val)) {
                        return 0.0f;
                    }
                    return std::clamp(val, -1.0f, 1.0f);
                };

                if (target_channels == 1) {
                    if (src_channels == 1) {
                        out_samples[dst_offset] = get_src_sample(0);
                    } else if (src_channels >= 2) {
                        // Downmix stereo/multi-channel to mono
                        float mixed = 0.5f * (get_src_sample(0) + get_src_sample(1));
                        out_samples[dst_offset] = std::clamp(mixed, -1.0f, 1.0f);
                    }
                } else if (target_channels == 2) {
                    if (src_channels == 1) {
                        // Upmix mono to stereo
                        float mono_val = get_src_sample(0);
                        out_samples[dst_offset + 0] = mono_val;
                        out_samples[dst_offset + 1] = mono_val;
                    } else {
                        out_samples[dst_offset + 0] = get_src_sample(0);
                        out_samples[dst_offset + 1] = get_src_sample(1);
                    }
                } else {
                    for (uint32_t ch = 0; ch < target_channels; ++ch) {
                        out_samples[dst_offset + ch] = (ch < src_channels) ? get_src_sample(ch) : 0.0f;
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
        if (chunk_samples == 0) chunk_samples = 480 * _channels;
        uint32_t count = (std::min)(max_samples, chunk_samples);
        std::memset(out_samples, 0, count * sizeof(float));
        total_samples_written = count;
    }

    out_read_samples = total_samples_written;
    _sample_counter += (total_samples_written / target_channels);
    _frame_counter++;

    return true;
#else
    uint32_t count = (std::min)(max_samples, static_cast<uint32_t>(480 * _channels));
    std::memset(out_samples, 0, count * sizeof(float));
    out_read_samples = count;
    _sample_counter += (count / _channels);
    _frame_counter++;
    return true;
#endif
}

void WasapiMicCapture::cleanup() {
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
    if (_com_initialized) {
        CoUninitialize();
        _com_initialized = false;
    }
#endif
}

} // namespace moonshine::audio
