> [!NOTE]
> **Development Status (v0.5.6-alpha)**: The WASAPI audio rendering subsystem is at **Prototype** maturity. It has been tested in isolation but is not yet wired to an end-to-end streaming pipeline. The latency figures below are design targets. Moonshine uses its own protocol (MNBP v1), not RTP.

# WASAPI Low-Latency Audio Rendering Engine

Moonshine features a low-latency audio playback pipeline built with the Windows Audio Session API (WASAPI) operating in Exclusive Mode, targeting sub-5ms render latencies without audio glitching or buffer underruns.

---

## 1. Architectural Overview

```
RTP Audio Packets (Opus 48kHz) [Legacy framing, to be replaced by MNBP v1]
                │
                │ Real-time Opus decoding
                ▼
MoonshineAudioPipeline (.NET 9 Managed Layer)
                │
                │ Zero-allocation ReadOnlySpan<float> (IEEE 32-bit Float PCM)
                ▼
C-ABI Interop Bridge (moonshine_audio_submit_pcm)
                │
                ▼
WasapiRenderer (C++23 Native Layer)
                │
                ├─► AUDCLNT_SHAREMODE_EXCLUSIVE (Bypasses Windows Audio Mixer)
                ├─► AvSetMmThreadCharacteristicsW (L"Pro Audio" Real-Time Priority)
                ├─► IAudioRenderClient (Direct hardware DMA buffer transfer)
                │
                ▼
Audio Hardware DAC (Buffer Period: 2.6ms - 4.0ms @ 48kHz)
```

---

## 2. Multi-Channel Surround Sound Configurations

Moonshine supports high-fidelity audio streams across three spatial topologies:

| Configuration | Channels | Channel Layout Mask | Sample Format |
| :--- | :--- | :--- | :--- |
| **Stereo (2.0)** | 2 | `SPEAKER_FRONT_LEFT \| SPEAKER_FRONT_RIGHT` | 32-bit IEEE Float @ 48kHz |
| **Surround 5.1** | 6 | `FL \| FR \| FC \| LFE \| BL \| BR` | 32-bit IEEE Float @ 48kHz |
| **Surround 7.1** | 8 | `FL \| FR \| FC \| LFE \| BL \| BR \| SL \| SR` | 32-bit IEEE Float @ 48kHz |

---

## 3. Latency Optimisation & MMCSS Scheduling

1. **Exclusive Mode Hardware Bypass**:
   - `AUDCLNT_SHAREMODE_EXCLUSIVE` opens direct access to the audio hardware ring buffer, eliminating Windows Audio Engine (audiodg.exe) sample rate conversion, limiter DSP, and mixer buffering overhead.
   - Fallback to `AUDCLNT_SHAREMODE_SHARED` is automatically handled if another application requests exclusive lock.
2. **Pro Audio MMCSS Real-Time Scheduling**:
   - The audio rendering thread registers with the Multimedia Class Scheduler Service via `AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex)`, ensuring preemption-free thread scheduling with real-time priority.
3. **Telemetry & Underrun Diagnostics**:
   - Real-time tracking of `FramesSubmitted`, `FramesRendered`, and `BufferUnderruns` via `AudioGetMetrics`.
