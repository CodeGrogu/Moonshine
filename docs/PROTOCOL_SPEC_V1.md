# Moonshine Native Binary Protocol (MNBP v1.2) Specification

<!-- REVISION: 1.2 — supersedes MNBP v1.1. Security-critical revision: fixes ClientAuth payload size (was misdeclared), replaces transmitted AEAD nonce prefixes with symmetric HKDF derivation, adds stream_id separation to the AEAD nonce construction (eliminating cross-stream nonce reuse), binds the host ephemeral key into the authentication transcript, corrects the proof pseudocode, clarifies TCP framing post-handshake, and defines the incompatible-version HelloResponse layout.
VERIFIED: this revision is verified when `ctest -R test_moonshine_protocol` and `dotnet test tests/Moonshine.Protocol.Tests` pass against revision hash <COMMIT_SHA> on Windows 11 Pro. Verification claims must cite this revision hash. -->

## 1. Overview and Design Principles

The **Moonshine Native Binary Protocol (MNBP v1.2)** is a high-performance, versioned, zero-allocation binary transport wire contract owned entirely by Moonshine. It establishes deterministic wire layouts for session control, **authenticated key exchange**, media streaming, audio transmission, microphone backchannel, input injection, QoS feedback, telemetry, and authenticated remote host management.

### Architectural Classification
MNBP v1.2 defines the **wire contract** for the Moonshine ecosystem. Concrete network transport engines (QUIC/TCP control plane, UDP media plane, packetisation, jitter scheduling) consume and produce these contracts across C++23 and .NET 9.

### Core Architectural Guarantees
1. **Strict Big-Endian Wire Encoding**: All multi-byte numeric fields are serialized in Big-Endian (Network Byte Order) through explicit field-by-field operations (`BinaryPrimitives` in C#, `std::byteswap` in C++23).
2. **Canonical 16-Byte UUID Representation**: UUIDs and cryptographic salts are encoded as raw 16-byte big-endian buffers (`MoonshineUuid128`).
3. **Explicit Separation of Logical Structs and Wire Formats**: Network payloads are governed by canonical serialization functions, never compiler struct padding. **All structs are packed; there is no implicit alignment.**
4. **Zero Heap Allocation in Codec Hot Paths**: Post-handshake packet codecs operate directly on `ReadOnlySpan<byte>` / `Span<byte>`.
5. **Codec Independence**: Media framing is independent of video codec (AV1, HEVC, H.264).
6. **No Legacy Dependencies**: No RTSP, RTP, RTCP, GameStream, or Sunshine framing.
7. **Cryptographically Authenticated and Protected**: Every session is mutually authenticated before any capability, configuration, media, or input message is processed, and every post-handshake datagram is AEAD-protected. *(New in v1.1)*
8. **Fail-Closed Validation**: Any validation failure on a received datagram causes the datagram to be discarded. Repeated failures above the thresholds in §11 cause session teardown. No error path allocates unbounded memory. *(New in v1.1)*

### Security Model Summary *(New in v1.1)*
- Authentication: **HMAC-SHA-256 challenge–response over a session transcript**, derived from a host-issued salt and a shared secret (host PIN or pairing token) via **Argon2id** (host side) / HKDF.
- Key agreement: **X25519 ephemeral Diffie–Hellman**, performed during the handshake, producing symmetric keys used for **AEAD (ChaCha20-Poly1305)** protection of all post-handshake traffic.
- **Auth-before-anything**: No message other than `Hello`, `HelloResponse`, `ClientAuth`, and `ServerConfirm` is accepted before the session reaches the `Established` state (§9).
- **Anti-reflection/amplification**: The host sends no media, and no high-bandwidth responses of any kind, until authentication completes and the client's UDP endpoints are confirmed by a keyed `MediaEndpointConfirm` packet (§7.6).
- **Downgrade protection**: negotiated version and capabilities are bound into the authenticated transcript.
- Session identity is never used as a bearer credential: the AEAD key, not `SessionId`, is the proof of session membership.

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
|                                                               +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                       Timestamp Us (64-bit)                   +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Field Definitions

| Field | Type | Offset | Size | Description |
| --- | --- | ---: | ---: | --- |
| `Magic` | `uint32_t` | 0 | 4 | Must equal `0x4D53484E`. Mismatch → `InvalidMagic`, datagram dropped. |
| `Version` | `uint16_t` | 4 | 2 | `0x0102` (Major: 1, Minor: 2). Mismatch → `UnsupportedVersion`, datagram dropped. |
| `MessageType` | `uint16_t` | 6 | 2 | Message family + message identifier (§3). |
| `PayloadSize` | `uint32_t` | 8 | 4 | Size of trailing payload **including the 16-byte AEAD tag when the message is protected** (§8). Must not exceed `negotiated_mtu - 32`. |
| `SequenceNumber` | `uint32_t` | 12 | 4 | Monotonically increasing per **direction per stream** (`stream_id` 0 for control). Wraparound is handled by the continuity rule in §8.4. |
| `SessionId` | `uint64_t` | 16 | 8 | 64-bit random session token assigned by the host. **Not a security credential**; routing/multiplexing hint only. |
| `TimestampUs` | `uint64_t` | 24 | 8 | **Relative monotonic** microseconds since session anchor time (`SessionSetupResponse` receive on the client, its send on the host). Unix-epoch timestamps are **invalid** in this field. |

### Validation Rules (Header) *(New in v1.1)*
1. `PayloadSize` for fixed-size message types must equal exactly the specified size (plus the 16-byte AEAD tag for protected messages); otherwise `MalformedHeader`, datagram dropped.
2. `PayloadSize` for variable-size message types must not exceed the type's documented maximum; otherwise `MalformedHeader`.
3. `PayloadSize` must satisfy `32 + PayloadSize <= datagram_length` and `PayloadSize <= negotiated_mtu - 32`; otherwise `PayloadTruncated` / `MalformedHeader`.
4. All `reserved` fields must be **zero on send**. Receivers **must validate** reserved fields are zero and drop the datagram with `MalformedHeader` otherwise.
5. `SessionId` must match the active session for all post-`HelloResponse` traffic. Pre-auth: drop **silently** (§11 anti-amplification — no error response is sent). Post-auth: the peer MAY be notified with `InvalidSession` on the appropriate plane.

---

## 3. Message Family Taxonomy

| Message Family | Code Range | Pre-Auth Permitted | Description |
| --- | --- | --- | --- |
| **Control & Session** | `0x0100` - `0x01FF` | Only `Hello`, `HelloResponse`, `ClientAuth` (§4.1) | Handshake, **authenticated key exchange**, stream negotiation, teardown. |
| **Media Video** | `0x0200` - `0x02FF` | No | Codec-agnostic video frame and FEC parity transmission. |
| **Audio** | `0x0300` - `0x03FF` | No | Low-latency audio (Host → Client). |
| **Microphone** | `0x0400` - `0x04FF` | No | Low-latency audio backchannel (Client → Host). |
| **Feedback & QoS** | `0x0500` - `0x05FF` | No | Loss stats, RTT, jitter, IDR requests. |
| **Input Injection** | `0x0600` - `0x06FF` | No | Keyboard, mouse, gamepad input state. |
| **Telemetry** | `0x0700` - `0x07FF` | No | Latency breakdowns, health metrics. |
| **Host Management** | `0x0800` - `0x08FF` | No | Authenticated host configuration queries and mutations. |

Receiving any message outside its permitted state is a protocol violation: pre-auth → drop datagram silently (control plane) or terminate TCP control connection; post-auth → session teardown with `InvalidSession`.

---

## 4. Message Payloads

### 4.1 Control & Session Payloads

#### `Hello` (`0x0101`, 32 bytes payload) — Client → Host, pre-auth, plaintext
- `uint16_t client_version_major`
- `uint16_t client_version_minor`
- `uint32_t capabilities_mask`
- `uint64_t client_nonce` — CSPRNG-generated fresh per handshake attempt, MUST NOT repeat within the host's replay window (host tracks recent nonces; duplicate → `DuplicateSequence`, drop). **Clients MUST regenerate the nonce on every handshake attempt, including retries after `RateLimited`.**
- `uint8_t client_uuid[16]`

#### `HelloResponse` (`0x0102`, 80 bytes payload) — Host → Client, pre-auth, plaintext
- `uint16_t server_version_major`
- `uint16_t server_version_minor`
- `uint32_t negotiated_capabilities`
- `uint64_t assigned_session_id` — **MUST be generated by a CSPRNG**, uniform random 64-bit value; rejected values: 0 and 0xFFFFFFFFFFFFFFFF.
- `uint64_t server_nonce` — CSPRNG-generated.
- `uint8_t challenge_salt[16]` — CSPRNG-generated per session.
- `uint32_t argon2_memory_kib` — host-chosen Argon2id memory cost (e.g., 65536).
- `uint32_t argon2_iterations` — host-chosen (e.g., 3).
- `uint32_t argon2_parallelism` — host-chosen (e.g., 1).
- `uint32_t session_lease_seconds`
- `uint32_t reserved`

**Version selection rule**: The host sets `server_version_major/minor` to **the highest version the host supports** (not an echo of the client's value). The negotiated version is `min(client, server)` per component, computed independently by both parties from the transcript inputs — it is therefore fully determined by `Hello` + `HelloResponse` bytes and is transcript-bound (§4.1.1) without needing to appear as a separate wire field. If no compatible version exists (i.e., `min` yields a version the host does not implement, or major versions differ), the host responds with **a well-formed `HelloResponse` in which `server_version_major = server_version_minor = 0`** and all remaining fields (`negotiated_capabilities`, `assigned_session_id`, `server_nonce`, `challenge_salt`, Argon2 parameters, `session_lease_seconds`) are **zero**. The client, upon receiving `server_version == 0x0000`, MUST NOT validate the remaining fields (the all-zero layout is the defined representation of "incompatible") and transitions to `Closed` without sending `ClientAuth`. **Downgrade protection**: the full negotiated version tuple is a deterministic function of the transcript (§4.1.1); an on-path attacker cannot force an older version without breaking the HMAC proof.

#### `ClientAuth` (`0x0107`, **84 bytes payload**) — Client → Host, pre-auth, plaintext, begins protected handshake *(New in v1.1; size corrected in v1.2)*
- `uint8_t client_ephemeral_x25519[32]` — CSPRNG ephemeral public key, single use per session.
- `uint8_t client_proof[32]` — HMAC-SHA-256 authentication tag, computed as specified in §4.1.1.
- `uint8_t client_uuid_confirmation[16]` — must equal `Hello.client_uuid`; mismatch → `AuthenticationFailed`.
- `uint32_t reserved`

*(Fields total 32 + 32 + 16 + 4 = 84 bytes.)*

#### `ServerConfirm` (`0x0108`, 80 bytes payload) — Host → Client, sent **protected with the newly derived key** (§4.1.2) *(New in v1.1)*
- `uint8_t host_ephemeral_x25519[32]`
- `uint8_t host_proof[32]` — HMAC tag per §4.1.1 (host role).
- `uint8_t aead_nonce_prefix[8]` — **informational copy of the host's AEAD nonce prefix** (derived per §4.1.2); the client MAY verify it matches its own derivation and MUST NOT rely on it for decryption.
- `uint32_t handshake_result` — 0 = Success, `AuthenticationFailed` (7), `RateLimited` (14).
- `uint32_t reserved`

Receipt of `ServerConfirm` with `handshake_result == 0` and a valid `host_proof` transitions the session to `Established`.

*Note on failure reporting*: when `handshake_result != 0`, the host cannot have derived shared keys with the (possibly unknown) client, so the failure `ServerConfirm` is sent **as a plaintext fixed-size message** (`PayloadSize = 80`, no AEAD tag). Clients MUST accept both forms: if the payload fails AEAD verification AND parses as a valid plaintext failure result, the handshake is failed per `handshake_result`. Success results MUST be AEAD-protected; a plaintext `handshake_result == 0` is invalid and discarded.

#### 4.1.1 Authentication and Key Schedule *(New in v1.1; host ephemeral binding and corrected proof labels in v1.2)*

**Secret material.** The host and client share a pre-provisioned secret: either a user-typed host PIN (6–32 Unicode code points) or a 256-bit pairing token from an out-of-band pairing flow. Implementations MUST NOT hardcode or derive-from-public-data secrets.

**Password hardening (host side, for PINs).**
```
psk = Argon2id(password = secret, salt = challenge_salt,
               memory = argon2_memory_kib, iterations = argon2_iterations,
               parallelism = argon2_parallelism, tag_length = 32)
```
For 256-bit pairing tokens, `psk = HKDF-SHA256(ikm = token, salt = challenge_salt, info = "MNBP v1.2 psk", L = 32)`.

**Authentication transcript.** Define `T` as the exact on-wire bytes, in this order:
```
T = "MNBP-AUTH-v1"                        (12 ASCII bytes)
  || Hello (32 bytes, as sent)
  || HelloResponse (80 bytes, as sent)
  || ClientAuth.client_ephemeral_x25519   (32 bytes)
  || ServerConfirm.host_ephemeral_x25519  (32 bytes)   ← host proofs only; see below
```

Two transcript prefixes are defined:

- `T_client = T[0 .. 12+32+80+32-1]` — everything through the client ephemeral key (156 bytes).
- `T_host = T[0 .. 12+32+80+32+32-1]` — everything through the host ephemeral key (188 bytes).

This binds the negotiated version, capabilities, nonces, salt, Argon2 parameters, session id, **and both ephemeral DH shares** into the proofs — providing downgrade protection, replay protection, and mutual key confirmation in one structure.

**Proofs.**
```
client_proof = HMAC-SHA-256(key = psk, data = T_client || "client")
host_proof   = HMAC-SHA-256(key = psk, data = T_host   || "host")
```
where `"client"` and `"host"` are the ASCII labels (6 and 4 bytes respectively). The labels are domain separators preventing proof substitution between roles. `client_proof` covers only `T_client` because the client sends its proof before receiving the host ephemeral key; `host_proof` covers the client's ephemeral key *and* the host's, so a client that verifies `host_proof` has explicit confirmation that the key it derived via X25519 is bound to an authenticated peer. Comparison MUST be constant-time. Failure → `AuthenticationFailed`, session torn down, and the failure counts against the host's brute-force rate limiter (§11).

**Key schedule.**
```
shared_secret = X25519(client_ephemeral_private, host_ephemeral_public)   // client side
shared_secret = X25519(host_ephemeral_private,   client_ephemeral_public) // host side
handshake_key = HKDF-SHA256(ikm   = shared_secret,
                            salt  = SHA-256(T_client),
                            info  = "MNBP v1.2 handshake",
                            L     = 80)
```

The 80-byte output splits into four 20-byte segments, each used as follows:

- `k_client_to_host  = handshake_key[ 0..31]` — ChaCha20-Poly1305 key, client → host.
- `k_host_to_client  = handshake_key[32..63]` — ChaCha20-Poly1305 key, host → client.
- `p_client_to_host  = handshake_key[64..71]` — 8-byte AEAD nonce prefix, client → host.
- `p_host_to_client  = handshake_key[72..79]` — 8-byte AEAD nonce prefix, host → client.

Both nonce prefixes are derived symmetrically from the same HKDF output; **no nonce prefix is ever transmitted for security-critical use** (the copy in `ServerConfirm.aead_nonce_prefix` is a redundant convenience field, see §4.1). This resolves the circularity of v1.1, where the client needed the prefix carried inside the encrypted `ServerConfirm` in order to decrypt it.

**AEAD nonce construction (per packet).**
```
nonce[0..3]   = direction nonce prefix, first 4 bytes (p_client_to_host or p_host_to_client)
nonce[4..7]   = stream_id, encoded big-endian (0 for control plane)
nonce[8..11]  = wire SequenceNumber, encoded big-endian (32-bit)
```
**Nonces MUST NEVER repeat under the same key.** Because `SequenceNumber` is scoped per direction per stream (§2), folding `stream_id` into bytes 4–7 is mandatory: without it, video stream seq 5 and audio stream seq 5 would produce identical nonces under the same direction key, which is fatal for ChaCha20-Poly1305. The 4-byte prefix provides 2³² distinct prefixes per direction; combined with the 2³² sequence space and per-session fresh keys, collision probability is negligible, and the epoch mechanism of §8.4 does not enter the nonce (per-stream sequence lifetimes are bounded well below 2³² by the session lease, §10.2 — implementations MUST NOT send more than 2³¹ packets on a single stream within one session).

**Ephemeral key hygiene.** X25519 keypairs are generated fresh per session from a CSPRNG and zeroized immediately after key derivation. Implementations MUST reject `ClientAuth` with an all-zero or low-order point (`X25519` all-zero output → `AuthenticationFailed`).

#### `SessionSetup` (`0x0103`, 40 bytes payload) — post-auth, AEAD-protected
As in v1.0, **with mandatory validation bounds** (§4.1.3):
- `uint32_t video_width` — 1..`max_encode_width` from `HostCapabilitiesResponse`
- `uint32_t video_height` — 1..`max_encode_height`
- `uint32_t video_fps` — 1..1000
- `uint32_t video_bitrate_kbps` — 1..`max_bitrate_kbps`
- `uint8_t video_codec` (1: AV1, 2: HEVC, 3: H.264)
- `uint8_t video_color_format` (1: NV12, 2: P010_HDR10) — P010 only when `supports_hdr10`
- `uint8_t audio_channels` (2, 6, 8)
- `uint8_t audio_codec` (1: Opus, 2: PCM16)
- `uint32_t audio_sample_rate` — 8000..192000
- `uint32_t audio_bitrate_kbps` — 16..1024
- `uint16_t client_udp_video_port` — 1024..65535, or 0 to disable
- `uint16_t client_udp_audio_port` — 1024..65535, or 0
- `uint16_t client_udp_feedback_port` — 1024..65535, or 0
- `uint16_t reserved` — must be 0
- `uint32_t mtu_payload_size` — 576..9000

Any violation → `SessionSetupResponse` with `InvalidConfigurationParameter`; session remains in `Established` but streams are not started.

#### `SessionSetupResponse` (`0x0104`, 32 bytes payload) — post-auth, AEAD-protected
- `uint32_t status_code` (0: Success, non-zero: ErrorCode)
- `uint32_t video_stream_id` — nonzero on success
- `uint32_t audio_stream_id`
- `uint32_t feedback_stream_id`
- `uint16_t host_udp_video_port` — 1024..65535, or 0
- `uint16_t host_udp_audio_port`
- `uint16_t host_udp_feedback_port`
- `uint16_t host_udp_input_port`
- `uint32_t negotiated_mtu` — ≤ client's `mtu_payload_size`
- `uint32_t reserved`

#### 4.1.2 Note on Handshake Encryption *(clarified in v1.2)*
`ClientAuth` and its payload transit in the clear (they contain no secret: the X25519 public key is public, and the HMAC proof is not replayable thanks to transcript binding and nonce tracking). `ServerConfirm` is the first AEAD-protected datagram on success — possible because both nonce prefixes are HKDF-derived (§4.1.1), so the client can decrypt it without any prior in-band prefix exchange — proving to the client that the host possesses `psk` before the client treats the channel as trusted. The first two handshake round trips occur over the TCP control connection; pre-auth datagrams are additionally rate-limited (§11).

### 4.2 Media Stream Framing — post-auth, AEAD-protected, UDP media plane

#### `VideoPacket` (`0x0201`, 32 bytes header + variable bitstream payload)
- `uint32_t stream_id`
- `uint64_t frame_index` — strictly monotonically increasing per stream, starting at 0
- `uint32_t packet_index` — 0-indexed; **MUST satisfy `packet_index < total_packets`**; violation → drop datagram
- `uint32_t total_packets` — **MUST satisfy `total_packets ≤ max_packets_per_frame`** where `max_packets_per_frame = ceil(max_frame_bytes / (negotiated_mtu - 32 - 32))` and `max_frame_bytes` is negotiated (default 4 MiB); violation → drop datagram and count toward `MalformedHeader` budget
- `uint32_t fec_block_index` — `< ceil(total_packets / fec_group_size)`
- `uint16_t payload_size` — **MUST be ≤ `negotiated_mtu - 32 - 32`** and equal the actual trailing AEAD-protected slice length; violation → `PayloadTruncated`
- `uint8_t packet_type` (0: Data Shard, 1: FEC Parity Shard)
- `uint8_t flags` (Bit 0: Keyframe, Bit 1: FrameStart, Bit 2: FrameEnd, Bit 3: HDR10 Present; **bits 4–7 reserved, must be 0**)
- `uint32_t reserved`

**Reassembly invariant**: decoders MUST NOT preallocate reassembly buffers from `total_packets`. Buffers are allocated against the negotiated `max_frame_bytes` bound only. A frame is assembled only when all `total_packets` shards with consistent values (`total_packets` identical across the frame; `payload_size` sums to ≤ `max_frame_bytes`) are present; inconsistent values → discard the entire frame's buffered shards and count a decode error.

### 4.3 Audio & Microphone Framing — post-auth, AEAD-protected

#### `AudioPacket` (`0x0301`, 24 bytes header + compressed audio payload)
- `uint32_t stream_id`
- `uint64_t sample_index`
- `uint32_t sample_rate` — must match negotiated value
- `uint16_t frame_duration_us` — 1000..20000
- `uint16_t payload_size` — ≤ `negotiated_mtu - 32 - 24`
- `uint8_t channels` — must match negotiated value
- `uint8_t codec` — must match negotiated value
- `uint16_t reserved`

#### `MicPacket` (`0x0401`, 20 bytes header + compressed audio payload) — Client → Host only
- `uint32_t stream_id`
- `uint64_t sample_index`
- `uint16_t payload_size` — ≤ `negotiated_mtu - 32 - 20`
- `uint8_t channels` (1: Mono, 2: Stereo)
- `uint8_t codec` (1: Opus, 2: PCM16)
- `uint32_t sample_rate` — 8000..192000

Mic backchannel is active only when `supports_mic_backchannel == 1` **and** the authenticated peer is the client role **and** mic was enabled in `SessionSetup` capability negotiation. Hosts MUST drop `MicPacket`s from clients without mic entitlement (`UnauthorizedConfiguration`).

### 4.4 Feedback & Quality of Service Payloads — post-auth, AEAD-protected

#### `FeedbackLossStats` (`0x0501`, 40 bytes payload)
As in v1.0, with these clarifications:
- `uint32_t stream_id`
- `uint64_t last_received_frame_index`
- `uint32_t packets_received`
- `uint32_t packets_lost`
- `uint32_t packets_recovered_fec`
- `uint32_t round_trip_time_us`
- `uint32_t jitter_us`
- `uint32_t estimated_bandwidth_kbps` — ≤ 10,000,000, else `MalformedHeader` (drop)
- `uint32_t receive_queue_depth`

#### `IdrRequest` (`0x0502`, 16 bytes payload)
- `uint32_t stream_id`
- `uint64_t last_valid_frame_index`
- `uint32_t reason_code` (1: UnrecoverableLoss, 2: SequenceGap, 3: DecoderError)

**IDR rate limit *(New in v1.1)***: Hosts MUST service at most **one IDR request per `idr_min_interval_us`** (default: 3 × current measured RTT, floor 10 ms, ceiling 500 ms) per stream. Additional requests within the window are counted; **exceeding 30 IDR requests in any 10-second window is a protocol violation → session teardown with `UnauthorizedConfiguration`**. This prevents keyframe-request DoS amplification of encoder load and uplink bandwidth.

#### 4.4.1 Feedback Ordering & Monotonic Stream Horizon Invariants
Unchanged from v1.0 (strictly monotonic frame indices; stale feedback with `last_received_frame_index < last_processed_frame_index` discarded; re-anchoring on `stream_id` change), with one addition:
4. **Stream horizon cannot regress**: a feedback datagram with `last_received_frame_index` more than `2^32` above the current horizon is malformed (drop); this bounds the impact of AEAD nonce-adjacent corruption bugs and hostile feedback.

### 4.5 Input Injection Payloads — post-auth, AEAD-protected, Client → Host only

**Input is refused while the host workstation is locked or on the secure desktop (UAC)** unless an explicitly configured `allow_input_when_locked` policy permits it, and it is always refused on the UAC secure desktop. *(New in v1.1)*

#### `KeyboardInput` (`0x0601`, 12 bytes payload)
- `uint16_t key_code` — Win32 VK code; host MUST reject injection of key sequences that trigger OS-level secure attention (see §4.5.1)
- `uint16_t scan_code`
- `uint8_t is_down` (1/0; other values → drop)
- `uint8_t modifiers` (Bit 0: Shift, 1: Ctrl, 2: Alt, 3: Meta; bits 4–7 must be 0)
- `uint16_t reserved`
- `uint32_t timestamp_offset_us`

#### `MouseInput` (`0x0602`, 20 bytes payload)
- `int32_t x` — for `is_absolute = 1`: 0..`desktop_width - 1` for the captured display (out-of-range values are clamped, not rejected, to tolerate display changes mid-session; multi-monitor spans use the virtual desktop bounds)
- `int32_t y`
- `int16_t wheel_delta_y`
- `int16_t wheel_delta_x`
- `uint16_t button_flags` (Bits 0–4: Left, Right, Middle, X1, X2; bits 5–15 must be 0)
- `uint8_t is_absolute`
- `uint8_t reserved`
- `uint32_t timestamp_offset_us`

#### `GamepadInput` (`0x0603`, 24 bytes payload)
As in v1.0:
- `uint8_t gamepad_index` (0..3; >3 → drop)
- `uint8_t reserved`
- `uint16_t button_mask`
- `uint8_t left_trigger`, `right_trigger`
- `int16_t thumb_lx`, `thumb_ly`, `thumb_rx`, `thumb_ry`
- `uint16_t motor_left`, `motor_right`
- `uint32_t timestamp_offset_us`
- `uint16_t reserved2` — must be 0

#### 4.5.1 Input Injection Safety Rules *(New in v1.1)*
1. Hosts MUST filter secure-attention sequences (e.g., SAS chord) from injected input regardless of client flags.
2. Hosts MUST rate-limit input messages: `input_rate_max` (default 8000 events/sec aggregate). Above the limit, excess events are dropped and the client is notified via `ConfigurationChanged` `change_reason_flags` bit 4 (`InputThrottled`). Sustained 10× over-limit → session teardown.
3. Input is accepted only from the peer holding the input entitlement for the current session mode; hosts in `Host only` role MUST drop all 0x0600 messages.

### 4.6 Telemetry Payloads — post-auth, AEAD-protected

#### `TelemetryReport` (`0x0701`, 32 bytes payload)
As in v1.0:
- `uint32_t encode_latency_us`, `decode_latency_us`, `render_latency_us`, `network_latency_us`
- `uint32_t frames_rendered`, `frames_dropped`, `fec_recovered_frames`
- `uint32_t reserved`

Telemetry is **best-effort and never security-relevant**: hosts/clients MUST NOT make access-control, entitlement, or state-machine decisions from telemetry content.

### 4.7 Host Management & Remote Configuration — post-auth, AEAD-protected

**Authorization model *(New in v1.1)***: Host management messages require the **management entitlement**. Entitlements are bound to the shared secret's provisioning level: PIN-based sessions receive streaming entitlements only; pairing-token sessions receive the entitlements granted at pairing time (bitmask persisted with the pairing record). All management messages from a peer without the management entitlement receive `UnauthorizedConfiguration` — and **that error response itself is rate-limited** (max 10/min) so unauthorized peers cannot use it as an oracle.

#### `GetHostCapabilities` (`0x0801`, 4 bytes payload)
- `uint32_t query_mask` — 0 = all; individual bits reserved for selective queries; undefined bits must be 0

#### `HostCapabilitiesResponse` (`0x0802`, 32 bytes payload)
As in v1.0:
- `uint32_t supported_video_codecs`, `supported_audio_codecs`
- `uint32_t max_encode_width` (≤ 16384), `max_encode_height` (≤ 16384), `max_encode_fps` (≤ 1000)
- `uint8_t supports_hdr10`, `supports_virtual_audio`, `supports_mic_backchannel` (0/1)
- `uint8_t reserved`
- `uint32_t max_bitrate_kbps` (≤ 1,000,000)
- `uint32_t reserved2`

#### `GetHostConfiguration` (`0x0803`, 4 bytes payload)
- `uint32_t config_scope` (0: active session, 1: persistent defaults; >1 → drop)

#### `HostConfigurationResponse` (`0x0804`, 48 bytes) & `SetHostConfiguration` (`0x0805`, 48 bytes)
As in v1.0, with bounds enforced per §4.1.3 ranges:
- `uint32_t config_version`, `display_width` (≤ 16384), `display_height` (≤ 16384), `refresh_rate_hz` (≤ 1000), `target_bitrate_kbps` (≤ `max_bitrate_kbps`), `max_bitrate_kbps`
- `uint8_t preferred_codec` (1–3), `hdr10_enabled` (0/1), `audio_channels` (2/6/8), `audio_quality_mode` (0/1)
- `uint32_t audio_bitrate_kbps` (16..1024)
- `uint16_t input_polling_rate_hz` (30..8000)
- `uint8_t mic_passthrough_enabled` (0/1), `virtual_audio_driver_enabled` (0/1)
- `uint32_t reserved1`, `reserved2`, `reserved3`

`SetHostConfiguration` from a streaming-only peer → `UnauthorizedConfiguration`.

#### `SetHostConfigurationResponse` (`0x0806`, 8 bytes payload)
- `uint32_t status_code`
- `uint32_t applied_config_version` — monotonically increasing; responses MUST echo the applied version

#### `ConfigurationChanged` (`0x0807`, 8 bytes payload)
- `uint32_t new_config_version`
- `uint32_t change_reason_flags` (Bit 0: Remote mutation, 1: Local mutation, 2: Display change, 3: Policy change, 4: InputThrottled; bits 5–31 reserved, must be 0)

---

## 5. Error Codes & Validation Rules

| Error Code | Numeric Value | Description |
| --- | ---: | --- |
| `Success` | 0 | Operation completed successfully. |
| `InvalidMagic` | 1 | Packet magic is not `0x4D53484E`. |
| `UnsupportedVersion` | 2 | Protocol version mismatch. |
| `MalformedHeader` | 3 | Header < 32 bytes, reserved field nonzero, fixed payload size mismatch, or invalid field value. |
| `BufferTooSmall` | 4 | Destination buffer insufficient for serialization. |
| `PayloadTruncated` | 5 | Available bytes less than declared `PayloadSize`, or payload exceeds message maximum. |
| `InvalidSession` | 6 | Session ID does not match active session, or message received outside its permitted state (§9). |
| `AuthenticationFailed` | 7 | HMAC proof mismatch, replayed nonce, or low-order X25519 key. Counts against rate limiter.
| `StreamNotFound` | 8 | Referenced stream ID does not exist in session. |
| `DuplicateSequence` | 9 | Sequence number already processed, or replayed `client_nonce`. |
| `StaleTimestamp` | 10 | Packet timestamp older than discard window (relative monotonic domain only). |
| `UnsupportedCodec` | 11 | Requested codec unsupported or not negotiated. |
| `UnauthorizedConfiguration` | 12 | Peer lacks entitlement, or IDR/input rate abuse. |
| `InvalidConfigurationParameter` | 13 | Setting outside bounds in §4.1.3 / §4.7. |
| `RateLimited` | 14 | Handshake or pre-auth rate limit exceeded; sender MUST back off (≥ 1 s, exponential). *(New in v1.1)* |
| `SessionExpired` | 15 | Session lease elapsed; re-handshake required. *(New in v1.1)* |
| `AeadFailure` | 16 | AEAD authentication tag verification failed. Datagrams with this condition are dropped and counted; > 100 failures in 10 s → session teardown. *(New in v1.1)* |

**Error delivery rules**: Pre-auth (plaintext), error codes are returned **only on the TCP control connection**, never in response to UDP datagrams, to avoid reflection. Post-auth, errors arrive as AEAD-protected messages on the appropriate plane. The one exception is the pre-auth UDP silence rule of §11: no error — of any code — is ever sent in response to a pre-auth UDP datagram.

---

## 6. Serialization Rules *(New in v1.1, formalized)*

1. All structs are **packed**: each field immediately follows the previous with no padding. Spec-declared payload sizes are normative (`sizeof`-checks in tests MUST assert exact sizes — including the corrected `sizeof(ClientAuth) == 84`).
2. Multi-byte fields are Big-Endian on the wire. Frame/payload **bitstream bytes** (video/audio compressed data) are copied verbatim — never byte-swapped.
3. Senders write reserved fields as zero; receivers verify.
4. Codecs MUST NOT read or write past the declared payload bounds; out-of-bounds attempts are programming errors and MUST be caught by fuzzing (§12) and debug-mode bounds assertions.

---

## 7. Transport Binding *(New in v1.1; framing clarified in v1.2)*

- **7.1 Control plane (TCP or QUIC stream)**: handshake, session setup, management, telemetry. All messages on the stream are framed as `4-byte BE length || message_bytes`, where `message_bytes` is the full 32-byte header plus payload (including the AEAD tag when protected). Handshake pre-auth messages (`Hello`, `HelloResponse`, `ClientAuth`) are plaintext. The length prefix is **not** part of the AEAD AAD; the AAD for every protected message is the 32-byte packet header only (§8.2).
- **7.2 Media plane (UDP)**: video, audio, mic, feedback, input. All post-handshake UDP datagrams are AEAD-protected per §8; each UDP datagram is exactly one message (header + payload + tag), no length prefix.
- **7.3** The host binds client media endpoints only to the observed source IP of the authenticated TCP connection. The `client_udp_*_port` fields select the port only. If the first protected UDP datagram from `(client_ip, declared_port)` fails to decrypt to a valid `MediaEndpointConfirm`, the endpoint is unconfirmed and **the host MUST NOT send media to it**.
- **7.4** Host→client media is sent only to confirmed endpoints. Unconfirmed endpoints receive nothing (amplification bound: zero bytes).
- **7.5** Client media ports may change mid-session via a new `SessionSetup`; reconfirmation applies.
- **7.6 `MediaEndpointConfirm` (`0x0109`, 16 bytes payload, client → host, one per UDP port)**: `uint32_t stream_id || uint32_t reserved || uint64_t echo_of_server_nonce`. The host validates the AEAD tag and the echoed nonce before confirming the endpoint.

---

## 8. Post-Handshake Packet Protection *(New in v1.1; nonce construction fixed in v1.2)*

**8.1** Every message after `ServerConfirm` — on both control and media planes — carries its payload encrypted and authenticated with ChaCha20-Poly1305 using the direction-appropriate key from §4.1.1. The AEAD tag (16 bytes) is appended to the message payload and is included in `PayloadSize`. Exception: a `ServerConfirm` reporting `handshake_result != 0` is sent plaintext (§4.1).

**8.2** The AAD (additional authenticated data) for every packet is the 32-byte packet header. This authenticates magic, version, type, payload size, sequence, session id, and timestamp — any header tampering breaks the tag. On the TCP control plane, the 4-byte framing length prefix is **not** included in the AAD.

**8.3** Receivers MUST verify the tag before any payload parsing. Tag failure → drop, count `AeadFailure` (§5).

**8.4 Sequence continuity and anti-replay.** Per direction per stream: receivers track a 64-bit extended sequence number (the 32-bit wire value plus a maintained epoch). Accept only packets whose extended sequence is ≥ `highest_seen - 64` and not previously seen (sliding replay window of 64). The 32-bit wrap increments the epoch when the wire sequence crosses from ≥ 2^31 to < 2^31. Out-of-window or replayed packets → `DuplicateSequence`, drop. **Note**: the epoch is used solely for replay-window bookkeeping; the AEAD nonce uses the raw 32-bit wire sequence (§4.1.1), which is safe because sessions MUST NOT exceed 2³¹ packets per stream (§4.1.1) before re-handshake (§10.2).

**8.5 Key separation.** Handshake keys are never used for media. If a future v2 rekeys, new keys derive via HKDF with `info = "MNBP v2 rekey"`; v1.2 sessions live at most `session_lease_seconds` (default 86400) after which re-handshake is required (`SessionExpired`).

**8.6 Forward secrecy.** Because both X25519 keys are ephemeral and zeroized, compromise of the long-term secret (PIN/token) does not decrypt previously recorded sessions.

---

## 9. Session State Machine *(New in v1.1)*

```
Idle
  │ (TCP control connection accepted)
  ▼
AwaitHello ── Hello ──► validate version, nonce freshness
  │                        │ incompatible version
  │                        ▼
  │                     Closed (respond HelloResponse with version 0x0000
  │                              and all-zero remaining fields, close)
  ▼
AwaitClientAuth ── ClientAuth ──► verify rate limits → verify HMAC proof → X25519 → derive keys
  │                                   │ AuthenticationFailed / RateLimited / timeout (10 s)
  │                                   ▼
  │                                 Closed (ServerConfirm with handshake_result,
  │                                        plaintext for failures)
  ▼
Established ◄── sent by host after successful ServerConfirm
  │
  ├── SessionSetup ──► SessionSetupResponse (success) ──► StreamsActive
  │                         │ InvalidConfigurationParameter
  │                         ▼
  │                    Established (no streams; client may retry)
  │
  ├── MediaEndpointConfirm (per UDP port) ──► endpoint confirmed; media may flow
  │
  ├── SetHostConfiguration / Get* ──► entitlement check (§4.7)
  │
  ├── ConfigurationChanged ──► (host → client, async)
  │
  ├── SessionExpired (lease elapsed) ──► Teardown
  ├── Violation budget exceeded (§11) ──► Teardown (state: reason recorded)
  └── GracefulClose (`0x010A`, 8 bytes: `uint32_t close_code`, `uint32_t reserved`) ──► Closed
Closed ──► session keys, nonce prefixes, and ephemeral secrets zeroized; SessionId retired (never reused)
```

### 9.1 State machine rules
1. In any state, receipt of a message not listed as legal for that state and direction → the datagram is dropped; in `Established`, repeated violations (≥ 20 in 10 s) → teardown with `InvalidSession`.
2. `Hello` retransmission by the client before `ClientAuth` is permitted (identical bytes, idempotent); a *different* `Hello` from the same connection restarts the handshake — and the client MUST generate a fresh `client_nonce` for the restarted handshake.
3. Handshake timeout: if `ClientAuth` is not received within 10 s of `HelloResponse`, the host releases the session slot.
4. Only one handshake may be in flight per control connection per source IP (§11).

---

## 10. Replay, Nonce, and Session Lifecycle Rules *(New in v1.1)*

1. **Nonce freshness**: The host maintains a rolling Bloom filter or LRU of the last 4096 `client_nonce` values seen (per client UUID and globally). A repeated nonce → `DuplicateSequence`, handshake refused, counts toward the rate limiter.
2. **Session lease**: `assigned_session_id` and derived keys expire after `session_lease_seconds`. Renewal requires a full re-handshake; there is no resume without fresh X25519 key agreement (preserving §8.6). The 2³¹-packet-per-stream bound (§4.1.1) is additionally enforced; a stream reaching the bound forces re-handshake regardless of remaining lease.
3. **Single-use handshakes**: A transcript `T` may produce at most one successful session. Replaying `ClientAuth` bytes with a new connection fails because the host rejects the reused nonce and because `assigned_session_id` and salts are fresh per handshake.
4. **Zeroization**: `psk`, `shared_secret`, `handshake_key` (all 80 bytes, including both nonce prefixes), and both X25519 private keys MUST be zeroized immediately after key derivation (prefixes and keys copied into their session-resident locations first) and at session close. Managed copies in C# must use fixed pinned buffers with explicit overwrite; language-level copies of key material are forbidden.
5. **SessionId hygiene**: `SessionId` never appears in logs, telemetry exports, or crash dumps.

---

## 11. Anti-Abuse Budgets *(New in v1.1)*

Host-enforced limits, all enforced *before* any state change, memory allocation, or reply:

| Vector | Limit | Action on excess |
| --- | --- | --- |
| Pre-auth `Hello` rate | 5 per source IP per 10 s, backstopped by 5 per (source IP, client_uuid) per 10 s; the (IP, uuid) limit is primary behind large NATs, the global-IP limit is the backstop | `RateLimited`, then silent drop; source ban 60 s |
| Handshake attempts (HMAC failures) | 5 per source IP per minute | `RateLimited` + exponential backoff ≥ 1 s; ban 10 min after 3 consecutive windows |
| Concurrent half-open handshakes | 8 per source IP, 256 global | Reject oldest / refuse new |
| Pre-auth UDP datagrams | Host sends **zero** bytes in response to pre-auth UDP; all such datagrams dropped silently | — |
| AEAD tag failures | > 100 in 10 s | Session teardown (`AeadFailure`) |
| IDR requests | See §4.4 | Teardown (`UnauthorizedConfiguration`) |
| Input event rate | `input_rate_max` (default 8000/s) | Throttle, then teardown (§4.5.1) |
| Malformed-header budget | 50 in 10 s per source | Teardown / source ban |
| Error-response oracle | Max 10 `UnauthorizedConfiguration` replies/min per session | Silent drop beyond |

Anti-amplification invariant: **the host never emits more bytes than it has received on an unauthenticated or unconfirmed channel**, and emits zero bytes on the media plane before `MediaEndpointConfirm`.

---

## 12. Conformance Testing & Fuzzing Requirements *(New in v1.1)*

Implementations claiming MNBP v1.2 conformance MUST include:

1. **Round-trip codec tests** asserting exact packed sizes for every message (e.g., `sizeof(HelloResponse) == 80`, `sizeof(ClientAuth) == 84`), big-endian layout, and reserved-field zeroing.
2. **Structure-aware fuzzing** of every deserializer with libFuzzer (C++) / SharpFuzz (C#), with the invariants of §2, §4.2, §6 as oracle checks (no OOB, no allocation proportional to attacker-controlled counts).
3. **Handshake vectors**: published deterministic test vectors for Argon2id/HKDF/HMAC/X25519/ChaCha20-Poly1305 stages (RFC 8439 test vectors + MNBP-specific transcript vectors covering `T_client` and `T_host` separately, including a test that `host_proof` verification fails when `host_ephemeral` is substituted). Vectors are published alongside the revision identified by the revision hash in the header comment block; the `<COMMIT_SHA>` placeholder MUST be resolved before a conformance claim is citeable.
4. **Replay/downgrade tests**: recorded-session replay rejected; MITM version-tamper test (mutate `Hello`/`HelloResponse` in transit → HMAC must fail); MITM `host_ephemeral` substitution → `host_proof` must fail.
5. **Amplification test**: unconfirmed-endpoint media suppression; pre-auth UDP response silence.
6. **State machine tests**: every message type attempted in every state; only legal transitions accepted.
7. **Nonce uniqueness tests** *(New in v1.2)*: assert that no two protected packets sent under the same direction key produce the same AEAD nonce, across concurrent streams, control plane, and message-type mixing; assert the client can decrypt a successful `ServerConfirm` using only HKDF-derived material (no in-band prefix dependency).

---

## 13. Summary of Changes from v1.1

| # | Change | Rationale |
| --- | --- | --- |
| 1 | `ClientAuth` payload size corrected 116 → 84 bytes to match its field layout | Declared size contradicted field sum; would break every size-assertion test |
| 2 | Both AEAD nonce prefixes HKDF-derived from `handshake_key` (L extended 64 → 80); `ServerConfirm.aead_nonce_prefix` demoted to informational | v1.1 was circular: the client needed the host's prefix from inside an encrypted packet to decrypt that packet |
| 3 | AEAD nonce construction folds `stream_id` into nonce bytes 4–7 | v1.1 construction reused nonces across streams under the same key — fatal for ChaCha20-Poly1305 |
| 4 | Host ephemeral key bound into `host_proof` via two-stage transcript (`T_client` / `T_host`); proof labels corrected (garbled `"C" || receipt_placeholder` pseudocode removed) | Binds the DH share the client actually uses into the authenticated proof; fixes contradictory proof definition |
| 5 | Version negotiation rule clarified: `server_version` = host's max, negotiated = deterministic `min()` over transcript inputs; incompatible-version `HelloResponse` layout fully defined (all-zero remainder) | Ambiguity in what `server_version` meant; client parser behavior on version 0 was undefined |
| 6 | TCP framing rule clarified: post-handshake length prefix excluded from AAD | Interop-critical ambiguity between §7.1 and §8.1 |
| 7 | Nonce uniqueness and ServerConfirm-decryptability tests added to §12; 2³¹-packets-per-stream bound added (§4.1.1, §10.2) | Reconciles §8.4's extended-sequence epoch with the 32-bit nonce field |
| 8 | Pre-auth `Hello` rate limit split into (IP, uuid) primary + global-IP backstop | False positives behind large NAT |
| 9 | `client_nonce` regeneration mandated on handshake retry/restart | Client-side retry with identical bytes would loop against the replay window |
| 10 | Pre-auth `SessionId` mismatch rule (§2.5) aligned with §11 silence rule; state machine diagram arrow corrected; zeroization scope includes both nonce prefixes | Editorial consistency repairs |
| 11 | HKDF info strings versioned to `"MNBP v1.2 ..."` | Domain separation from v1.1 derivations |

---

## 14. Implementation Checklist (normative "must" index)

- [ ] CSPRNG for all nonces, salts, session ids, X25519 keys (§4.1)
- [ ] Argon2id for PINs; HKDF for tokens; constant-time HMAC compares (§4.1.1)
- [ ] Transcript `T_client` / `T_host` computed from exact on-wire bytes; `host_proof` covers both ephemeral keys (§4.1.1)
- [ ] HKDF derives 80 bytes: two keys + two nonce prefixes; no security-relevant prefix transmitted (§4.1.1)
- [ ] AEAD nonce = prefix(4) || stream_id(4) || seq(4); never repeats under one key (§4.1.1)
- [ ] AEAD tag verified before any payload parse (§8.3)
- [ ] Sliding replay window + epoch handling; epoch never enters the nonce (§8.4)
- [ ] Pre-auth message allowlist enforced at dispatcher (§3, §9)
- [ ] Zero bytes to unconfirmed/unauthenticated endpoints (§7.3, §11)
- [ ] `total_packets`/`packet_index`/`payload_size` bounds checked before any allocation (§4.2)
- [ ] IDR, input, handshake, and error-oracle rate limits (§4.4, §4.5.1, §11)
- [ ] Reserved fields zero-checked (§2)
- [ ] Secure-attention input filtered; locked-desktop policy (§4.5, §4.5.1)
- [ ] Management entitlement model (§4.7)
- [ ] Zeroization on close (keys, prefixes, ephemerals); no `SessionId` in logs (§10)
- [ ] Exact-size codec assertions (`ClientAuth == 84`) + fuzzers + nonce-uniqueness tests in CI (§12)
