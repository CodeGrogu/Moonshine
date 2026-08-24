# AMD AMF & Intel QuickSync Hardware Video Encoder Pipelines

The **Moonshine Host AMF and QuickSync Video Encoding Pipelines** provide direct hardware-accelerated video encoding for AMD Radeon (VCN 1.0 to 5.0+) and Intel Arc / Iris Xe / Core Ultra (QuickSync / oneVPL) GPUs. They process Direct3D 11 texture surfaces directly with ultra-low-latency CBR tuning for H.264, HEVC Main10 (HDR10), and AV1 Profile 0. <!-- PROVENANCE: STANDARDS.md Rule 9 verified via test_amf_pipeline, test_amf_conformance, test_qsv_pipeline, and test_qsv_conformance -->

---

## 1. AMD AMF Pipeline Architecture

```
┌────────────────────────────────────────────────────────────┐
│         Direct3D 11 Captured Desktop Texture / Frame       │
│           (DXGI / WGC Desktop Duplication Surface)         │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│                  AMD AMF Direct Context                    │
│    - AMFContext::InitDX11 (Zero-copy GPU texture surface)  │
│    - AMFSurface creation from ID3D11Texture2D              │
└─────────────────────────────┬──────────────────────────────┘
                              │
       ┌──────────────────────┼──────────────────────┐
       ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│  VCN H.264   │       │VCN HEVC Main10│      │  VCN 4.0 AV1 │
│ - Ultra-Low  │       │ - P010 Surface│      │ - RDNA 3+ Gen│
│ - Speed Mode │       │ - HDR10 ST2084│      │ - OBU Packing│
└──────┬───────┘       └──────┬───────┘       └──────┬───────┘
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│           AMD VCN Ultra-Low Latency Configuration          │
│    - AMF_VIDEO_ENCODER_QUALITY_PRESET_SPEED                │
│    - AMF_VIDEO_ENCODER_USAGE_ULTRA_LOW_LATENCY             │
│    - Peak-Constrained Constant Bitrate (CBR)               │
│    - Zero B-Frames (AMF_VIDEO_ENCODER_B_PIC_PATTERN = 0)   │
│    - Progressive Intra-Refresh (Slice Columns)             │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│         Annex B / OBU Bitstream -> RTP/FEC Streamer        │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Intel QuickSync / oneVPL Architecture

```
┌────────────────────────────────────────────────────────────┐
│         Direct3D 11 Captured Desktop Texture / Frame       │
│           (DXGI / WGC Desktop Duplication Surface)         │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│               Intel oneVPL / QSV MFXSession                │
│    - MFXVideoCORE_SetHandle (MFX_HANDLE_D3D11_DEVICE)      │
│    - Direct mfxFrameSurface1 binding to DXGI Shared Handle │
└─────────────────────────────┬──────────────────────────────┘
                              │
       ┌──────────────────────┼──────────────────────┐
       ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│  Intel AVC   │       │Intel HEVC 10b│       │Intel Arc AV1 │
│ - Best Speed │       │ - P010 Surface│      │ - Alchemist/ │
│ - CBR Rate   │       │ - Low-Power  │       │   Battlemage │
│ - Zero-B     │       │   VDENC Mode │       │ - OBU Format │
└──────┬───────┘       └──────┬───────┘       └──────┬───────┘
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│            Intel QSV Low-Latency Configuration             │
│    - TargetUsage = MFX_TARGETUSAGE_BEST_SPEED (1)          │
│    - RateControlMethod = MFX_RATECONTROL_CBR               │
│    - AsyncDepth = 1 (Immediate emission, zero queue delay) │
│    - GopRefDist = 1 (Zero B-frames)                        │
│    - LowPower = MFX_CODINGOPTION_ON (Hardware VDENC engine)│
│    - Progressive Intra-Refresh (Slice Cycle Delta QP)      │
└─────────────────────────────┬──────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│         Annex B / OBU Bitstream -> RTP/FEC Streamer        │
└────────────────────────────────────────────────────────────┘
```

---

## 3. Dedicated C-ABI Export Methods

### A. AMD AMF Exports
```c
MOONSHINE_API int moonshine_amf_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

MOONSHINE_API int moonshine_amf_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t usage
);

MOONSHINE_API int moonshine_amf_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t mbs_per_slot
);
```

### B. Intel QuickSync / oneVPL Exports
```c
MOONSHINE_API int moonshine_qsv_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

MOONSHINE_API int moonshine_qsv_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t target_usage,
    int low_power_vdenc
);

MOONSHINE_API int moonshine_qsv_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t cycle_size,
    int32_t qp_delta
);
```

---

## 4. Managed Host Usage Examples

### A. AMD AMF Pipeline
```csharp
using var amfPipeline = new AmfHardwareEncoderPipeline(
    width: 3840,
    height: 2160,
    fps: 120,
    bitrateKbps: 45000,
    codec: VideoCodec.HevcMain10,
    preset: AmfQualityPreset.Speed,
    usage: AmfUsage.UltraLowLatency
);

amfPipeline.ConfigureIntraRefresh(enable: true, mbsPerSlot: 16);
```

### B. Intel QuickSync Pipeline
```csharp
using var qsvPipeline = new QsvHardwareEncoderPipeline(
    width: 2560,
    height: 1440,
    fps: 240,
    bitrateKbps: 38000,
    codec: VideoCodec.Av1,
    targetUsage: QsvTargetUsage.BestSpeed,
    lowPowerVdenc: true
);

qsvPipeline.ConfigureIntraRefresh(enable: true, cycleSize: 30, qpDelta: -2);
```

---

## 5. 9-Tier Matrix Conformance and Certification

Both the AMD AMF and Intel QuickSync pipelines include complete 9-tier matrix conformance test suites (`test_amf_conformance.cpp` and `test_qsv_conformance.cpp`):
1. **Defensive Error Handling**: Zero capacity buffers, null pointers, and double destruction protection.
2. **Resolution Matrix**: 720p HD, 1080p FHD, 1440p QHD, and 4K UHD.
3. **Codec Matrix**: H.264 / AVC, HEVC / H.265 Main10, and AV1 Profile 0.
4. **Deep NALU Validation**: Start codes (3/4-byte), monotonic indexing, and microsecond timestamps across 10 sequential frames.
5. **Direct3D 11 Video Decoder Hardware Loopback**: Complete encode-decode roundtrip validation.
6. **Dynamic Keyframe & Bitrate Reconfiguration**: Mid-stream bitrate modifications and forced IDR injection.
7. **Buffer Overrun Protection**: Canary byte validation (`0xAA`/`0xBB`) surrounding undersized buffers.
8. **Rapid Start/Stop Cycles**: 10 consecutive create/encode/destroy cycles.
9. **Multi-Instance Concurrency**: Dual simultaneous encoder sessions on shared Direct3D 11 devices.
