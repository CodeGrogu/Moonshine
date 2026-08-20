# Low-Latency Audio Pipeline (WASAPI Exclusive)

## 1. Problem Statement: Audio Latency in Shared Sound Mixers

Standard Windows audio engines use the shared Windows Audio Session API (WASAPI Shared Mode), which routes all application audio through the Windows system mixer (AudioDG engine). This shared pipeline introduces:
1. Resampling and format conversion delays (10ms to 25ms).
2. Buffer underrun protections requiring large ring buffer sizes (typically 480 to 960 samples per buffer).
3. Audio jitter that disrupts lip-sync synchronisation with high-frame-rate video (120Hz/240Hz).

---

## 2. Custom Solution: WASAPI Exclusive Mode Sub-5ms Pipeline

Moonshine implements a custom WASAPI Exclusive Mode audio renderer (`WasapiRenderer` in C++23) that acquires direct hardware control of the audio endpoint:

```
Opus RTP Audio Packets (48kHz, 2.0 Stereo / 5.1 / 7.1 Surround)
         │
         ▼
Opus Low-Latency Decoder (Float32 / Int16 PCM)
         │
         ▼  (Zero-Copy Direct Buffer Write)
WASAPI Exclusive Mode Ring Buffer (IAudioRenderClient::GetBuffer)
         │
         ▼  (Direct Hardware Endpoint - Sub-5ms Periodicity)
DAC / Audio Output Device
```

### Key Technical Details:
- Exclusive Endpoint Lock (`AUDCLNT_SHAREMODE_EXCLUSIVE`): Bypasses the Windows system mixer entirely.
- Event-Driven Buffer Refill (`AUDCLNT_STREAMFLAGS_EVENTCALLBACK`): The audio thread sleeps on a native Win32 event object signaled directly by the audio hardware clock interrupt, eliminating CPU polling loops.
- Minimal Buffer Periodicity: Configured for 2.5ms to 5.0ms buffer durations (typically 120 to 240 samples at 48kHz), reducing total audio path latency to under 3ms.
- 5.1 and 7.1 Surround Sound Mapping: Direct multi-channel matrix routing matching Sunshine surround sound stream formats.
