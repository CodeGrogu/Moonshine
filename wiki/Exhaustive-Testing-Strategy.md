> [!WARNING]
> **Status Disclaimer:** Moonshine is in active development (v0.5.6-alpha). It is its own platform with its own protocol (MNBP v1), not a GameStream client or Moonlight replacement. No end-to-end streaming works yet. The application is fail-closed.

# Exhaustive Testing Strategy and Quality Assurance

Moonshine enforces a cautious, multi-tier testing discipline. Every module, helper, mathematical operation, and memory boundary is tested against edge cases, corrupted inputs, out-of-order packet delivery, and concurrent thread contention.

---

## 1. Test Suite Architecture

```
                                  [ Moonshine Quality Assurance ]
                                                 │
            ┌────────────────────────────────────┼────────────────────────────────────┐
            ▼                                    ▼                                    ▼
[ Native C++23 CTest Suites ]         [ Managed .NET 9 xUnit Suites ]      [ Memory & Concurrency Sanitizers ]
- 25 native test targets              - Moonshine.Protocol.Tests (102)     - ASan (AddressSanitizer)
  including MNBP v1, capture,         - Moonshine.Core.Tests (214)         - UBSan (UndefinedBehavior)
  hardware encoders, and drivers      - Moonshine.Host.Tests (308)         - TSan (ThreadSanitizer)
                                      - Moonshine.Interop.Tests (88)       - BenchmarkDotNet 0B Alloc
```

---

## 2. Exhaustive Test Matrix by Module

### A. Forward Error Correction & Concurrency (`test_fec_simd.cpp`, `test_spsc_ring_buffer.cpp`, `test_jitter_buffer.cpp`)
- **FEC SIMD**: Vector XOR alignment (0B, 1B, 15B, 31B, 32B, 33B, 63B, 1400B), Galois Field $GF(2^8)$ multiplication identities, single-parity shard recovery under MTU data loss, null-pointer and zero-shard defensive handling.
- **SPSC Ring Buffer**: Push/pop correctness, full-capacity rejection, empty queue rejection, multi-threaded stress test (1,000,000 items pushed/popped across cores with zero drops), index wraparound.
- **Predictive Jitter Buffer**: Single-slice frame completion, multi-slice frame reassembly with reverse-order packet arrival, circular slot rollover across 1,000 consecutive frames without leaks.

### B. C-ABI Native Export Boundary (`test_c_abi_export.cpp`)
- Verification of P/Invoke entry points: `Moonshine_VectorXor`, `Moonshine_Spsc*`, `Moonshine_Jitter*`, `Moonshine_VideoQueryCaps`, encoder/decoder lifecycle exports.
- Null pointer resilience, zero-buffer defenses, structured exception handling, and error return codes.

### C. Desktop Capture & Colour Pipelines (`test_desktop_capture.cpp`, `test_wgc_capture.cpp`, `test_hdr_colorimetry.cpp`)
- IDXGIOutputDuplication and Windows.Graphics.Capture session initialisation and error recovery.
- HDR10 metadata extraction, colour space gamut validation, SMPTE ST 2084 PQ conversion, and surface sharing.

### D. Multi-Vendor Hardware Encoders & Conformance (`test_hardware_encoders.cpp`, `test_nvenc_*.cpp`, `test_amf_*.cpp`, `test_qsv_*.cpp`)
- Dynamic adapter vendor detection (NVIDIA `0x10DE`, AMD `0x1002`, Intel `0x8086`, D3D11 hardware).
- 9-tier matrix conformance: defensive error handling, resolution matrix (720p to 4K), codec matrix (H.264, HEVC Main10, AV1), NALU start codes, Direct3D 11 decoder hardware loopbacks, dynamic IDR keyframe injection, buffer overrun protection with canary bytes, rapid start/stop cycles, and multi-instance concurrency.

### E. Audio Subsystem & Virtual Driver (`test_wasapi_*.cpp`, `test_opus_*.cpp`, `test_mic_sink.cpp`, `test_virtual_audio_*.cpp`)
- WASAPI loopback master audio capture and Exclusive mode rendering.
- Low-latency Opus float encoding and decoding.
- Virtual audio driver WaveRT miniport interface validation and shared memory IPC ring buffer coherency.

### F. Protocol, Video Decode & Presentation (`test_moonshine_protocol.cpp`, `test_video_decoder.cpp`, `test_input_injector.cpp`, `test_swapchain_presenter.cpp`)
- MNBP v1 native packet parsing, Direct3D 11/12 video decode profile discovery, Win32 input injection coordinate normalisation, and DXGI Flip Model swapchain presentation.

### G. Managed Protocol Engine (`Moonshine.Protocol.Tests` - 102 tests)
- **MNBP v1 Protocol Tests**: Full test suite for Moonshine's native MNBP v1 protocol framing, control operations, and feedback envelopes.
- `RtpHeader`, `RtpAudioHeader`, `RtpSequenceUnwrapper`, `RtspMessage`: Legacy compatibility codecs tested in isolation.
- `AesGcmHelper`: PIN/salt key derivation, AES-GCM encryption/decryption roundtrips, tampered ciphertext rejection.
- `ControlPacket` & `InputPacket`: 1000Hz controller state bitmasks, stick coordinate normalisation, and high-DPI mouse event packing.

### H. Managed Host, Core & Interop (`Moonshine.Host.Tests` - 308 tests, `Moonshine.Core.Tests` - 214 tests, `Moonshine.Interop.Tests` - 88 tests)
- `HardwareVideoEncoderConformanceTests`: Full lifecycle, dynamic bitrate adjustment, IDR injection, buffer size safety, and D3D11 decoder loopbacks across NVENC, AMF, and QSV.
- `HostAudioPipelineTests` & `HostCoordinatorTests`: Audio processing loops, MMCSS thread registration, and lifecycle state machines.
- `StructLayoutTests` & `NativeMemoryOwnerTests`: Exact byte sizing and alignment assertions across C# and C++ blittable structures.
- `DiscoveryTests` & `PairingTests`: Sunshine XML serverinfo response parsing and RSA-2048/X.509 certificate exchange (legacy compatibility).
