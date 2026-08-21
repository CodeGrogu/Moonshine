# WASAPI Loopback Audio Capture & Low-Latency Streaming

The **Moonshine Host WASAPI Loopback Audio Engine** intercepts the master Windows audio mix directly from the kernel mixing engine (`AUDCLNT_STREAMFLAGS_LOOPBACK`) with sub-3ms latency. It supports Stereo 2.0, Surround 5.1, and Surround 7.1 channel topologies at 48kHz, packetising audio into RFC 3550 RTP frames with zero GC allocations.

---

## 1. Loopback Capture Architecture

```
┌────────────────────────────────────────────────────────────┐
│              Windows Audio Endpoint (IMMDevice)            │
│               (Default Console / Multimedia)               │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│               WASAPI Loopback Capture Client               │
│    - AUDCLNT_STREAMFLAGS_LOOPBACK                          │
│    - AUDCLNT_STREAMFLAGS_EVENTCALLBACK                     │
│    - AvSetMmThreadCharacteristicsW ("Pro Audio", MMCSS)    │
│    - Periodic 5ms / 10ms Frame Interval (240 / 480 smp)    │
└─────────────────────────────┬──────────────────────────────┘
                              │
       ┌──────────────────────┼──────────────────────┐
       ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│  Stereo 2.0  │       │Surround 5.1  │       │Surround 7.1  │
│  - 2 Channels│       │ - 6 Channels │       │ - 8 Channels │
│  - FL, FR    │       │ - FL, FR, FC,│       │ - FL, FR, FC,│
│              │       │   LFE, BL, BR│       │   LFE, BL, BR│
│              │       │              │       │   SL, SR     │
└──────┬───────┘       └──────┬───────┘       └──────┬───────┘
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│                PCM Conversion & Clamping                   │
│    - Float32 IEEE 754 -> Int16 Signed Little-Endian        │
│    - SIMD Clamping [-32768, 32767]                         │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│              Zero-Allocation RTP Audio Packetiser          │
│    - RFC 3550 Standard 12-byte RTP Header                  │
│    - Monotonic 48kHz Timestamps                            │
│    - UDP Datagram Emission -> Client Audio Pipeline        │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Multi-Channel Speaker Channel Masks

Moonshine negotiates standard WaveFormatExtensible speaker configurations:

| Channel Topology | Channel Count | Speaker Channel Mask | Channel Order |
| :--- | :--- | :--- | :--- |
| **Stereo** | 2 | `KSAUDIO_SPEAKER_STEREO` | Front Left, Front Right |
| **Surround 5.1** | 6 | `KSAUDIO_SPEAKER_5POINT1_SURROUND` | Front Left, Front Right, Front Centre, Low Frequency Effects (LFE / Subwoofer), Back Left, Back Right |
| **Surround 7.1** | 8 | `KSAUDIO_SPEAKER_7POINT1_SURROUND` | Front Left, Front Right, Front Centre, LFE, Back Left, Back Right, Side Left, Side Right |

---

## 3. Dedicated C-ABI Export Methods

```c
MOONSHINE_API MoonshineAudioCaptureHandle MOONSHINE_CONV moonshine_audio_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_destroy(
    MoonshineAudioCaptureHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_float(
    MoonshineAudioCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_pcm16(
    MoonshineAudioCaptureHandle handle,
    int16_t* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_get_metrics(
    MoonshineAudioCaptureHandle handle,
    uint64_t* out_frames_captured,
    uint64_t* out_samples_captured,
    uint32_t* out_underruns,
    uint32_t* out_overruns
);
```

---

## 4. Managed Host Usage Example

```csharp
using Moonshine.Host.Audio;

// Initialize 5ms WASAPI Loopback Capture at 48kHz Stereo
using var capture = new WasapiLoopbackAudioPipeline(
    sampleRate: 48000,
    topology: AudioChannelTopology.Stereo,
    bufferDurationMs: 5
);

var packetiser = new RtpAudioPacketiser(payloadType: 97, ssrc: 0x11223344);

Span<short> pcmBuffer = stackalloc short[480]; // 240 samples * 2 channels
Span<byte> rtpBuffer = stackalloc byte[1024];

if (capture.TryReadSamplesPcm16(pcmBuffer, out int samplesRead, out ulong timestampQpc))
{
    ReadOnlySpan<byte> pcmBytes = MemoryMarshal.AsBytes(pcmBuffer.Slice(0, samplesRead));
    if (packetiser.TryPacketise(pcmBytes, timestamp: (uint)(timestampQpc / 1000), marker: false, rtpBuffer, out int written))
    {
        // Dispatch rtpBuffer.Slice(0, written) over UDP
    }
}
```
