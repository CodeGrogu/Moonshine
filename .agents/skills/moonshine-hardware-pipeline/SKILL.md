---
name: moonshine-hardware-pipeline
description: >-
  Runbook for configuring, profiling, and debugging hardware video decoding (Direct3D 11/12, Vulkan Video),
  DXGI flip model presentation, HDR10 tone mapping, and low-latency WASAPI Exclusive audio.
  Use when analyzing video stutter, decode latency, color space mismatches, or audio buffer underruns.
---

# Moonshine Hardware Pipeline Skill

This skill guides the hardware video decode and audio rendering subsystems in Moonshine.

## Hardware Video Acceleration Standards

### 1. Direct3D 11/12 Video Decoding
- Hardware decoder initialized via `ID3D11VideoDevice` / `ID3D12VideoDecoder`.
- Supported hardware profiles:
  - **AV1**: `D3D11_DECODER_PROFILE_AV1_VLD_MAIN10`
  - **HEVC / H.265**: `D3D11_DECODER_PROFILE_HEVC_VLD_MAIN` & `MAIN10`
  - **H.264 / AVC**: `D3D11_DECODER_PROFILE_H264_VLD_NOFGT`

### 2. DXGI Low-Latency Presentation
- Swap chain creation with `DXGI_SWAP_EFFECT_FLIP_DISCARD`.
- `DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` enabled for Variable Refresh Rate (G-Sync / FreeSync).
- Frame latency waitable object (`IDXGISwapChain2::GetFrameLatencyWaitableObject`) configured with `SetMaximumFrameLatency(1)`.

### 3. WASAPI Low-Latency Audio
- Exclusive mode initialization (`AUDCLNT_SHAREMODE_EXCLUSIVE`) bypassing Windows mixer.
- Periodic buffer size set to minimum device period (e.g. 2.5ms - 5ms).
