# Stateful RTSP Stream Control and Dynamic SDP Negotiation

Moonshine provides an asynchronous, stateful RTSP (Real Time Streaming Protocol) client and SDP (Session Description Protocol) negotiator tailored for NVIDIA GameStream and Sunshine streaming servers over TCP port 48010.

---

## 1. RTSP Stream Control Architecture

The stream lifecycle transitions through an explicit state machine:

```
[Disconnected] 
      │ ConnectAsync(hostIp, 48010)
      ▼
[Connected]
      │ SendOptionsAsync()
      ▼
[OptionsReceived]
      │ SendDescribeAsync(config) [SDP Offer / Answer Exchange]
      ▼
[Described]
      │ SendSetupVideoAsync() (Port 47998)
      ▼
[VideoSetup]
      │ SendSetupAudioAsync() (Port 48000)
      ▼
[AudioSetup]
      │ SendPlayAsync()
      ▼
[Playing] ◄───► [Dynamic ANNOUNCE Bitrate & Loss Adaptation]
      │ SendTeardownAsync()
      ▼
[Teardown] -> [Disconnected]
```

---

## 2. Dynamic SDP Negotiation Specifications

The Session Description Protocol (RFC 4566) payload contains configuration parameters for video, audio, QoS, FEC matrices, and HDR10 mastering metadata.

### Video Media Descriptor (`m=video`)
- **Payload Types**:
  - `96`: H.264 (AVC) Baseline / High Profile
  - `98`: H.265 / HEVC Main 10 (10-bit YUV420)
  - `100`: AV1 Main Profile
- **GameStream Custom QoS Attributes**:
  - `a=x-nv-video[0].clientViewportWd`: Target display width (e.g. 1920, 2560, 3840).
  - `a=x-nv-video[0].clientViewportHt`: Target display height (e.g. 1080, 1440, 2160).
  - `a=x-nv-video[0].fps`: Streaming frame rate (e.g. 60, 120, 144, 240 fps).
  - `a=x-nv-video[0].initialBitrateKbps`: Video bitrate budget (5,000 - 150,000 kbps).
  - `a=x-nv-video[0].dynamicRangeMode`: `0` for SDR, `1` for HDR10.
  - `a=x-nv-vqos[0].fec.k`: Reed-Solomon source shard count $K$ (e.g. 20).
  - `a=x-nv-vqos[0].fec.n`: Reed-Solomon total shard count $N$ (e.g. 25).

### HDR10 Static Metadata (SMPTE ST 2086 / CTA-861-G)
When HDR10 is enabled (`dynamicRangeMode: 1`), mastering display colour primaries and light levels are negotiated in the SDP offer:
- `a=x-nv-video[0].hdr.displayPrimaries:34000,16000,13250,34500,7500,3000` (Rec.2020 R, G, B chromaticity coordinates in units of 0.00002).
- `a=x-nv-video[0].hdr.whitePoint:15635,16450` (D65 white point).
- `a=x-nv-video[0].hdr.masteringLuminance:1000,1` (Max/Min display luminance in nits).
- `a=x-nv-video[0].hdr.maxCll:1000` (Max Content Light Level).
- `a=x-nv-video[0].hdr.maxFall:400` (Max Frame Average Light Level).

### Audio Media Descriptor (`m=audio`)
- **Payload Type**: `97` (Opus)
- **Parameters**: 48,000 Hz sample rate, 2-channel stereo or 6-channel 5.1 surround sound, bitrate up to 512 kbps.

---

## 3. Dynamic Telemetry & QoS Announcements (`ANNOUNCE`)

During an active streaming session in the `Playing` state, Moonshine transmits low-latency feedback to Sunshine:

### Bitrate Update
```http
ANNOUNCE rtsp://192.168.1.50:48010/streamid=video RTSP/1.0
CSeq: 12
Session: sess-987654321
Content-Type: application/x-nv-qos
Content-Length: 16

bitrate=45000
```

### Packet Loss Statistics
```http
ANNOUNCE rtsp://192.168.1.50:48010/streamid=video RTSP/1.0
CSeq: 13
Session: sess-987654321
Content-Type: application/x-nv-loss-stats
Content-Length: 22

loss=3;total=1500
```
