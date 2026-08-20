# HDR10 & Dynamic Color Space Metadata Engine

The **Moonshine Host HDR10 Subsystem** provides dynamic high-dynamic-range (HDR) display metadata extraction, real-time SMPTE ST 2084 / BT.2020 color volume serialization, and Direct3D 11/12 GPU color conversion.

---

## 1. Architectural Overview

When HDR is enabled on the host system, desktop backbuffers are rendered in wide-gamut 10-bit format (`DXGI_FORMAT_R10G10B10A2_UNORM`). To stream this content losslessly to HDR10-capable clients (such as OLED displays, HDR monitors, and TVs), Moonshine extracts display colorimetry and performs zero-CPU-overhead GPU color format adaptation.

```
┌──────────────────────────────────────────────────────────┐
│              Windows Desktop DWM (HDR Active)            │
│            DXGI_FORMAT_R10G10B10A2_UNORM (10-bit)        │
└────────────────────────────┬─────────────────────────────┘
                             │
              ┌──────────────┴──────────────┐
              ▼                             ▼
┌───────────────────────────┐ ┌───────────────────────────┐
│   IDXGIOutput6 (Desc1)    │ │   D3DColorSpaceConverter   │
│  - SMPTE ST 2086 Primaries│ │  - RGB10A2 -> P010 Surface │
│  - MaxCLL / MaxFALL       │ │  - BGRA8 -> NV12 Surface   │
│  - Peak / Min Luminance   │ │  - Zero CPU Memory Copies  │
└─────────────┬─────────────┘ └─────────────┬─────────────┘
              │                             │
              ▼                             ▼
┌───────────────────────────┐ ┌───────────────────────────┐
│   SEI Mastering Volume    │ │  Hardware Video Encoders  │
│  & RTSP / SDP Descriptors │ │  (NVENC HEVC Main10 / AV1)│
└───────────────────────────┘ └───────────────────────────┘
```

---

## 2. Mathematical Color Model & Transfer Functions

### A. BT.2020 RGB-to-YUV Conversion Matrix
For 10-bit HDR10 color encoding:
$$Y = 0.2627 \cdot R + 0.6780 \cdot G + 0.0593 \cdot B$$
$$U = -0.1396 \cdot R - 0.3604 \cdot G + 0.5000 \cdot B + \frac{512}{1023}$$
$$V = 0.5000 \cdot R - 0.4598 \cdot G - 0.0402 \cdot B + \frac{512}{1023}$$

### B. SMPTE ST 2084 Perceptual Quantizer (PQ)
The non-linear optical-to-electrical transfer function maps absolute luminance $L$ ($0 \le L \le 10000 \text{ cd/m}^2$):
$$V = \left( \frac{c_1 + c_2 \cdot L^{m_1}}{1 + c_3 \cdot L^{m_1}} \right)^{m_2}$$

Where:
- $m_1 = \frac{2610}{16384} = 0.1593017578125$
- $m_2 = \frac{2523}{4096} \cdot 128 = 78.84375$
- $c_1 = \frac{3424}{4096} = 0.8359375$
- $c_2 = \frac{2413}{4096} \cdot 32 = 18.8515625$
- $c_3 = \frac{2392}{4096} \cdot 32 = 18.6875$

---

## 3. C-ABI Interface

```c
typedef struct MoonshineHdr10Metadata {
    uint16_t red_primary[2];                // BT.2020 Red coordinates (scaled by 50000)
    uint16_t green_primary[2];              // BT.2020 Green coordinates (scaled by 50000)
    uint16_t blue_primary[2];               // BT.2020 Blue coordinates (scaled by 50000)
    uint16_t white_point[2];                // D65 White Point coordinates (scaled by 50000)
    uint32_t max_mastering_luminance;       // Max mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    uint32_t min_mastering_luminance;       // Min mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    uint16_t max_content_light_level;       // MaxCLL in nits
    uint16_t max_frame_average_light_level; // MaxFALL in nits
    uint8_t  hdr_enabled;                   // 1 if HDR10 active, 0 for SDR
    uint8_t  color_space;                   // 0 for BT.709, 1 for BT.2020
    uint8_t  reserved[2];                   // Padding for strict 32-byte alignment
} MoonshineHdr10Metadata;

MOONSHINE_API int moonshine_hdr_extract_metadata(
    void* hmonitor,
    MoonshineHdr10Metadata* out_metadata
);

MOONSHINE_API int moonshine_hdr_parse_capabilities(
    uint32_t color_space_dxgi,
    MoonshineHdr10Metadata* out_metadata
);

MOONSHINE_API MoonshineColorConverterHandle moonshine_color_converter_create(
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t in_format,
    uint32_t out_format
);

MOONSHINE_API int moonshine_color_converter_convert(
    MoonshineColorConverterHandle handle,
    void* in_texture,
    void* out_texture
);

MOONSHINE_API void moonshine_color_converter_destroy(
    MoonshineColorConverterHandle handle
);
```

---

## 4. Managed Orchestration & Protocol Serialization

### A. SEI Mastering Display Colour Volume Generation
Moonshine constructs ITU-T H.265 / H.264 Annex D.2.27 compliant SEI NAL units containing display color volume and Content Light Level (CLL) metadata to inform the client decoder:

```csharp
if (Hdr10MetadataExtractor.TryExtractMetadata(hmonitor, out var metadata))
{
    byte[] seiPayload = Hdr10MetadataExtractor.GenerateMasteringDisplaySeiPayload(metadata);
    // Inject seiPayload directly into HEVC / AV1 stream headers
}
```

### B. RTSP / SDP Negotiation
```csharp
string sdpAttributes = Hdr10MetadataExtractor.FormatSdpHdrAttributes(metadata, payloadType: 96);
// Emits: a=fmtp:96 color-primaries=9;transfer-characteristics=16;matrix-coefficients=9;mastering-display-color-volume=...;content-light-level=...
```
