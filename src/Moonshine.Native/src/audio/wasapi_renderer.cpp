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
      staging_buffer_(static_cast<size_t>(sample_rate_) * static_cast<size_t>(channels_)) {
}

WasapiRenderer::~WasapiRenderer() {
    Shutdown();
}

int WasapiRenderer::Initialize() {
    if (sample_rate_ == 0 || channels_ == 0) return -1;
    Shutdown();

#if defined(_WIN32)
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(enumerator_.GetAddressOf())
    );

    if (SUCCEEDED(hr) && enumerator_) {
        hr = enumerator_->GetDefaultAudioEndpoint(eRender, eMultimedia, &device_);
        if (SUCCEEDED(hr) && device_) {
            hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
            if (SUCCEEDED(hr) && audio_client_) {
                WAVEFORMATEX* pMixFormat = nullptr;
                hr = audio_client_->GetMixFormat(&pMixFormat);

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
                    wfx.dwChannelMask = 0;
                }

                device_channels_ = channels_;
                device_sample_rate_ = sample_rate_;
                bits_per_sample_ = 32;
                is_float_format_ = true;

                REFERENCE_TIME buffer_duration = exclusive_ ? 30000 : 100000; // 3ms exclusive vs 10ms shared
                DWORD flags = AUDCLNT_STREAMFLAGS_NOPERSIST;

                AUDCLNT_SHAREMODE share_mode = exclusive_ ? AUDCLNT_SHAREMODE_EXCLUSIVE : AUDCLNT_SHAREMODE_SHARED;
                hr = audio_client_->Initialize(
                    share_mode,
                    flags,
                    buffer_duration,
                    buffer_duration,
                    reinterpret_cast<WAVEFORMATEX*>(&wfx),
                    nullptr
                );

                if (FAILED(hr)) {
                    // Re-activate fresh client for shared mode fallback
                    audio_client_.Reset();
                    hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
                    if (SUCCEEDED(hr) && audio_client_) {
                        share_mode = AUDCLNT_SHAREMODE_SHARED;
                        exclusive_ = false;

                        WAVEFORMATEX* target_fmt = (pMixFormat != nullptr) ? pMixFormat : reinterpret_cast<WAVEFORMATEX*>(&wfx);
                        hr = audio_client_->Initialize(
                            share_mode,
                            AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                            100000,
                            0,
                            target_fmt,
                            nullptr
                        );

                        if (SUCCEEDED(hr) && target_fmt) {
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

                if (pMixFormat) {
                    CoTaskMemFree(pMixFormat);
                    pMixFormat = nullptr;
                }

                if (audio_client_ && SUCCEEDED(hr)) {
                    audio_client_->GetBufferSize(&buffer_frame_count_);
                    hr = audio_client_->GetService(__uuidof(IAudioRenderClient), reinterpret_cast<void**>(render_client_.GetAddressOf()));
                    if (SUCCEEDED(hr) && render_client_) {
                        audio_client_->Start();
                    }
                }

                // Register MMCSS task for Pro Audio real-time scheduling priority
                DWORD task_index = 0;
                AvSetMmThreadCharacteristicsW(L"Pro Audio", &task_index);
            }
        }
    }
#endif

    initialized_ = true;
    device_invalidated_ = false;
    frames_rendered_ = 0;
    underruns_ = 0;
    return 0;
}

int WasapiRenderer::Recover() {
    if (sample_rate_ == 0 || channels_ == 0) return -1;

#if defined(_WIN32)
    if (audio_client_) {
        audio_client_->Stop();
    }
    render_client_.Reset();
    audio_client_.Reset();
    device_.Reset();

    if (!enumerator_) {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr_enum = CoCreateInstance(
            __uuidof(MMDeviceEnumerator),
            nullptr,
            CLSCTX_ALL,
            __uuidof(IMMDeviceEnumerator),
            reinterpret_cast<void**>(enumerator_.GetAddressOf())
        );
        if (FAILED(hr_enum) || !enumerator_) {
            device_invalidated_ = true;
            return -1;
        }
    }

    HRESULT hr = enumerator_->GetDefaultAudioEndpoint(eRender, eMultimedia, &device_);
    if (FAILED(hr) || !device_) {
        hr = enumerator_->GetDefaultAudioEndpoint(eRender, eConsole, &device_);
    }
    if (FAILED(hr) || !device_) {
        device_invalidated_ = true;
        return -1;
    }

    hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
    if (FAILED(hr) || !audio_client_) {
        device_invalidated_ = true;
        return -1;
    }

    WAVEFORMATEX* pMixFormat = nullptr;
    hr = audio_client_->GetMixFormat(&pMixFormat);

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
        wfx.dwChannelMask = 0;
    }

    device_channels_ = channels_;
    device_sample_rate_ = sample_rate_;
    bits_per_sample_ = 32;
    is_float_format_ = true;

    REFERENCE_TIME buffer_duration = exclusive_ ? 30000 : 100000;
    DWORD flags = AUDCLNT_STREAMFLAGS_NOPERSIST;

    AUDCLNT_SHAREMODE share_mode = exclusive_ ? AUDCLNT_SHAREMODE_EXCLUSIVE : AUDCLNT_SHAREMODE_SHARED;
    hr = audio_client_->Initialize(
        share_mode,
        flags,
        buffer_duration,
        buffer_duration,
        reinterpret_cast<WAVEFORMATEX*>(&wfx),
        nullptr
    );

    if (FAILED(hr)) {
        audio_client_.Reset();
        hr = device_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client_.GetAddressOf()));
        if (SUCCEEDED(hr) && audio_client_) {
            share_mode = AUDCLNT_SHAREMODE_SHARED;
            exclusive_ = false;

            WAVEFORMATEX* target_fmt = (pMixFormat != nullptr) ? pMixFormat : reinterpret_cast<WAVEFORMATEX*>(&wfx);
            hr = audio_client_->Initialize(
                share_mode,
                AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                100000,
                0,
                target_fmt,
                nullptr
            );

            if (SUCCEEDED(hr) && target_fmt) {
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

    if (pMixFormat) {
        CoTaskMemFree(pMixFormat);
        pMixFormat = nullptr;
    }

    if (audio_client_ && SUCCEEDED(hr)) {
        audio_client_->GetBufferSize(&buffer_frame_count_);
        hr = audio_client_->GetService(__uuidof(IAudioRenderClient), reinterpret_cast<void**>(render_client_.GetAddressOf()));
        if (SUCCEEDED(hr) && render_client_) {
            audio_client_->Start();
        }
    }

    DWORD task_index = 0;
    AvSetMmThreadCharacteristicsW(L"Pro Audio", &task_index);

    device_invalidated_ = false;
    initialized_ = true;
    resample_phase_ = 0.0;
    last_src_frame_.assign(channels_, 0.0f);
    return 0;
#else
    device_invalidated_ = false;
    initialized_ = true;
    return 0;
#endif
}

int WasapiRenderer::SubmitPcm(const float* pcm_data, uint32_t sample_count) {
    if (!initialized_ || !pcm_data || sample_count == 0) {
        return -1;
    }

#if defined(_WIN32)
    if (render_client_ && audio_client_ && !device_invalidated_) {
        UINT32 padding = 0;
        HRESULT hr = audio_client_->GetCurrentPadding(&padding);
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
            hr == AUDCLNT_E_UNSUPPORTED_FORMAT || hr == AUDCLNT_E_DEVICE_IN_USE || 
            hr == AUDCLNT_E_BUFFER_ERROR) {
            device_invalidated_ = true;
        } else if (SUCCEEDED(hr)) {
            const uint32_t src_channels = channels_;
            const uint32_t dst_channels = device_channels_ > 0 ? device_channels_ : src_channels;
            const uint32_t src_rate = sample_rate_ > 0 ? sample_rate_ : 48000;
            const uint32_t dst_rate = device_sample_rate_ > 0 ? device_sample_rate_ : src_rate;
            const bool needs_resample = (src_rate != dst_rate);
            const double resample_ratio = (dst_rate > 0) ? (static_cast<double>(src_rate) / static_cast<double>(dst_rate)) : 1.0;

            if (last_src_frame_.size() != src_channels) {
                last_src_frame_.assign(src_channels, 0.0f);
            }

            UINT32 frames_available = (buffer_frame_count_ > padding) ? (buffer_frame_count_ - padding) : 0;

            if (!needs_resample) {
                UINT32 frames_to_write = (std::min)(sample_count, frames_available);
                if (frames_to_write > 0) {
                    BYTE* pData = nullptr;
                    hr = render_client_->GetBuffer(frames_to_write, &pData);
                    if (SUCCEEDED(hr) && pData) {
                        if (is_float_format_ && bits_per_sample_ == 32) {
                            auto* dst_float = reinterpret_cast<float*>(pData);
                            for (uint32_t f = 0; f < frames_to_write; ++f) {
                                for (uint32_t ch = 0; ch < dst_channels; ++ch) {
                                    float val = (ch < src_channels) ? pcm_data[(f * src_channels) + ch] : 0.0f;
                                    if (std::isnan(val) || std::isinf(val)) val = 0.0f;
                                    dst_float[(f * dst_channels) + ch] = std::clamp(val, -1.0f, 1.0f);
                                }
                            }
                        } else if (bits_per_sample_ == 16) {
                            auto* dst_pcm16 = reinterpret_cast<int16_t*>(pData);
                            for (uint32_t f = 0; f < frames_to_write; ++f) {
                                for (uint32_t ch = 0; ch < dst_channels; ++ch) {
                                    float val = (ch < src_channels) ? pcm_data[(f * src_channels) + ch] : 0.0f;
                                    if (std::isnan(val) || std::isinf(val)) val = 0.0f;
                                    val = std::clamp(val, -1.0f, 1.0f);
                                    dst_pcm16[(f * dst_channels) + ch] = static_cast<int16_t>(val * 32767.0f);
                                }
                            }
                        } else if (bits_per_sample_ == 32) {
                            auto* dst_pcm32 = reinterpret_cast<int32_t*>(pData);
                            for (uint32_t f = 0; f < frames_to_write; ++f) {
                                for (uint32_t ch = 0; ch < dst_channels; ++ch) {
                                    float val = (ch < src_channels) ? pcm_data[(f * src_channels) + ch] : 0.0f;
                                    if (std::isnan(val) || std::isinf(val)) val = 0.0f;
                                    val = std::clamp(val, -1.0f, 1.0f);
                                    dst_pcm32[(f * dst_channels) + ch] = static_cast<int32_t>(val * 2147483647.0f);
                                }
                            }
                        }
                        hr = render_client_->ReleaseBuffer(frames_to_write, 0);
                        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
                            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
                            hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
                            device_invalidated_ = true;
                        }
                    } else if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
                               hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
                               hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
                        device_invalidated_ = true;
                    }
                } else {
                    underruns_++;
                }
            } else {
                // Resample between sample_rate_ and device_sample_rate_
                uint32_t target_output_frames = static_cast<uint32_t>(std::ceil(static_cast<double>(sample_count) / resample_ratio));
                UINT32 frames_to_write = (std::min)(target_output_frames, frames_available);

                if (frames_to_write > 0) {
                    BYTE* pData = nullptr;
                    hr = render_client_->GetBuffer(frames_to_write, &pData);
                    if (SUCCEEDED(hr) && pData) {
                        auto get_src_sample = [&](uint32_t frame_idx, uint32_t ch_idx) -> float {
                            if (frame_idx >= sample_count || ch_idx >= src_channels) return 0.0f;
                            float val = pcm_data[(frame_idx * src_channels) + ch_idx];
                            if (std::isnan(val) || std::isinf(val)) return 0.0f;
                            return std::clamp(val, -1.0f, 1.0f);
                        };

                        for (uint32_t f = 0; f < frames_to_write; ++f) {
                            double src_pos = resample_phase_;
                            auto idx = static_cast<uint32_t>(src_pos);
                            double frac = src_pos - static_cast<double>(idx);

                            for (uint32_t ch = 0; ch < dst_channels; ++ch) {
                                float s0 = (idx == 0 && resample_phase_ < 1.0) ? (ch < src_channels ? last_src_frame_[ch] : 0.0f) : get_src_sample(idx > 0 ? idx - 1 : 0, ch);
                                float s1 = get_src_sample(idx, ch);
                                float interpolated = static_cast<float>((1.0 - frac) * s0 + frac * s1);
                                if (std::isnan(interpolated) || std::isinf(interpolated)) interpolated = 0.0f;
                                float val = std::clamp(interpolated, -1.0f, 1.0f);

                                if (is_float_format_ && bits_per_sample_ == 32) {
                                    reinterpret_cast<float*>(pData)[(f * dst_channels) + ch] = val;
                                } else if (bits_per_sample_ == 16) {
                                    reinterpret_cast<int16_t*>(pData)[(f * dst_channels) + ch] = static_cast<int16_t>(val * 32767.0f);
                                } else if (bits_per_sample_ == 32) {
                                    reinterpret_cast<int32_t*>(pData)[(f * dst_channels) + ch] = static_cast<int32_t>(val * 2147483647.0f);
                                }
                            }
                            resample_phase_ += resample_ratio;
                        }

                        if (sample_count > 0) {
                            for (uint32_t ch = 0; ch < src_channels; ++ch) {
                                last_src_frame_[ch] = get_src_sample(sample_count - 1, ch);
                            }
                        }
                        resample_phase_ -= static_cast<double>(sample_count);
                        if (resample_phase_ < 0.0 || std::isnan(resample_phase_) || std::isinf(resample_phase_)) {
                            resample_phase_ = 0.0;
                        }

                        hr = render_client_->ReleaseBuffer(frames_to_write, 0);
                        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
                            hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
                            hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
                            device_invalidated_ = true;
                        }
                    } else if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == AUDCLNT_E_RESOURCES_INVALIDATED || 
                               hr == AUDCLNT_E_SERVICE_NOT_RUNNING || hr == AUDCLNT_E_NOT_INITIALIZED || 
                               hr == AUDCLNT_E_UNSUPPORTED_FORMAT) {
                        device_invalidated_ = true;
                    }
                } else {
                    underruns_++;
                }
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
    initialized_ = false;
    device_invalidated_ = false;

#if defined(_WIN32)
    if (audio_client_) {
        audio_client_->Stop();
    }
    render_client_.Reset();
    audio_client_.Reset();
    device_.Reset();
    enumerator_.Reset();
    resample_phase_ = 0.0;
    last_src_frame_.clear();
#endif
}

} // namespace moonshine::audio

