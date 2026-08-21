# Low-Latency Client-to-Host Microphone Passthrough & Virtual Audio Sink

The **Moonshine Microphone Passthrough Engine** enables real-time, low-latency streaming of client voice audio to the Moonshine host with a sub-15ms end-to-end latency budget. Decoded microphone streams are routed directly to Windows virtual audio recording devices (such as Steam Streaming Microphone, VB-CABLE, or Windows CoreAudio virtual audio sinks) for in-game voice chat and Discord communication.

---

## 1. Bidirectional Voice Pipeline Architecture

```
┌────────────────────────────────────────────────────────────┐
│         Client Microphone Ingestion Pipeline               │
│   - 48kHz Mono/Stereo PCM Audio Capture                    │
│   - Software Noise Gate (RMS < Threshold Attenuation)      │
│   - Real-Time Gain Normalisation & Client Mute Control     │
│   - Low-Delay Opus VoIP Compression (32-64 kbps, 10ms)     │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│            UDP/RTP Audio Backchannel Protocol              │
│   - RFC 3550 Standard RTP Framing (Payload Type 98)        │
│   - Monotonic 48kHz Timestamps & Sequence Number Tracking  │
│   - Asynchronous Socket Pipeline with Zero Allocation      │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│             Native C++23 MicAudioSink Engine               │
│   - Sub-10ms Adaptive Jitter Buffer Depth                  │
│   - Packet Loss Concealment (PLC) Synthesis for Dropped Pkt│
│   - Clock Drift Compensation (Dynamic Queue Depth Trimming)│
│   - Noise Gating (RMS Threshold: -50dB) & Gain Multiplier  │
│   - Soft Mute Enforcer (Zero Silence Output)               │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│          Host Virtual Microphone Device Injection          │
│   - Float32 PCM Render Stream                              │
│   - Sub-15ms Total Voice Latency Budget                    │
│   - In-Game Voice Chat, Discord, and CoreAudio Routing     │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Adaptive Jitter Buffer & Clock Drift Compensation

Because client recording clocks and host playback clocks are physically independent crystal oscillators, clock drift is inevitable over long streaming sessions:

1. **Adaptive Buffer Depth**:
   - Maintains a compact queue target of 5-10ms (1 to 2 frames @ 10ms frame size).
2. **Clock Drift Trimming**:
   - If the client recording clock runs faster than host consumption (queue depth $> 4$ frames / $>40\text{ms}$), the oldest voice packet is dropped to prevent progressive latency buildup.
   - If the host playback clock runs faster (buffer starvation), smooth zero-concealment is generated without audio pops or clicks.
3. **Packet Loss Concealment (PLC)**:
   - Sequence number gaps trigger synthetic silent or decayed interpolation frames to maintain continuous playback timing.

---

## 3. Dedicated C-ABI Export API

```c
MOONSHINE_API MoonshineMicSinkHandle MOONSHINE_CONV moonshine_mic_sink_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t target_latency_ms,
    float gain_multiplier,
    float noise_gate_threshold_db,
    uint8_t is_muted
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_destroy(
    MoonshineMicSinkHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_push_opus_packet(
    MoonshineMicSinkHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_len,
    uint32_t timestamp,
    uint16_t sequence_number
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_pull_pcm(
    MoonshineMicSinkHandle handle,
    float* out_pcm,
    uint32_t max_samples,
    uint32_t* out_samples_read
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_gain(
    MoonshineMicSinkHandle handle,
    float gain
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_mute(
    MoonshineMicSinkHandle handle,
    uint8_t is_muted
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_get_metrics(
    MoonshineMicSinkHandle handle,
    uint64_t* out_packets_received,
    uint64_t* out_samples_rendered,
    uint32_t* out_loss_count,
    uint32_t* out_drift_corrections,
    double* out_jitter_ms
);
```

---

## 4. Managed Host Pipeline Usage Example

```csharp
using Moonshine.Host.Audio;
using Moonshine.Protocol.Audio;

// Initialize Host Virtual Microphone Sink Pipeline
using var micSink = new HostVirtualMicSinkPipeline(
    sampleRate: 48000,
    channels: 1,
    targetLatencyMs: 10,
    gainMultiplier: 1.2f,
    noiseGateThresholdDb: -50.0f,
    isMuted: false
);

// On receiving UDP/RTP backchannel datagram:
if (MicAudioPacket.TryParse(datagramSpan, out MicAudioPacket packet))
{
    micSink.TryPushOpusPacket(packet.Payload, packet.Timestamp, packet.SequenceNumber);
}

// In host audio rendering loop:
Span<float> pcmBuffer = stackalloc float[480]; // 10ms @ 48kHz
if (micSink.TryPullPcm(pcmBuffer, out int samplesRead))
{
    // Submit Float32 PCM samples to virtual microphone device
}
```
