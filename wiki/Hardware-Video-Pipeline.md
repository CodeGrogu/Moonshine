# Hardware Video Decode and Presentation Pipeline

## 1. Zero-Copy Video Pipeline Architecture

Moonshine bypasses intermediate CPU frame copying by feeding decoded video bitstreams directly into GPU hardware decoders via native Direct3D 11/12 and Vulkan Video pipelines.

```
Reassembled Frame Bitstream (AV1 / HEVC / H.264)
       │
       ▼
Hardware Video Decoder Device (D3D11VA / D3D12 Video / Vulkan)
       │
       ▼  (Direct GPU Texture Surface - NV12 / P010)
DXGI Swap Chain Presentation (DXGI_SWAP_EFFECT_FLIP_DISCARD)
       │
       ▼  (Sub-Frame Presentation Waitable Object)
Display Output (HDR10 Rec.2020 / 240Hz+ VRR)
```

---

## 2. Direct3D 11 and Direct3D 12 Hardware Decoding

### Direct3D 11 Video Acceleration (D3D11VA)
- Uses `ID3D11VideoDevice` and `ID3D11VideoDecoder` for hardware-accelerated slice decoding.
- Decodes directly into DXGI texture arrays without unmanaged-to-managed copies.
- Supports 8-bit NV12 and 10-bit P010 surface formats for HDR10 and Wide Colour Gamut (WCG).

### Direct3D 12 Low-Overhead Pipeline
- Utilises `ID3D12VideoDecoder` and independent video command queues.
- Asynchronous GPU command list submission parallelised with CPU socket ingestion.
- Explicit fence synchronisation between video decode completion and the Direct3D 12 direct render queue.

---

## 3. Sub-Frame Presentation (DXGI Flip Model)

Traditional presentation models (`DXGI_SWAP_EFFECT_DISCARD` or windowed blit) buffer frames in the Desktop Window Manager (DWM) composition queue, introducing between 8ms and 16ms of presentation delay.

Moonshine implements modern DXGI Flip Model (`DXGI_SWAP_EFFECT_FLIP_DISCARD`):
1. Independent Flip (`DirectComposition` / Borderless Fullscreen): Bypasses DWM compositing entirely and hands the decoded frame texture directly to the display scanout engine.
2. Presentation Waitable Object (`CreateSwapChainForHwnd` with `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`): Moonshine sets maximum frame latency to 1 (`SetMaximumFrameLatency(1)`), eliminating render queue buffering and presenting within sub-frame timing ($< 1.0\,\text{ms}$).
3. Variable Refresh Rate (VRR / G-Sync / FreeSync): Automatically disables V-Sync tearing blocks and matches presentation cadence to incoming host frame delivery timestamps.
