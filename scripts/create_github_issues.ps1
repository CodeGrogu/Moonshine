# Script to create structured GitHub issues for Moonshine milestones

$Repo = "CodeGrogu/Moonshine"

$MS1 = "v0.1.0 - Alpha: Protocol Ingestion & Native SIMD Pipeline"
$MS2 = "v0.2.0 - Beta: Hardware Acceleration & Presentation Subsystem"
$MS3 = "v1.0.0 - Production: 1000Hz Input & Sub-5ms Streaming Engine"

$Issues = @(
    @{
        title = "[FEAT] Implement Live LAN Host Discovery via mDNS and SSDP Broadcasts"
        milestone = $MS1
        labels = "area: network-io,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Implement automated zero-configuration host discovery across the local subnet to find Sunshine and NVIDIA GameStream servers without requiring manual IP address entry.

## Requirements
- **mDNS Service Discovery**: Broadcast query on UDP port 5353 for service `_nvstream._tcp.local`.
- **SSDP Broadcast**: Broadcast UDP packet on `239.255.255.250:48010` to discover active GameStream hosts.
- **ServerInfo HTTP Probe**: Poll `http://<host>:47989/serverinfo` and parse XML metadata (host name, pairing state, supported codecs, app list).
- **Zero-Allocation Parsing**: Utilize `System.IO.Pipelines` and `XmlReader` over `ReadOnlySpan<byte>` for zero heap allocations during discovery sweeps.

## Acceptance Criteria
- [ ] Sunshine server discovered on LAN within 500ms of application start.
- [ ] Unit tests for mDNS packet parsing and SSDP responses.
- [ ] Documented in GitHub wiki under `wiki/GameStream-Sunshine-Protocol.md`.
"@
    },
    @{
        title = "[FEAT] Implement End-to-End Cryptographic Pairing Pipeline (X.509 / PBKDF2 / AES-128-GCM)"
        milestone = $MS1
        labels = "area: pairing-crypto,type: feature,priority: critical-path,type: security"
        body = @"
## Architectural Overview
Implement the full cryptographic pairing sequence required by Sunshine / GameStream servers to establish trusted communication.

## Cryptographic Protocol Details
1. **X.509 Certificate Generation**: Generate an RSA 2048-bit keypair and self-signed X.509 certificate on first client run.
2. **PIN & Salt Exchange**: Generate 4-digit PIN, exchange random 16-byte salts with host.
3. **Key Derivation**: Derive AES key via PBKDF2 (SHA-256, 16-byte key length).
4. **Challenge-Response**: Encrypt random 16-byte client challenge via AES-128-GCM; verify server response and mutual authentication.
5. **Keystore Storage**: Persist trusted server certificates and client keys securely.

## Acceptance Criteria
- [ ] Successful pairing with clean Sunshine v0.23+ installation using generated 4-digit PIN.
- [ ] Passing test suite in `Moonshine.Core.Tests/PairingTests.cs`.
- [ ] Cryptographic mathematical specifications heavily documented in `wiki/`.
"@
    },
    @{
        title = "[FEAT] Build Stateful RTSP Stream Control Client & Dynamic SDP Negotiation"
        milestone = $MS1
        labels = "area: protocol-rtsp,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Implement a high-performance, stateful RTSP client to orchestrate the video/audio stream lifecycle with Sunshine over TCP port 48010.

## Protocol Sequence
- `OPTIONS`: Query host server capabilities.
- `DESCRIBE`: Request Session Description Protocol (SDP) configuration.
- `SETUP`: Configure video stream (port 47998) and audio stream (port 48000).
- `PLAY`: Initiate real-time UDP packet delivery.
- `ANNOUNCE`: Send dynamic bitrate updates and loss stats.
- `TEARDOWN`: Gracefully terminate stream session.

## Dynamic SDP Parameter Negotiation
- Negotiate resolution (1080p, 1440p, 4K), frame rate (60Hz, 120Hz, 240Hz), and target bitrates (10Mbps - 150Mbps).
- Negotiate codec payload IDs (H.264 / HEVC / AV1).
- Negotiate HDR10 static metadata (SMPTE 2086 / CTA-861-G mastering display primaries).

## Acceptance Criteria
- [ ] Full RTSP state machine operating with zero GC allocations per request.
- [ ] Test suite validating all RTSP request/response permutations in `Moonshine.Protocol.Tests`.
"@
    },
    @{
        title = "[FEAT] Complete High-Throughput Zero-Copy UDP Ingestion Pipeline via SocketAsyncEngine"
        milestone = $MS1
        labels = "area: network-io,area: protocol-rtp,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Engine an ultra-high-throughput UDP packet receiver capable of processing up to 250,000 packets per second (150+ Mbps video bitrate) without frame drops or GC pauses.

## Technical Architecture
- **Pinned Buffer Pools**: Allocate contiguous native memory slabs via `NativeMemoryOwner`.
- **Zero-Allocation Socket Loop**: Use `SocketReceiveMessageFromResult` / `System.IO.Pipelines.Pipe`.
- **RTP Demuxing**: Extract video, audio, and FEC parity packets in-place using `ReadOnlySpan<byte>`.
- **Sequence Unwrapping**: Monotonically unwrap 16-bit RTP sequence numbers to 64-bit epoch counters using RFC 3550 signed modular arithmetic.
- **Native Ring Buffer Dispatch**: Push descriptors directly into lock-free C++23 SPSC circular ring buffer.

## Acceptance Criteria
- [ ] 0 bytes GC allocated per frame in `UdpSocketPipeline`.
- [ ] Micro-benchmarks proving sub-microsecond packet ingestion latency in BenchmarkDotNet.
"@
    },
    @{
        title = "[FEAT] AVX-512 / GFNI Vector Acceleration Kernel for Multi-Shard Galois Field FEC"
        milestone = $MS1
        labels = "area: native-simd,type: feature,priority: high,status: benchmark-required"
        body = @"
## Architectural Overview
Extend the custom Galois Field GF(2^8) Reed-Solomon Forward Error Correction engine with AVX-512 and Intel GFNI (Galois Field New Instructions) matrix multiplication kernels.

## Mathematical Formulation
For polynomial \$P(x) = x^8 + x^4 + x^3 + x^2 + 1\$ (0x11D):
- Utilize 512-bit ZMM registers to compute 64 bytes of Galois Field multiplication simultaneously.
- Utilize `_mm512_gf2p8affine_epi64_epi8` for tableless single-cycle affine transformations.
- Compare against AVX2 4-bit nibble decomposition lookup benchmarks.

## Acceptance Criteria
- [ ] AVX-512 / GFNI kernel passing all boundary, inversion, and reconstruction tests in `test_fec_simd.cpp`.
- [ ] BenchmarkDotNet demonstrating > 2x throughput scaling over AVX2 on supported hardware.
- [ ] Mathematical equations and assembly instruction sequences documented in `wiki/Custom-SIMD-Galois-Field-FEC.md`.
"@
    },
    @{
        title = "[FEAT] Implement Direct3D 11/12 Hardware Video Decoding Subsystem (D3D11VA / D3D12 Video)"
        milestone = $MS2
        labels = "area: video-d3d,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Build a high-performance C++23 hardware video decoding pipeline using Direct3D 11 Video Acceleration (D3D11VA) and Direct3D 12 Video Decode APIs.

## Key Capabilities
- **Codec Support**: Hardware accelerated decoding for HEVC (Main / Main10), AV1 (10-bit), and H.264 (High profile).
- **Zero-Copy Texture Sharing**: Decode directly into `ID3D11Texture2D` / `ID3D12Resource` NV12/P010 surfaces without intermediate host memory copies.
- **Sub-Millisecond Execution**: Target decode times under 1.0ms for 4K 120 FPS video streams.

## Acceptance Criteria
- [ ] Hardware decoding verified on NVIDIA (NVDEC), AMD (VCN), and Intel (QuickSync).
- [ ] Zero copy from jitter buffer arena directly to decoder input slice buffer.
- [ ] Comprehensive documentation in `wiki/Hardware-Video-Pipeline.md`.
"@
    },
    @{
        title = "[FEAT] Implement Low-Latency DXGI Flip Model Swapchain Presentation (< 1ms overhead)"
        milestone = $MS2
        labels = "area: video-d3d,type: feature,priority: high,status: benchmark-required"
        body = @"
## Architectural Overview
Implement a low-overhead presentation engine using the modern Windows DXGI Flip Model swapchain (`DXGI_SWAP_EFFECT_FLIP_DISCARD`) to achieve minimum display latency.

## Key Features
- **Tearing Support**: Enable `DXGI_PRESENT_ALLOW_TEARING` for variable refresh rate (G-Sync / FreeSync) monitors.
- **Sub-Millisecond Present**: Bypass Desktop Window Manager (DWM) composition redirection overhead.
- **HDR10 ST 2084 Output**: Configure `DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020` for true 10-bit Rec.2020 HDR rendering.

## Acceptance Criteria
- [ ] Measured presentation overhead < 0.5ms on Windows 11.
- [ ] Zero frame tearing or micro-stutter during 120Hz/240Hz streaming.
- [ ] Architecture documented in `wiki/Hardware-Video-Pipeline.md`.
"@
    },
    @{
        title = "[FEAT] Implement Sub-5ms WASAPI Exclusive Audio Rendering Engine"
        milestone = $MS2
        labels = "area: audio-wasapi,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Implement an ultra-low latency audio playback subsystem using Windows Audio Session API (WASAPI) in Exclusive Mode.

## Technical Requirements
- **Exclusive Mode Operation**: Bypass Windows Audio Engine mixer to eliminate resampling and buffering latency.
- **Sub-5ms Latency**: Target audio buffer periods of 2.6ms - 4.0ms (128 - 192 samples @ 48kHz).
- **Opus Decompression**: Real-time decompression of Opus audio packets into 32-bit floating point PCM.
- **Surround Sound**: Support stereo (2.0), 5.1 surround, and 7.1 surround sound configurations.

## Acceptance Criteria
- [ ] WASAPI Exclusive mode buffer rendered without underruns or buffer glitches.
- [ ] Measured audio render latency < 5.0ms.
- [ ] Documentation updated in `wiki/Audio-Engine-WASAPI.md`.
"@
    },
    @{
        title = "[FEAT] Implement 1000Hz High-Resolution Raw Input Polling Engine (HID / XInput / RawMouse)"
        milestone = $MS3
        labels = "area: input-hid,type: feature,priority: critical-path"
        body = @"
## Architectural Overview
Design and implement a high-resolution input polling loop operating at 1000Hz to deliver instant mouse, keyboard, and gamepad feedback to the remote host.

## Input Mechanisms
- **High-DPI Mouse**: Windows Raw Input (`WM_INPUT` / `GetRawInputData`) for sub-pixel precision cursor updates.
- **Low-Latency Gamepad**: Polling loop for XInput / DirectInput / DualSense gamepads with rumble and analog trigger feedback.
- **Zero-Allocation Packet Emission**: Format and encrypt binary input packets (`ControllerStatePacket`, `MouseMovePacket`) directly into UDP buffers.

## Acceptance Criteria
- [ ] Consistent 1000Hz polling rate measured without timing jitter on high-resolution timer (`QueryPerformanceCounter`).
- [ ] End-to-end input latency < 1.0ms client overhead.
"@
    },
    @{
        title = "[FEAT] Dynamic RTCP Bitrate Adaptation & Predictive Congestion Control"
        milestone = $MS3
        labels = "area: protocol-rtp,area: network-io,type: feature,priority: high"
        body = @"
## Architectural Overview
Implement real-time network congestion control and dynamic bitrate scaling based on RTCP feedback reports.

## Subsystem Details
- **Loss Statistics Feedback**: Send periodic `LossStatsPayload` feedback packets reporting lost vs FEC-recovered packets.
- **RTT Measurement**: Measure network round-trip time via ping/pong control packets.
- **Predictive Bitrate Adjuster**: Adjust video bitrate via RTSP `ANNOUNCE` before packet buffer bloat causes frame drops.
- **IDR Frame Request**: Automatically request Instantaneous Decoder Refresh (IDR) frames upon unrecoverable multi-packet loss.

## Acceptance Criteria
- [ ] Stream smoothly recovers from sudden bandwidth drops without freeze or disconnect.
- [ ] Unit tests covering loss report math and IDR trigger logic.
"@
    },
    @{
        title = "[FEAT] Continuous End-to-End Performance Benchmarking & Latency Telemetry Harness"
        milestone = $MS3
        labels = "type: benchmark,area: documentation,priority: medium"
        body = @"
## Architectural Overview
Build an automated performance auditing harness using BenchmarkDotNet and native C++ high-resolution timers to enforce zero-allocation discipline and prevent latency regressions.

## Benchmark Matrices
- **FEC Reconstruction**: Measure execution time across packet shard matrices (10+2, 20+4, 40+8).
- **RTP Parsing**: Validate 0-byte allocation on 1,000,000 consecutive packet parse iterations.
- **SPSC Ring Buffer**: Measure throughput and latency under 100M push/pop operations.
- **Jitter Buffer**: Measure frame assembly and out-of-order reordering times.

## Acceptance Criteria
- [ ] Automated execution in `.github/workflows/benchmarks.yml`.
- [ ] Comprehensive latency and allocation benchmarks documented in `wiki/Benchmarking-and-Performance-Audit.md`.
"@
    }
)

Write-Host "Creating GitHub roadmap issues..." -ForegroundColor Cyan

foreach ($issue in $Issues) {
    gh issue create --title $issue.title --milestone $issue.milestone --label $issue.labels --body $issue.body --repo $Repo
}

Write-Host "All GitHub roadmap issues created successfully." -ForegroundColor Green
