# Moonshine Native Binary Protocol (MNBP v1) Specification

<!-- VERIFIED: 2026-08-21, via `ctest --test-dir build/release-avx2 -R test_moonshine_protocol` and `tools/dotnet_sdk/dotnet.exe test tests/Moonshine.Protocol.Tests` on Windows 11 Pro build 26200 -->

## 1. Overview and Design Principles

The **Moonshine Native Binary Protocol (MNBP v1)** is a high-performance, versioned, zero-allocation binary transport wire contract owned entirely by Moonshine. It establishes deterministic wire layouts for session control, media streaming, audio transmission, microphone backchannel, input injection, Quality of Service (QoS) feedback, telemetry, and authenticated remote host management.

### Architectural Classification
> [!IMPORTANT]
> MNBP v1 represents the **wire contract foundation** for the Moonshine ecosystem. It defines canonical message envelopes, endian serialization rules, struct boundaries, validation criteria, and error codes. Concrete network transport engines (QUIC/TCP control plane, UDP media plane, packetisation, and jitter scheduling) consume and produce these contracts across C++23 and .NET 9.

### Core Architectural Guarantees
1. **Strict Big-Endian Wire Encoding**: All multi-byte numeric fields are serialized in **Big-Endian** (Network Byte Order) through explicit field-by-field operations (`BinaryPrimitives` in C#, `std::byteswap` in C++23).
2. **Canonical 16-Byte UUID Representation**: UUIDs and cryptographic salt tokens are encoded as raw 16-byte big-endian buffers (`MoonshineUuid128`), preventing .NET mixed-endian `Guid` memory layout disparities across native and managed boundaries.
3. **Explicit Separation of Logical Structs and Wire Formats**: Network payloads are governed by canonical serialization functions rather than compiler struct padding assumptions.
4. **Zero Heap Allocation in Codec Hot Paths**: All packet header and payload codecs operate directly upon `ReadOnlySpan<byte>` and `Span<byte>` buffers without managed heap allocations.
5. **Codec Independence**: Media framing operates independently of specific video codecs (AV1, HEVC, H.264).
6. **No Legacy Dependencies**: The protocol replaces all legacy RTSP, RTP, RTCP, GameStream, and Sunshine binary framing formats with first-party Moonshine contracts.

---

## 2. Common Packet Envelope

Every datagram transmitted across control and media channels starts with a mandatory **32-byte global packet header**.

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                   Magic: 'M' 'S' 'H' 'N'                      | (0x4D53484E)
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Protocol Version        |         Message Type          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Payload Size                          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Sequence Number                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                       Session ID (64-bit)                     +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                     Timestamp Us (64-bit)                     +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Field Definitions

| Field | Type | Offset | Size | Description |
| --- | --- | ---: | ---: | --- |
| `Magic` | `uint32_t` | 0 | 4 | Protocol identifier: must equal `0x4D53484E` (`'MSHN'`). |
| `Version` | `uint16_t` | 4 | 2 | Protocol version: `0x0001` (Major: 1, Minor: 0). |
| `MessageType` | `uint16_t` | 6 | 2 | Distinct message family identifier. |
| `PayloadSize` | `uint32_t` | 8 | 4 | Size of the trailing payload bytes (excluding the 32-byte header). |
| `SequenceNumber` | `uint32_t` | 12 | 4 | Monotonically increasing message sequence number. |
| `SessionId` | `uint64_t` | 16 | 8 | 64-bit session token associated with the authenticated peer. |
| `TimestampUs` | `uint64_t` | 24 | 8 | Microsecond Unix epoch or relative monotonic stream timestamp. |

---

## 3. Message Family Taxonomy

| Message Family | Code Range | Description |
| --- | --- | --- |
| **Control & Session** | `0x0100` - `0x01FF` | Connection handshake, authentication, stream negotiation, teardown. |
| **Media Video** | `0x0200` - `0x02FF` | Codec-agnostic video frame and FEC parity shard transmission. |
| **Audio** | `0x0300` - `0x03FF` | Low-latency multi-channel audio stream transmission (Host to Client). |
| **Microphone** | `0x0400` - `0x04FF` | Low-latency audio backchannel (Client to Host). |
| **Feedback & QoS** | `0x0500` - `0x05FF` | Packet loss statistics, round-trip latency, jitter, IDR keyframe requests. |
| **Input Injection** | `0x0600` - `0x06FF` | High-frequency keyboard, mouse, and gamepad input state transmission. |
| **Telemetry** | `0x0700` - `0x07FF` | Latency breakdown reports, render statistics, and health metrics. |
| **Host Management** | `0x0800` - `0x08FF` | Authenticated remote host configuration queries and mutations. |

---

## 4. Message Payloads

### 4.1 Control & Session Payloads

#### `Hello` (`0x0101`, 32 bytes payload)
Sent by Client to initiate protocol version and capability handshake:
- `uint16_t client_version_major`
- `uint16_t client_version_minor`
- `uint32_t capabilities_mask`
- `uint64_t client_nonce`
- `uint8_t client_uuid[16]` (RFC 4122 Big-Endian 128-bit UUID)

#### `HelloResponse` (`0x0102`, 48 bytes payload)
Sent by Host in response to `Hello`:
- `uint16_t server_version_major`
- `uint16_t server_version_minor`
- `uint32_t negotiated_capabilities`
- `uint64_t assigned_session_id`
- `uint64_t server_nonce`
- `uint8_t challenge_salt[16]`
- `uint32_t session_lease_seconds`
- `uint32_t reserved`

#### `SessionSetup` (`0x0103`, 40 bytes payload)
Negotiates video, audio, and network stream parameters:
- `uint32_t video_width`
- `uint32_t video_height`
- `uint32_t video_fps`
- `uint32_t video_bitrate_kbps`
- `uint8_t video_codec` (1: AV1, 2: HEVC, 3: H.264)
- `uint8_t video_color_format` (1: NV12, 2: P010_HDR10)
- `uint8_t audio_channels` (2: Stereo, 6: 5.1, 8: 7.1)
- `uint8_t audio_codec` (1: Opus, 2: PCM16)
- `uint32_t audio_sample_rate` (e.g. 48000)
- `uint32_t audio_bitrate_kbps`
- `uint16_t client_udp_video_port`
- `uint16_t client_udp_audio_port`
- `uint16_t client_udp_feedback_port`
- `uint16_t reserved`
- `uint32_t mtu_payload_size`

#### `SessionSetupResponse` (`0x0104`, 32 bytes payload)
- `uint32_t status_code` (0: Success, non-zero: ErrorCode)
- `uint32_t video_stream_id`
- `uint32_t audio_stream_id`
- `uint32_t feedback_stream_id`
- `uint16_t host_udp_video_port`
- `uint16_t host_udp_audio_port`
- `uint16_t host_udp_feedback_port`
- `uint16_t host_udp_input_port`
- `uint32_t negotiated_mtu`
- `uint32_t reserved`

---

### 4.2 Media Stream Framing

#### `VideoPacket` (`0x0201`, 32 bytes header + variable bitstream payload)
Codec-agnostic video transmission framing:
- `uint32_t stream_id`
- `uint64_t frame_index` (64-bit monotonic frame sequence)
- `uint32_t packet_index` (0-indexed packet within frame)
- `uint32_t total_packets` (Total packet count in frame)
- `uint32_t fec_block_index` (FEC shard group index)
- `uint16_t payload_size` (Size of bitstream slice following header)
- `uint8_t packet_type` (0: Data Shard, 1: FEC Parity Shard)
- `uint8_t flags` (Bit 0: Keyframe, Bit 1: FrameStart, Bit 2: FrameEnd, Bit 3: HDR10 Present)
- `uint32_t reserved`

---

### 4.3 Audio & Microphone Framing

#### `AudioPacket` (`0x0301`, 24 bytes header + compressed audio payload)
- `uint32_t stream_id`
- `uint64_t sample_index`
- `uint32_t sample_rate`
- `uint16_t frame_duration_us`
- `uint16_t payload_size`
- `uint8_t channels` (2, 6, 8)
- `uint8_t codec` (1: Opus, 2: PCM16)
- `uint16_t reserved`

#### `MicPacket` (`0x0401`, 20 bytes header + compressed audio payload)
- `uint32_t stream_id`
- `uint64_t sample_index`
- `uint16_t payload_size`
- `uint8_t channels` (1: Mono, 2: Stereo)
- `uint8_t codec` (1: Opus, 2: PCM16)
- `uint32_t sample_rate`

---

### 4.4 Feedback & Quality of Service Payloads

#### `FeedbackLossStats` (`0x0501`, 40 bytes payload)
- `uint32_t stream_id`
- `uint64_t last_received_frame_index` (Highest monotonic frame index received / processed by client)
- `uint32_t packets_received` (Cumulative packets received for active stream)
- `uint32_t packets_lost` (Cumulative packets lost)
- `uint32_t packets_recovered_fec` (Cumulative packets recovered via FEC)
- `uint32_t round_trip_time_us` (Measured RTT in microseconds)
- `uint32_t jitter_us` (Smoothed jitter in microseconds)
- `uint32_t estimated_bandwidth_kbps` (Estimated throughput in Kbps)
- `uint32_t receive_queue_depth` (Current client jitter/decode queue depth in frames)

#### `IdrRequest` (`0x0502`, 16 bytes payload)
- `uint32_t stream_id`
- `uint64_t last_valid_frame_index`
- `uint32_t reason_code` (1: UnrecoverableLoss, 2: SequenceGap, 3: DecoderError)

#### 4.4.1 Feedback Ordering & Monotonic Stream Horizon Invariants

1. **Monotonic Frame Indices**: Frame indices (`frame_index`) in MNBP video packets are strictly monotonically increasing per stream (`0, 1, 2, ...`). Even if individual video packet slices arrive out-of-order over UDP or are reconstructed after a delay via FEC, the client's `last_received_frame_index` represents the **stream horizon** (the highest frame index observed/processed).
2. **Stale Feedback Filtering**: Because UDP feedback datagrams may experience reordering or transit delay, the host congestion controller enforces monotonic progression of `last_received_frame_index`. Any feedback datagram with `last_received_frame_index < last_processed_frame_index` is treated as a delayed/stale datagram and discarded without polluting moving averages or delta counters.
3. **Session Re-Anchoring**: When `stream_id` changes, the baseline is reset, establishing a new monotonic horizon for the incoming stream.


---

### 4.5 Input Injection Payloads

#### `KeyboardInput` (`0x0601`, 12 bytes payload)
- `uint16_t key_code` (Win32 Virtual-Key code)
- `uint16_t scan_code`
- `uint8_t is_down` (1: Pressed, 0: Released)
- `uint8_t modifiers` (Bit 0: Shift, Bit 1: Ctrl, Bit 2: Alt, Bit 3: Meta)
- `uint16_t reserved`
- `uint32_t timestamp_offset_us`

#### `MouseInput` (`0x0602`, 20 bytes payload)
- `int32_t x`
- `int32_t y`
- `int16_t wheel_delta_y`
- `int16_t wheel_delta_x`
- `uint16_t button_flags` (Bit 0: Left, Bit 1: Right, Bit 2: Middle, Bit 3: X1, Bit 4: X2)
- `uint8_t is_absolute` (1: Absolute desktop coords, 0: Relative delta)
- `uint8_t reserved`
- `uint32_t timestamp_offset_us`

#### `GamepadInput` (`0x0603`, 24 bytes payload)
- `uint8_t gamepad_index` (0..3)
- `uint8_t reserved`
- `uint16_t button_mask` (Standard XInput / Moonshine bitmask)
- `uint8_t left_trigger` (0..255)
- `uint8_t right_trigger` (0..255)
- `int16_t thumb_lx` (-32768..32767)
- `int16_t thumb_ly`
- `int16_t thumb_rx`
- `int16_t thumb_ry`
- `uint16_t motor_left`
- `uint16_t motor_right`
- `uint32_t timestamp_offset_us`
- `uint16_t reserved2`

---

### 4.6 Telemetry Payloads

#### `TelemetryReport` (`0x0701`, 32 bytes payload)
- `uint32_t encode_latency_us`
- `uint32_t decode_latency_us`
- `uint32_t render_latency_us`
- `uint32_t network_latency_us`
- `uint32_t frames_rendered`
- `uint32_t frames_dropped`
- `uint32_t fec_recovered_frames`
- `uint32_t reserved`

---

### 4.7 Host Management & Remote Configuration Payloads

#### `GetHostCapabilities` (`0x0801`, 4 bytes payload)
- `uint32_t query_mask`

#### `HostCapabilitiesResponse` (`0x0802`, 32 bytes payload)
- `uint32_t supported_video_codecs` (Bitmask: AV1, HEVC, H264)
- `uint32_t supported_audio_codecs` (Bitmask: Opus, PCM16)
- `uint32_t max_encode_width`
- `uint32_t max_encode_height`
- `uint32_t max_encode_fps`
- `uint8_t supports_hdr10` (0/1)
- `uint8_t supports_virtual_audio` (0/1)
- `uint8_t supports_mic_backchannel` (0/1)
- `uint8_t reserved`
- `uint32_t max_bitrate_kbps`
- `uint32_t reserved2`

#### `GetHostConfiguration` (`0x0803`, 4 bytes payload)
- `uint32_t config_scope`

#### `HostConfigurationResponse` (`0x0804`, 48 bytes payload) & `SetHostConfiguration` (`0x0805`, 48 bytes payload)
- `uint32_t config_version`
- `uint32_t display_width`
- `uint32_t display_height`
- `uint32_t refresh_rate_hz`
- `uint32_t target_bitrate_kbps`
- `uint32_t max_bitrate_kbps`
- `uint8_t preferred_codec` (1: AV1, 2: HEVC, 3: H264)
- `uint8_t hdr10_enabled` (0/1)
- `uint8_t audio_channels` (2, 6, 8)
- `uint8_t audio_quality_mode`
- `uint32_t audio_bitrate_kbps`
- `uint16_t input_polling_rate_hz` (e.g. 1000)
- `uint8_t mic_passthrough_enabled` (0/1)
- `uint8_t virtual_audio_driver_enabled` (0/1)
- `uint32_t reserved1`
- `uint32_t reserved2`
- `uint32_t reserved3`

#### `SetHostConfigurationResponse` (`0x0806`, 8 bytes payload)
- `uint32_t status_code` (0: Success, non-zero: ErrorCode)
- `uint32_t applied_config_version`

#### `ConfigurationChanged` (`0x0807`, 8 bytes payload)
- `uint32_t new_config_version`
- `uint32_t change_reason_flags`

---

## 5. Error Codes & Validation Rules

| Error Code | Numeric Value | Description |
| --- | ---: | --- |
| `Success` | 0 | Operation completed successfully. |
| `InvalidMagic` | 1 | Packet magic is not `0x4D53484E`. |
| `UnsupportedVersion` | 2 | Protocol version mismatch. |
| `MalformedHeader` | 3 | Header size less than 32 bytes or invalid field value. |
| `BufferTooSmall` | 4 | Destination buffer insufficient for serialization. |
| `PayloadTruncated` | 5 | Available bytes less than declared `PayloadSize`. |
| `InvalidSession` | 6 | Session ID does not match active session token. |
| `AuthenticationFailed` | 7 | Nonce or challenge HMAC mismatch. |
| `StreamNotFound` | 8 | Referenced stream ID does not exist in session. |
| `DuplicateSequence` | 9 | Sequence number already processed. |
| `StaleTimestamp` | 10 | Packet timestamp is older than discard window. |
| `UnsupportedCodec` | 11 | Requested video or audio codec unsupported by backend. |
| `UnauthorizedConfiguration` | 12 | Remote peer lacks authorization to modify host settings. |
| `InvalidConfigurationParameter` | 13 | Requested setting outside acceptable hardware boundaries. |
