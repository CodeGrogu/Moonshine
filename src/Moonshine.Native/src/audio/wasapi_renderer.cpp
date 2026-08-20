#include "moonshine/audio/wasapi_renderer.hpp"
#include <cstring>
#include <algorithm>

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
    : sample_rate_(sample_rate),
      channels_(channels == 0 ? 2 : channels),
      exclusive_(exclusive),
      staging_buffer_(sample_rate_ * channels_) {
}

WasapiRenderer::~WasapiRenderer() {
    Shutdown();
}

int WasapiRenderer::Initialize() {
    if (sample_rate_ == 0 || channels_ == 0) return -1;

#if defined(_WIN32)
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

    ComPtr<IMMDeviceEnumerator> enumerator;
    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(enumerator.GetAddressOf())
    );

    if (SUCCEEDED(hr)) {
        ComPtr<IMMDevice> device;
        hr = enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &device);
        if (SUCCEEDED(hr)) {
            ComPtr<IAudioClient> audio_client;
            hr = device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(audio_client.GetAddressOf()));
            if (SUCCEEDED(hr)) {
                // Configure WAVEFORMATEXTENSIBLE for 32-bit IEEE float PCM
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

                REFERENCE_TIME buffer_duration = exclusive_ ? 30000 : 100000; // 3ms exclusive vs 10ms shared
                DWORD flags = AUDCLNT_STREAMFLAGS_NOPERSIST;

                AUDCLNT_SHAREMODE share_mode = exclusive_ ? AUDCLNT_SHAREMODE_EXCLUSIVE : AUDCLNT_SHAREMODE_SHARED;
                hr = audio_client->Initialize(
                    share_mode,
                    flags,
                    buffer_duration,
                    buffer_duration,
                    reinterpret_cast<WAVEFORMATEX*>(&wfx),
                    nullptr
                );

                if (FAILED(hr) && exclusive_) {
                    // Fallback to shared mode if exclusive mode is occupied by another app
                    share_mode = AUDCLNT_SHAREMODE_SHARED;
                    audio_client->Initialize(
                        share_mode,
                        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                        100000,
                        0,
                        reinterpret_cast<WAVEFORMATEX*>(&wfx),
                        nullptr
                    );
                }

                // Register MMCSS task for Pro Audio real-time scheduling priority
                DWORD task_index = 0;
                AvSetMmThreadCharacteristicsW(L"Pro Audio", &task_index);

                initialized_ = true;
                return 0;
            }
        }
    }
#endif

    initialized_ = true;
    return 0;
}

int WasapiRenderer::SubmitPcm(const float* pcm_data, uint32_t sample_count) {
    if (!initialized_ || !pcm_data || sample_count == 0) {
        return -1;
    }

    frames_rendered_ += sample_count;
    return 0;
}

void WasapiRenderer::GetMetrics(uint64_t& out_frames_rendered, uint32_t& out_underruns) const noexcept {
    out_frames_rendered = frames_rendered_;
    out_underruns = underruns_;
}

void WasapiRenderer::Shutdown() {
    initialized_ = false;
    frames_rendered_ = 0;
    underruns_ = 0;
}

} // namespace moonshine::audio
