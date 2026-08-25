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
- 22 native test targets              - Moonshine.Protocol.Tests           - ASan (AddressSanitizer)
  including MNBP v1 and               - Moonshine.Interop.Tests            - UBSan (UndefinedBehavior)
  legacy compatibility                - Moonshine.Core.Tests               - TSan (ThreadSanitizer)
  layers                                                                   - BenchmarkDotNet 0B Alloc
```

---

## 2. Exhaustive Test Matrix by Module

### A. Forward Error Correction (`test_fec_simd.cpp`)
- Vector XOR alignment: 0 bytes, 1 byte, 15 bytes, 31 bytes, 32 bytes, 33 bytes, 63 bytes, 1400 bytes.
- Self-inverse properties: $x \oplus x = 0$ and $x \oplus 0 = x$.
- Galois Field $GF(2^8)$ multiplication identities: $0 \otimes x = 0$ and $1 \otimes x = x$.
- Single parity shard recovery under MTU data loss.
- Boundary error handling: Null pointer rejection, zero shard count rejection.

### B. Lock-Free SPSC Ring Buffer (`test_spsc_ring_buffer.cpp`)
- Single element push and pop correctness.
- Full capacity rejection: Pushing to a full queue returns false without corruption.
- Empty queue rejection: Popping from an empty queue returns false.
- Multi-threaded stress test: 1,000,000 items concurrently pushed and popped across independent CPU cores with zero drops.
- Ring index wraparound across thousands of continuous cycles.

### C. Predictive Jitter Buffer (`test_jitter_buffer.cpp`)
- Single-slice frame completion.
- Multi-slice frame reassembly with reverse-order packet arrival.
- Circular slot rollover across 1,000 consecutive frames without memory leaks.

### D. C-ABI Native Export Boundary (`test_c_abi_export.cpp`)
- Verification of P/Invoke entry points: `Moonshine_VectorXor`, `Moonshine_Spsc*`, `Moonshine_Jitter*`, `Moonshine_VideoQueryCaps`.
- Null pointer resilience and error return codes.

### E. Managed Protocol Engine (`Moonshine.Protocol.Tests`)
- **MNBP v1 Protocol Tests**: Full test suite for Moonshine's native MNBP v1 protocol framing and control operations.
- `RtpHeader`: RTP packet header parsing, 12-byte minimum size enforcement, flag extraction, payload slicing (tests legacy compatibility layer).
- `RtpAudioHeader`: Opus audio RTP parsing and metadata extraction (legacy compatibility).
- `RtpSequenceUnwrapper`: 64-bit monotonic sequence unwrapping across 16-bit boundaries ($65535 \rightarrow 0$) and late-arriving packet handling.
- `FecHeader`: Binary header parsing and shard count validation.
- `RtspMessage`: Request and response serialisation, method parsing, header extraction, and body size handling (legacy compatibility).
- `AesGcmHelper`: PIN/salt key derivation, AES-GCM encryption/decryption roundtrips, tampered ciphertext rejection.
- `ControlPacket`: Ping/Pong serialisation, IDR frame requests, and loss report payload formatting.
- `InputPacket`: 1000Hz controller state bitmasks, stick coordinate normalisation, and high-DPI mouse event packing.

### F. Managed Interop & Core (`Moonshine.Interop.Tests`, `Moonshine.Core.Tests`)
- `StructLayoutTests`: Exact byte sizing and alignment for `MoonshinePacketDesc`, `MoonshineFrameDesc`, and `MoonshineDecoderCaps`.
- `NativeMemoryOwnerTests`: Unmanaged memory allocation, zero-copy span access, and double-dispose safety.
- `DiscoveryTests`: Sunshine XML serverinfo response parsing (legacy compatibility).
- `PairingTests`: Self-signed RSA-2048/X.509 client certificate and private key generation.
- `UdpSocketPipelineTests`: Socket pipeline lifecycle and resource cleanup.
- `MoonshineStreamSessionTests`: Session initialisation and default state validation.
