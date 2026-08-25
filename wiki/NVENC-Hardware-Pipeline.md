> [!WARNING]
> **Status: Incomplete / Fail-Closed**
> Moonshine is in early development (v0.5.6-alpha) and is fail-closed by design. End-to-end streaming is not yet operational. Hardware encoders are not operational just because vendor APIs exist. Operational status requires successful submission of real captured GPU content.

# NVIDIA NVENC Hardware Video Encoder Pipeline

The **NVIDIA NVENC Hardware Video Encoder Pipeline** provides direct hardware-accelerated video encoding for NVIDIA GeForce GTX/RTX, RTX Professional, and Tesla/Data Center GPUs. It takes Direct3D 11 / Direct3D 12 texture surfaces directly with a design target of sub-2ms encode latency for H.264, HEVC Main10 (HDR10), and AV1 Profile 0.

---

## 1. NVENC Architecture & Surface Interop

```
┌────────────────────────────────────────────────────────────┐
│      Direct3D 11/12 Captured Desktop Texture / Frame       │
│           (DXGI / WGC Desktop Duplication Surface)         │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│                NVENC Direct Surface Mapping                │
│    - NV_ENC_REGISTER_RESOURCE (D3D11 / D3D12 / CUDA)       │
│    - NV_ENC_MAP_INPUT_RESOURCE (Lock-free zero copy)       │
└─────────────────────────────┬──────────────────────────────┘
                              │
       ┌──────────────────────┼──────────────────────┐
       ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│    H.264     │       │ HEVC Main10  │       │ AV1 Profile 0│
│  - High/Main │       │ - P010 Surface│       │ - Ada/Black- │
│  - Slice NAL │       │ - HDR10 ST2084│       │   well Gen   │
│  - Ultra-Low │       │ - VPS/SPS/PPS │       │ - OBU Format │
└──────┬───────┘       └──────┬───────┘       └──────┬───────┘
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│              Ultra-Low Latency Tuning & Presets            │
│         - P1 (Ultra-Fast) & P2 (Fast) Tuning               │
│         - Strict Constant Bitrate (CBR) Rate Control       │
│         - Zero B-Frames (maxNumBFrames = 0)                │
│         - Infinite GOP Length with Dynamic IDR Injection   │
│         - Progressive Intra-Refresh Slice Columns          │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│         Annex B / OBU Bitstream -> MNBP/FEC Streamer       │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Presets, Tuning Modes, and Latency Profiles

| Preset | Target Latency | Visual Quality | Best Use Case |
|---|---|---|---|
| **P1_UltraFast** | `< 1.2 ms` | Good | 4K 120/240 FPS Ultra-Low Latency Competitive Gaming (Design Target) |
| **P2_Fast** | `< 1.8 ms` | Very Good | 1440p 120 FPS / 4K 60 FPS Balance |
| **P3_Medium** | `~ 2.5 ms` | High | 1080p 60/120 FPS High Quality Streaming |
| **P4_Default** | `~ 3.2 ms` | High | General Video Streaming |
| **P5-P7** | `> 4.0 ms` | Maximum | Archival / Production Recording |

---

## 3. Dedicated C-ABI Export Methods

```c
// Queries whether the given codec (H264, HEVC, HEVC Main10, AV1) is supported
MOONSHINE_API int moonshine_nvenc_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

// Updates preset (P1-P7) and tuning mode (UltraLowLatency, LowLatency, HighQuality)
MOONSHINE_API int moonshine_nvenc_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t tuning
);

// Enables and configures progressive intra-refresh slice columns
MOONSHINE_API int moonshine_nvenc_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t period,
    uint32_t count
);
```

---

## 4. Managed Host Usage Example

```csharp
using var nvencPipeline = new NvencHardwareEncoderPipeline(
    width: 3840,
    height: 2160,
    fps: 120,
    bitrateKbps: 50000,
    codec: VideoCodec.HevcMain10,
    preset: NvencPreset.P1_UltraFast,
    tuning: NvencTuning.UltraLowLatency
);

// Enable progressive intra-refresh to smooth intra-frame bit distribution
nvencPipeline.ConfigureIntraRefresh(enable: true, period: 60, count: 4);

// Zero-allocation frame encoding in streaming hot path
Span<byte> outBuffer = stackalloc byte[1024 * 512];
if (nvencPipeline.TryEncodeFrame(d3dTexturePtr, forceIdr: false, out var desc, outBuffer, out int written))
{
    _packetiser.SendFrame(desc, outBuffer.Slice(0, written));
}
```
