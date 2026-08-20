# GPU Hardware Video Encoding Subsystem

The **Moonshine Host GPU Video Encoding Subsystem** provides a zero-copy, multi-vendor hardware video encoding engine engineered for sub-2ms encode latency at up to 4K 240 FPS across NVIDIA NVENC, AMD AMF (Advanced Media Framework), Intel QuickSync / oneVPL, and Direct3D 11/12 hardware surfaces.

---

## 1. Architectural Overview

```
┌──────────────────────────────────────────────────────────┐
│             Desktop Frame Capture & Color Conversion     │
│             (DXGI / WGC -> D3DColorSpaceConverter)       │
└────────────────────────────┬─────────────────────────────┘
                             │ Direct3D Texture Pointer
                             ▼
┌──────────────────────────────────────────────────────────┐
│              UnifiedHardwareEncoderEngine                │
│         - Auto-probes GPU vendor (NV / AMD / Intel)      │
│         - Fallback hierarchy: NVENC -> AMF -> QSV -> D3D11│
└────────────────────────────┬─────────────────────────────┘
                             │
       ┌─────────────────────┼─────────────────────┐
       ▼                     ▼                     ▼
┌──────────────┐      ┌──────────────┐      ┌──────────────┐
│ NVIDIA NVENC │      │   AMD AMF    │      │Intel QuickSync│
│ - P1/P2 Ultra│      │ - VCN Context│      │ - oneVPL Low │
│ - CBR Zero-B │      │ - D3D Direct │      │ - Lookahead  │
│ - HEVC 10-bit│      │ - HEVC 10-bit│      │ - AV1 & HEVC │
│ - AV1 OBU    │      │ - Dynamic IDR│      │ - Dynamic IDR│
└──────┬───────┘      └──────┬───────┘      └──────┬───────┘
       │                     │                     │
       └─────────────────────┼─────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│              Annex B / OBU Bitstream Emission            │
│         - Monotonically increasing QPC timestamps        │
│         - Instant IDR Keyframe on RTCP Packet Loss       │
│         - Zero GC Allocations in Streaming Hot Path      │
└────────────────────────────┬─────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│               RTP / FEC Packet Streaming Layer           │
└──────────────────────────────────────────────────────────┘
```

---

## 2. Rate Control & Ultra-Low Latency Tuning

### A. Rate Control Mathematics
Moonshine operates strictly under Constant Bitrate (CBR) or Constrained Quality (CQP) with strict buffer models to guarantee predictable frame sizes and prevent network buffer bloat:

$$\text{Target Frame Size (Bytes)} = \frac{\text{Bitrate (bps)}}{8 \cdot \text{FPS}}$$

For an IDR keyframe (intra-coded refresh):
$$\text{Keyframe Target Size} \approx 1.5 \cdot \text{Average Frame Size}$$

### B. Tuning Parameters
- **Preset**: Ultra-low latency (NVIDIA `P1`/`P2`, AMD `AMF_VIDEO_ENCODER_QUALITY_PRESET_SPEED`, Intel `MFX_TARGETUSAGE_BEST_SPEED`).
- **B-Frames**: Strictly 0 (`GOP length = infinite` or `0`, `max_b_frames = 0`) to eliminate frame reordering delays.
- **Intra-Refresh Mode**: Optional progressive intra-refresh across macroblock columns for seamless recovery without large I-frame burst spikes.
- **Dynamic IDR Injection**: Immediate generation of a keyframe upon client packet loss signaled via RTCP without destroying or reinitializing the encoder pipeline.

---

## 3. C-ABI Interface

```c
typedef struct MoonshineEncoderCaps {
    uint32_t supported_codecs_mask; // Bit 0: H264, Bit 1: HEVC, Bit 2: HEVC Main10, Bit 3: AV1
    uint32_t max_width;             // e.g. 4096 / 8192
    uint32_t max_height;            // e.g. 4096 / 8192
    uint32_t max_fps;               // e.g. 240
    uint8_t  supports_10bit;        // 1 if 10-bit HDR encoding supported
    uint8_t  supports_lossless;     // 1 if lossless encoding supported
    uint8_t  supports_smart_idr;    // 1 if dynamic IDR injection without full reset supported
    uint8_t  vendor_id;             // 1: NVENC, 2: AMF, 3: QSV, 4: D3D11
    uint32_t min_bitrate_kbps;      // Minimum bitrate (e.g. 500 kbps)
    uint32_t max_bitrate_kbps;      // Maximum bitrate (e.g. 200000 kbps)
    uint32_t reserved;              // Padding for strict 32-byte alignment
} MoonshineEncoderCaps;

typedef struct MoonshineEncoderConfig {
    uint32_t width;                 // Frame width in pixels
    uint32_t height;                // Frame height in pixels
    uint32_t fps;                   // Target framerate
    uint32_t bitrate_kbps;          // Target bitrate in kbps
    uint32_t peak_bitrate_kbps;     // Peak bitrate for VBR / bursts
    uint32_t codec;                 // 0: H264, 1: HEVC, 2: HEVC Main10, 3: AV1
    uint32_t rc_mode;               // 0: CBR, 1: VBR, 2: CQP
    uint16_t gop_length;            // GOP size
    uint8_t  enable_intra_refresh;  // 1 to enable progressive intra-refresh
    uint8_t  enable_filler_data;    // 1 to emit filler for strict CBR
} MoonshineEncoderConfig;

typedef struct MoonshineEncodedPacketDesc {
    uint64_t frame_index;           // Monotonically increasing frame index
    int64_t  timestamp_qpc;         // High-precision QPC timestamp
    uint32_t payload_size;          // Total size of encoded NAL / OBU bytes
    uint8_t  is_keyframe;           // 1 if IDR / SPS / PPS keyframe
    uint8_t  is_header_packet;      // 1 if packet contains VPS/SPS/PPS parameter sets
    uint8_t  temporal_id;           // Temporal layer ID
    uint8_t  reserved;              // Padding for strict 24-byte alignment
} MoonshineEncodedPacketDesc;

MOONSHINE_API int moonshine_encoder_query_caps(
    uint32_t vendor,
    void* d3d_device,
    MoonshineEncoderCaps* out_caps
);

MOONSHINE_API MoonshineEncoderHandle moonshine_encoder_create(
    uint32_t vendor,
    void* d3d_device,
    const MoonshineEncoderConfig* config
);

MOONSHINE_API int moonshine_encoder_encode_frame(
    MoonshineEncoderHandle handle,
    void* d3d_texture,
    int force_idr,
    MoonshineEncodedPacketDesc* out_desc,
    uint8_t* out_buffer,
    uint32_t max_buffer_size,
    uint32_t* out_size
);

MOONSHINE_API int moonshine_encoder_reconfigure(
    MoonshineEncoderHandle handle,
    const MoonshineEncoderConfig* new_config
);

MOONSHINE_API void moonshine_encoder_request_keyframe(
    MoonshineEncoderHandle handle
);

MOONSHINE_API void moonshine_encoder_destroy(
    MoonshineEncoderHandle handle
);
```

---

## 4. Managed Host Orchestration

### A. Initialization & Auto Vendor Selection
```csharp
using var engine = new UnifiedHardwareEncoderEngine(
    width: 3840,
    height: 2160,
    fps: 120,
    bitrateKbps: 45000,
    codec: VideoCodec.HevcMain10,
    rcMode: RateControlMode.ConstantBitrate,
    preferredVendor: EncoderVendor.Auto
);
```

### B. Streaming Hot Path & Frame Ingestion
```csharp
Span<byte> bitstreamBuffer = stackalloc byte[1024 * 512];
if (engine.TryEncodeFrame(d3dTextureHandle, forceIdr: false, out var desc, bitstreamBuffer, out int written))
{
    // Dispatch slice data directly into RTP packetizer with zero GC allocations
    _rtpStreamer.DispatchVideoPayload(desc, bitstreamBuffer.Slice(0, written));
}
```

### C. Congestion Feedback & Dynamic Reconfiguration
When RTCP receiver reports detect network packet drop or bandwidth constraints:
```csharp
engine.ReconfigureBitrate(newBitrateKbps: 30000, newFps: 120);
```
