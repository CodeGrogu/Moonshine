# Dynamic RTCP Bitrate Adaptation & Congestion Control

Moonshine integrates an active feedback-driven bandwidth estimation and predictive congestion control engine using real-time RTCP loss statistics and round-trip time (RTT) measurements to scale streaming video bitrates dynamically and trigger Instantaneous Decoder Refresh (IDR) frames upon burst packet loss.

---

## 1. Congestion Control Architectural Pipeline

```
Host Video Bitstream (RTP / UDP)
                │
                │ RTP Sequence tracking & FEC Shard Reconstruction
                ▼
UdpSocketPipeline (Unmanaged Contiguous Buffer)
                │
                │ Periodic RTCP Statistics (50ms - 200ms Interval)
                ▼
RtcpLossStatsPacket (Big-Endian Binary Receiver Report)
                │
                │ Feedback Ingestion
                ▼
CongestionController (.NET 9 Managed Layer)
                │
                ├─► Exponential Moving Average (EMA) Loss & RTT Tracking
                ├─► Predictive AIMD Bitrate Adjuster (Additive Increase / Multiplicative Decrease)
                │
                ├─► [Clean: Loss < 1%] ──────────────► +2,000 kbps (Up to MaxBitrateKbps)
                ├─► [Moderate: 1% <= Loss < 5%] ─────► -10% Target Bitrate
                ├─► [Severe: Loss >= 5%] ────────────► -30% Target Bitrate (Down to MinBitrateKbps)
                └─► [Burst Loss > 5 Packets] ────────► Emit RtcpIdrRequestPacket (PLI)
                │
                ▼
RTSP Stream Control Client (Dynamic ANNOUNCE Bitrate Scaling)
```

---

## 2. RTCP Packet Specifications

All RTCP control and feedback structures conform to standard RFC 3550 / RFC 4585 specifications:

### A. Loss Statistics Feedback (`RtcpLossStatsPacket` - 28 Bytes)
| Offset | Type | Field Name | Description |
| :--- | :--- | :--- | :--- |
| `0..1` | `uint16` | `Header` | `0x81C9` (V=2, P=0, Count=1, PT=201 / Receiver Report) |
| `2..3` | `uint16` | `Length` | 32-bit word count minus one |
| `4..7` | `uint32` | `SSRC` | Synchronization source identifier |
| `8..11` | `uint32` | `PacketsReceived` | Total monotonic RTP packets received |
| `12..15` | `uint32` | `PacketsLost` | Total sequence gap packets lost |
| `16..19` | `uint32` | `PacketsRecovered` | Packets successfully recovered via GF(2^8) FEC |
| `20..23` | `uint32` | `LastSequenceNumber` | Highest unwrap 64-bit sequence modulo $2^{32}$ |
| `24..27` | `uint32` | `JitterMicros` | Interarrival jitter variance in microseconds |

### B. Picture Loss Indication / IDR Keyframe Request (`RtcpIdrRequestPacket` - 12 Bytes)
| Offset | Type | Field Name | Description |
| :--- | :--- | :--- | :--- |
| `0..1` | `uint16` | `Header` | `0x81CE` (V=2, P=0, FMT=1 / PLI, PT=206 / Payload-Specific) |
| `2..3` | `uint16` | `Length` | 32-bit word count minus one (2) |
| `4..7` | `uint32` | `SenderSSRC` | Client sender SSRC |
| `8..11` | `uint32` | `MediaSSRC` | Target video stream media SSRC |

---

## 3. Mathematical Adaptation Formulation

1. **Unrecoverable Loss Ratio**:
   \[
   L_{\text{unrec}} = \max\left(0, \frac{\text{Lost} - \text{Recovered}}{\text{Received} + \text{Lost}}\right)
   \]
2. **Exponential Moving Average Smoothing**:
   \[
   L_{\text{smoothed}} = \alpha \cdot L_{\text{unrec}} + (1 - \alpha) \cdot L_{\text{smoothed, prev}} \quad (\alpha = 0.4)
   \]
   \[
   \text{RTT}_{\text{smoothed}} = \beta \cdot \text{RTT}_{\text{instant}} + (1 - \beta) \cdot \text{RTT}_{\text{prev}} \quad (\beta = 0.3)
   \]
3. **Bandwidth Scaling Laws**:
   - **Additive Increase**: $R_{t+1} = \min(R_{\max}, R_t + 2000\text{ kbps})$ when $L_{\text{smoothed}} < 0.01$.
   - **Moderate Reduction**: $R_{t+1} = \max(R_{\min}, 0.90 \cdot R_t)$ when $0.01 \le L_{\text{smoothed}} < 0.05$.
   - **Severe Congestion**: $R_{t+1} = \max(R_{\min}, 0.70 \cdot R_t)$ when $L_{\text{smoothed}} \ge 0.05$.
