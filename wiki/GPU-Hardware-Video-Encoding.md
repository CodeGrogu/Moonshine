> [!WARNING]
> **Status: Incomplete / Fail-Closed**
> Moonshine is in early development (v0.5.6-alpha) and is fail-closed by design. End-to-end streaming is not yet operational. Hardware encoders are implemented as capability-gated subsystems verified in unit and conformance test suites; the full host-to-client pipeline described here is a design target. Moonshine uses the first-party MNBP v1 protocol, not RTP/RTCP.

# GPU Hardware Video Encoding Subsystem

The **Moonshine Host GPU Video Encoding Subsystem** provides a multi-vendor hardware video encoding engine designed with a target of sub-2ms encode latency and up to 4K 240 FPS operation across NVIDIA NVENC, AMD AMF (Advanced Media Framework), Intel QuickSync / oneVPL, and Direct3D 11 hardware surfaces. These latency and throughput targets are architectural design goals and are not yet demonstrated in an end-to-end streaming deployment.

---

## 1. Architectural Overview & Vendor Selection

```
┌──────────────────────────────────────────────────────────┐
│             Desktop Frame Capture & Colour Conversion    │
│             (DXGI / WGC -> D3DColourSpaceConverter)      │
└────────────────────────────┬─────────────────────────────┘
                             │ Direct3D Texture Pointer
                             ▼
┌──────────────────────────────────────────────────────────┐
│              UnifiedHardwareEncoderEngine                │
│         - Auto-detects adapter PCI VendorId              │
│         - Fallback sequence: NVENC -> AMF -> QSV -> D3D11│
└────────────────────────────┬─────────────────────────────┘
                             │
       ┌─────────────────────┼─────────────────────┐
       ▼                     ▼                     ▼
┌──────────────┐      ┌──────────────┐      ┌──────────────┐
│ NVIDIA NVENC │      │   AMD AMF    │      │Intel QuickSync│
│ - Vendor 10DE│      │ - Vendor 1002│      │ - Vendor 8086│
│ - P1/P2 Fast │      │ - VCN Context│      │ - oneVPL Low │
│ - CBR Zero-B │      │ - D3D Direct │      │ - Best Speed │
│ - HEVC/AV1   │      │ - HEVC/AV1   │      │ - HEVC/AV1   │
└──────┬───────┘      └──────┬───────┘      └──────┬───────┘
       │                     │                     │
       └─────────────────────┼─────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│              Annex B / OBU Bitstream Emission            │
│         - Monotonically increasing QPC timestamps        │
│         - Instant IDR Keyframe on MNBP Loss Signals      │
│         - Zero GC Allocations in Streaming Hot Path      │
└────────────────────────────┬─────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│               MNBP / FEC Packet Streaming Layer          │
└──────────────────────────────────────────────────────────┘
```

### Auto-Detection vs. Fallback Hierarchy

The native engine distinguishes between adapter-aware auto-detection and generic fallback ordering:

1. **Adapter-Aware Auto-Detection (`UnifiedVideoEncoder::query_capabilities`)**:
   When a Direct3D 11 device handle (`ID3D11Device*`) is supplied, the native code queries the underlying `IDXGIDevice` $\rightarrow$ `IDXGIAdapter` and inspects `DXGI_ADAPTER_DESC::VendorId`:
   - **NVIDIA (`0x10DE`)**: Directly routes to `NvencVideoEncoder`.
   - **AMD (`0x1002`)**: Directly routes to `AmfVideoEncoder`.
   - **Intel (`0x8086`)**: Directly routes to `QsvVideoEncoder`.
   - **Generic / Other**: Rejects software rasterisers (`DXGI_ADAPTER_FLAG_SOFTWARE`) and routes to `D3D11HardwareEncoder`.

2. **Deterministic Fallback Order (`UnifiedVideoEncoder::initialize`)**:
   When `EncoderVendor::Auto` is specified without an explicit vendor binding, the engine iterates through a deterministic fallback cascade:
   $$\text{NVIDIA NVENC} \longrightarrow \text{AMD AMF} \longrightarrow \text{Intel QuickSync} \longrightarrow \text{Direct3D 11 Hardware}$$
   Each candidate is instantiated and initialised against the device; the first encoder that successfully acquires hardware context and confirms capability support is retained as the active encoder.

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
- **Dynamic IDR Injection**: Immediate generation of a keyframe upon client packet loss signalled via MNBP without destroying or reinitialising the encoder pipeline.

---

## 3. Normative C ABI Contract

> **Normative ABI Specification**: The following data structures define the strict, blittable binary contract across the native (`Moonshine.Native.dll`) and managed (`Moonshine.Interop`) boundary. Defined canonically in [`src/Moonshine.Native/include/moonshine/export/moonshine_native_api.h`](https://github.com/CodeGrogu/Moonshine/blob/main/src/Moonshine.Native/include/moonshine/export/moonshine_native_api.h#L850-L885). Field order, byte widths, padding, and alignments are normative and strictly verified by compile-time `static_assert` statements in C++ and layout assertions in .NET.

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

## 4. Managed Host Orchestration (Subsystem API Pattern)

The managed layer wraps native encoder handles in safe disposal patterns with zero GC allocation in hot paths:

### A. Initialisation & Auto Vendor Selection
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

### B. Frame Ingestion & Bitstream Emission
```csharp
Span<byte> bitstreamBuffer = stackalloc byte[1024 * 512];
if (engine.TryEncodeFrame(d3dTextureHandle, forceIdr: false, out var desc, bitstreamBuffer, out int written))
{
    // Dispatch slice data directly into MNBP packetiser with zero GC allocations
    _mnbpStreamer.DispatchVideoPayload(desc, bitstreamBuffer.Slice(0, written));
}
```

### C. Congestion Feedback & Dynamic Reconfiguration
When MNBP receiver reports detect network packet drop or bandwidth constraints:
```csharp
engine.ReconfigureBitrate(newBitrateKbps: 30000, newFps: 120);
```
