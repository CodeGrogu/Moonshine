# Low-Latency Opus Audio Compression & Multi-Channel Stream Encoder

The **Moonshine Opus Audio Compression Engine** delivers sub-1ms audio compression for 48kHz audio streams across Stereo 2.0, Surround 5.1 (Vorbis Layout Family 1), and Surround 7.1 channel configurations. Designed for ultra-low latency interactive streaming, the encoder operates with zero GC allocations in streaming hot paths and supports dynamic runtime bitrate and complexity adjustment.

---

## 1. Multi-Stream Opus Architecture

```
┌────────────────────────────────────────────────────────────┐
│          WASAPI Master Loopback 48kHz PCM Stream           │
│        (Float32 or Int16 Little-Endian Audio Buffer)       │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│                  OpusAudioEncoder Engine                   │
│   - Low-Delay Mode (OPUS_APPLICATION_RESTRICTED_LOWDELAY)  │
│   - 5.0ms / 10.0ms Frame Durations (240 / 480 smp/channel) │
│   - Dynamic Bitrate Scaling: 64 kbps to 512 kbps           │
│   - Zero-Allocation Scratch Buffers                        │
└─────────────────────────────┬──────────────────────────────┘
                              │
       ┌──────────────────────┼──────────────────────┐
       ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│  Stereo 2.0  │       │Surround 5.1  │       │Surround 7.1  │
│  - 1 Stream  │       │ - 4 Streams  │       │ - 6 Streams  │
│  - 1 Coupled │       │ - 2 Coupled  │       │ - 2 Coupled  │
│  - [FL, FR]  │       │ - [FL, FR,   │       │ - [FL, FR,   │
│              │       │    FC, LFE,  │       │    FC, LFE,  │
│              │       │    BL, BR]   │       │    BL, BR,   │
│              │       │              │       │    SL, SR]   │
└──────┬───────┘       └──────┬───────┘       └──────┬───────┘
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│            Multi-Stream Opus Packet Framing                │
│   - RFC 6716 & RFC 7845 Self-Delimited Stream Delimiters   │
│   - Low-Delay CELT Frame TOC Header                        │
│   - Compressed Audio Payload Datagram                      │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│             RFC 3550 RTP Audio Packetiser Output           │
│   - Monotonic 48kHz Monotonic Timestamps                   │
│   - Sub-1ms End-to-End Compression Latency                 │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Multi-Stream Channel Mapping Matrix

| Channel Topology | Channel Count | Total Streams | Coupled Streams | Channel Matrix Order |
| :--- | :--- | :--- | :--- | :--- |
| **Stereo** | 2 | 1 | 1 | `[0, 1]` -> Front Left, Front Right (Coupled) |
| **Surround 5.1** | 6 | 4 | 2 | `[0, 1, 2, 3, 4, 5]` -> FL/FR (Coupled), FC (Uncoupled), LFE (Uncoupled), BL/BR (Coupled) |
| **Surround 7.1** | 8 | 6 | 2 | `[0, 1, 2, 3, 4, 5, 6, 7]` -> FL/FR (Coupled), FC (Uncoupled), LFE (Uncoupled), BL/BR (Coupled), SL/SR (Coupled) |

---

## 3. Dedicated C-ABI Export API

```c
MOONSHINE_API MoonshineOpusEncoderHandle MOONSHINE_CONV moonshine_opus_encoder_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t bitrate,
    uint32_t frame_duration_ms,
    uint32_t complexity,
    int32_t use_vbr
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_destroy(
    MoonshineOpusEncoderHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_float(
    MoonshineOpusEncoderHandle handle,
    const float* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_pcm16(
    MoonshineOpusEncoderHandle handle,
    const int16_t* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_bitrate(
    MoonshineOpusEncoderHandle handle,
    uint32_t bitrate
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_complexity(
    MoonshineOpusEncoderHandle handle,
    uint32_t complexity
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_get_metrics(
    MoonshineOpusEncoderHandle handle,
    uint64_t* out_frames_encoded,
    uint64_t* out_bytes_encoded,
    double* out_avg_encode_time_us,
    uint32_t* out_bitrate,
    uint32_t* out_streams_count
);
```

---

## 4. Managed Host Pipeline Usage Example

```csharp
using Moonshine.Host.Audio;

// Initialize 5ms Opus Audio Encoder at 48kHz Surround 5.1 (256 kbps)
using var encoder = new OpusAudioEncoderPipeline(
    sampleRate: 48000,
    topology: AudioChannelTopology.Surround51,
    bitrate: 256000,
    frameDurationMs: 5,
    complexity: 8,
    useVbr: true
);

var packetiser = new RtpAudioPacketiser(payloadType: 97, ssrc: 0x55667788);

Span<float> pcmBuffer = stackalloc float[240 * 6]; // 240 samples * 6 channels
Span<byte> opusPayload = stackalloc byte[1024];
Span<byte> rtpDatagram = stackalloc byte[1500];

if (encoder.TryEncode(pcmBuffer, frameSamples: 240, opusPayload, out int bytesEncoded))
{
    ReadOnlySpan<byte> payloadSlice = opusPayload.Slice(0, bytesEncoded);
    if (packetiser.TryPacketise(payloadSlice, timestamp: 48000, marker: false, rtpDatagram, out int rtpWritten))
    {
        // Transmit UDP datagram
    }
}
```
