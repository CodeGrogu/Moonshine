# Hardware Video Decoding Pipeline (Direct3D 11 & Direct3D 12)

Moonshine implements an ultra-low-latency, zero-copy hardware video decoding pipeline built natively in C++23 utilizing Direct3D 11 Video Acceleration (D3D11VA) and Direct3D 12 Video Decode APIs.

---

## 1. Zero-Copy Hardware Video Architecture

```
UDP Socket / Jitter Buffer (Native Arena)
                    │
                    │ Direct frame bitstream pointer (byte*)
                    ▼
MoonshineVideoPipeline (.NET 9 Managed Layer)
                    │
                    │ Blittable MoonshineFrameDesc
                    ▼
C-ABI Interop Bridge (moonshine_video_submit_frame)
                    │
                    ├─► D3D11VideoDecoder (ID3D11VideoContext / ID3D11VideoDecoder)
                    └─► D3D12VideoDecoder (ID3D12VideoDevice / ID3D12VideoDecoder)
                    │
                    ▼
Direct Surface Decode (Zero Host-to-Device Copies)
                    │
                    ├─► DXGI_FORMAT_NV12 (8-bit SDR H.264 / HEVC)
                    └─► DXGI_FORMAT_P010 (10-bit HDR10 HEVC Main10 / AV1)
                    │
                    ▼
DXGI Flip Model Swapchain (Direct Screen Presentation < 1ms)
```

---

## 2. Multi-Codec Video Decoder Matrix

Moonshine negotiates hardware decoder profiles dynamically based on GPU capabilities:

| Codec | Profile / Pixel Format | D3D11 Decoder Profile GUID | Output Format |
| :--- | :--- | :--- | :--- |
| **H.264** | High Profile / 8-bit | `D3D11_DECODER_PROFILE_H264_VLD_NOFGT` | `DXGI_FORMAT_NV12` |
| **HEVC (H.265)** | Main Profile / 8-bit SDR | `D3D11_DECODER_PROFILE_HEVC_VLD_MAIN` | `DXGI_FORMAT_NV12` |
| **HEVC Main10** | Main 10 Profile / 10-bit HDR10 | `D3D11_DECODER_PROFILE_HEVC_VLD_MAIN10` | `DXGI_FORMAT_P010` |
| **AV1** | Profile 0 / 8-bit & 10-bit | `D3D11_DECODER_PROFILE_AV1_VLD_PROFILE0` | `DXGI_FORMAT_NV12` / `P010` |

---

## 3. Direct Surface Decoding and Zero-Copy Discipline

To achieve sub-millisecond decode latency at 4K 120 FPS:

1. **Host-to-Device Bypass**:
   - Bitstream buffers reconstructed by the predictive jitter buffer reside in pinned native memory slabs.
   - Pointers are passed directly into `ID3D11VideoContext::SubmitDecoderBuffers` / `ID3D12VideoDecodeCommandList::DecodeFrame`.
2. **Direct GPU Texture Output**:
   - Decoded macroblocks write directly into GPU VRAM video surfaces (`ID3D11Texture2D` or `ID3D12Resource`).
   - Surfaces are bound directly as shader resource views (`ID3D11ShaderResourceView`) or presented via DXGI Flip Model swapchains with zero CPU readbacks or host buffer blits.

---

## 4. Low-Latency DXGI Flip Model Swapchain (`DxgiSwapchainPipeline`)

Moonshine integrates a custom DXGI Flip Model swapchain presentation engine for Windows 11:

### A. Flip Discard and Tearing (VRR)
- **`DXGI_SWAP_EFFECT_FLIP_DISCARD`**: Completely bypasses Desktop Window Manager (DWM) redirection surfaces. The GPU presents the decoded back-buffer directly to the display scanout.
- **`DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` & `DXGI_PRESENT_ALLOW_TEARING`**: Enables seamless Variable Refresh Rate (VRR / NVIDIA G-Sync / AMD FreeSync) presentation without tearing or micro-stuttering.
- **`DXGI_MWA_NO_ALT_ENTER`**: Prevents legacy DXGI window hooks from interfering with high-frame-rate streaming.

### B. True 10-bit HDR10 Rec.2020 Color Spaces
- **SDR Standard**: `DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709` over `DXGI_FORMAT_B8G8R8A8_UNORM`.
- **HDR10 Wide Color Gamut**: `DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020` over `DXGI_FORMAT_R10G10B10A2_UNORM` configured via `IDXGISwapChain3::SetColorSpace1`.

---

## 5. Hardware Capability Telemetry (`QueryCaps`)

The native bridge queries the active GPU adapter capabilities:
- **`MaxWidth` / `MaxHeight`**: Maximum hardware resolution (up to 7680x4320 8K).
- **`MaxFps`**: Maximum hardware refresh capability (up to 240 FPS).
- **`SupportsAv1` / `SupportsHevc` / `SupportsH264`**: Codec decoder presence.
- **`SupportsHdr10` / `Supports10Bit`**: 10-bit Rec.2020 wide color gamut capability.
- **`SupportsD3D12` / `SupportsVulkan`**: Modern low-overhead graphics API decode availability.
