#include "moonshine/audio/wasapi_renderer.hpp"
#include <cstring>
#include <algorithm>
#include <cmath>

#if defined(_WIN32)
    #include <windows.h>
    #include <mmdeviceapi.h>
    #include <audioclient.h>
    #include <avrt.h>
    #include <wrl/client.h>
    #include <ksmedia.h>

    using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::audio {

WasapiRenderer::WasapiRenderer(uint32_t sample_rate, uint16_t channels, bool exclusive)
    : sample_rate_(sample_rate == 0 ? 48000 : sample_rate),
      channels_(channels == 0 ? 2 : channels),
      exclusive_(exclusive),
      resampler_(sample_rate_, sample_rate_, channels_) {
}

WasapiRenderer::~WasapiRenderer() {
    Shutdown();
}

int WasapiRenderer::Initialize() {
    if (sample_rate_ == 0 || channels_ == 0) return -1;
    Shutdown();

#if defined(_WIN32)
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    int res = SetupAudioClientLocked();
    if (res != 0) {
        state_ = WasapiState::Faulted;
        return -1;
    }
#else
    state_ = WasapiState::Running;
#endif

    frames_rendered_ = 0;
    underruns_ = 0;
    recovery_attempts_ = 0;
    return 0;
}

int WasapiRenderer::SetupAudioClientLocked() {
#if defined(_WIN32)
    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(enumerator_.GetAddressOf())
    );

    if (FAILED(hr) || !enumerator_) {
        return -1;
    }

    hr = enumerator_->GetDefaultAudioEndpoint(eRender, eMultimedia, &device_);
    if (FAILED(hr) || !device_) {
        hr = enumerator_->GetDefaultAudioEndpoint(eRender, eConsole, &device_);
    }
    if (FAILED(hr) || !device_) {
        return -1;
    }

    hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
    if (FAILED(hr) || !audio_client_) {
        return -1;
    }

    REFERENCE_TIME default_period = 100000;
    REFERENCE_TIME min_period = 30000;
    hr = audio_client_->GetDevicePeriod(&default_period, &min_period);

    WAVEFORMATEX* pMixFormat = nullptr;
    audio_client_->GetMixFormat(&pMixFormat);

    WAVEFORMATEXTENSIBLE wfx = {};
    wfx.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    wfx.Format.nChannels = channels_;
    wfx.Format.nSamplesPerSec = sample_rate_;
    wfx.Format.wBitsPerSample = 32;
    wfx.Format.nBlockAlign = (wfx.Format.nChannels * wfx.Format.wBitsPerSample) / 8;
    wfx.Format.nAvgBytesPerSec = wfx.Format.nSamplesPerSec * wfx.Format.nBlockAlign;
    wfx.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
    wfx.Samples.wValidBitsPerSample = 32;
    wfx.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;

    if (channels_ == 2) {
        wfx.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    } else if (channels_ == 6) {
        wfx.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER |
                            SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT;
    } else if (channels_ == 8) {
        wfx.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER |
                            SPEAKER_LOW_FREQUENCY | SPEAKER_BACK_LEFT | SPEAKER_BACK_RIGHT |
                            SPEAKER_SIDE_LEFT | SPEAKER_SIDE_RIGHT;
    } else {
        wfx.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    }

    device_channels_ = channels_;
    device_sample_rate_ = sample_rate_;
    bits_per_sample_ = 32;
    is_float_format_ = true;

    bool init_success = false;

    if (exclusive_) {
        // Query exclusive-mode format support
        hr = audio_client_->IsFormatSupported(AUDCLNT_SHAREMODE_EXCLUSIVE, reinterpret_cast<WAVEFORMATEX*>(&wfx), nullptr);
        if (SUCCEEDED(hr)) {
            REFERENCE_TIME buffer_duration = min_period > 0 ? min_period : 30000;
            hr = audio_client_->Initialize(
                AUDCLNT_SHAREMODE_EXCLUSIVE,
                AUDCLNT_STREAMFLAGS_NOPERSIST,
                buffer_duration,
                buffer_duration,
                reinterpret_cast<WAVEFORMATEX*>(&wfx),
                nullptr
            );
            if (SUCCEEDED(hr)) {
                init_success = true;
            }
        }
    }

    if (!init_success) {
        // Fallback to Shared Mode negotiation
        audio_client_.Reset();
        hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
        if (SUCCEEDED(hr) && audio_client_) {
            exclusive_ = false;
            WAVEFORMATEX* target_fmt = (pMixFormat != nullptr) ? pMixFormat : reinterpret_cast<WAVEFORMATEX*>(&wfx);

            hr = audio_client_->Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                default_period > 0 ? default_period : 100000,
                0,
                target_fmt,
                nullptr
            );

            if (SUCCEEDED(hr)) {
                init_success = true;
                if (target_fmt) {
                    device_channels_ = target_fmt->nChannels;
                    device_sample_rate_ = target_fmt->nSamplesPerSec;
                    bits_per_sample_ = target_fmt->wBitsPerSample;

                    is_float_format_ = false;
                    if (target_fmt->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) {
                        is_float_format_ = true;
                    } else if (target_fmt->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
                        auto* pExt = reinterpret_cast<WAVEFORMATEXTENSIBLE*>(target_fmt);
                        if (IsEqualGUID(pExt->SubFormat, KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)) {
                            is_float_format_ = true;
                        }
                    }
                }
            }
        }
    }

    if (pMixFormat) {
        CoTaskMemFree(pMixFormat);
        pMixFormat = nullptr;
    }

    if (!init_success || !audio_client_) {
        return -1;
    }

    audio_client_->GetBufferSize(&buffer_frame_count_);
    hr = audio_client_->GetService(__uuidof(IAudioRenderClient), reinterpret_cast<void**>(render_client_.GetAddressOf()));
    if (FAILED(hr) || !render_client_) {
        return -1;
    }

    hr = audio_client_->Start();
    if (FAILED(hr)) {
        return -1;
    }

    // Configure resampler with persistent FIFO
    resampler_.Configure(sample_rate_, device_sample_rate_, device_channels_);

    DWORD task_index = 0;
    AvSetMmThreadCharacteristicsW(L"Pro Audio", &task_index);

    state_ = WasapiState::Running;
    return 0;
#else
    state_ = WasapiState::Running;
    return 0;
#endif
}

int WasapiRenderer::Recover() {
    if (sample_rate_ == 0 || channels_ == 0) return -1;

    state_ = WasapiState::Recovering;
    if (++recovery_attempts_ > 10) {
        state_ = WasapiState::Faulted;
        return -1;
    }

#if defined(_WIN32)
    if (audio_client_) {
        audio_client_->Stop();
    }
    render_client_.Reset();
    audio_client_.Reset();
    device_.Reset();

    int res = SetupAudioClientLocked();
    if (res == 0) {
        recovery_attempts_ = 0;
        state_ = WasapiState::Running;
        return 0;
    } else {
        state_ = WasapiState::DeviceLost;
        return -1;
    }
#else
    state_ = WasapiState::Running;
    recovery_attempts_ = 0;
    return 0;
#endif
}

int WasapiRenderer::SubmitPcm(const float* pcm_data, uint32_t sample_count) {
    if (state_ != WasapiState::Running || !pcm_data || sample_count == 0) {
        return -1;
    }

#if defined(_WIN32)
    if (render_client_ && audio_client_) {
        UINT32 padding = 0;
        HRESULT hr = audio_client_->GetCurrentPadding(&padding);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
            hr == AUDCLNT_E_UNSUPPORTED_FORMAT || hr == AUDCLNT_E_DEVICE_IN_USE || 
            hr == AUDCLNT_E_BUFFER_ERROR) {
            state_ = WasapiState::DeviceLost;
            return -1;
        }

        if (SUCCEEDED(hr)) {
            // 1. Channel layout conversion (source channels -> device channels)
            channel_staging_buffer_.resize(static_cast<size_t>(sample_count) * device_channels_);
            ChannelConverter::Convert(
                pcm_data,
                channels_,
                sample_count,
                channel_staging_buffer_.data(),
                device_channels_
            );

            // 2. Push converted frames into persistent resampler FIFO
            resampler_.PushInput(channel_staging_buffer_.data(), sample_count);

            // 3. Determine available frames in WASAPI output buffer
            UINT32 frames_available = (buffer_frame_count_ > padding) ? (buffer_frame_count_ - padding) : 0;
            size_t available_output_frames = resampler_.AvailableOutputFrames();
            size_t frames_to_write = (std::min)(static_cast<size_t>(frames_available), available_output_frames);

            if (frames_to_write > 0) {
                BYTE* pData = nullptr;
                hr = render_client_->GetBuffer(static_cast<UINT32>(frames_to_write), &pData);
                if (SUCCEEDED(hr) && pData) {
                    render_staging_buffer_.resize(frames_to_write * device_channels_);
                    size_t actual_generated = resampler_.Resample(render_staging_buffer_.data(), frames_to_write);

                    if (is_float_format_ && bits_per_sample_ == 32) {
                        std::memcpy(pData, render_staging_buffer_.data(), actual_generated * device_channels_ * sizeof(float));
                    } else if (bits_per_sample_ == 16) {
                        auto* dst_pcm16 = reinterpret_cast<int16_t*>(pData);
                        for (size_t i = 0; i < actual_generated * device_channels_; ++i) {
                            float val = std::clamp(render_staging_buffer_[i], -1.0f, 1.0f);
                            dst_pcm16[i] = static_cast<int16_t>(val * 32767.0f);
                        }
                    } else if (bits_per_sample_ == 32) {
                        auto* dst_pcm32 = reinterpret_cast<int32_t*>(pData);
                        for (size_t i = 0; i < actual_generated * device_channels_; ++i) {
                            float val = std::clamp(render_staging_buffer_[i], -1.0f, 1.0f);
                            dst_pcm32[i] = static_cast<int32_t>(val * 2147483647.0f);
                        }
                    }

                    hr = render_client_->ReleaseBuffer(static_cast<UINT32>(actual_generated), 0);
                    if (FAILED(hr)) {
                        state_ = WasapiState::DeviceLost;
                    }
                } else if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED) {
                    state_ = WasapiState::DeviceLost;
                }
            } else if (frames_available == 0 && available_output_frames > 0) {
                underruns_++;
            }
        }
    }
#endif

    frames_rendered_ += sample_count;
    return 0;
}

void WasapiRenderer::GetMetrics(uint64_t& out_frames_rendered, uint32_t& out_underruns) const noexcept {
    out_frames_rendered = frames_rendered_;
    out_underruns = underruns_;
}

void WasapiRenderer::Shutdown() {
    state_ = WasapiState::Stopped;

#if defined(_WIN32)
    if (audio_client_) {
        audio_client_->Stop();
    }
    render_client_.Reset();
    audio_client_.Reset();
    device_.Reset();
    enumerator_.Reset();
    resampler_.Reset();
    channel_staging_buffer_.clear();
    render_staging_buffer_.clear();
#endif
}

} // namespace moonshine::audio
