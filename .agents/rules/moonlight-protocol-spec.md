# Moonlight & GameStream / Sunshine Protocol Invariants

## 1. Network Ports
- `47989`: HTTP Discovery & Server Information
- `47984`: HTTPS Pairing & Certificate Exchange
- `48010`: RTSP Stream Session Control
- `47998`: Video Stream (UDP / RTP / FEC)
- `48000`: Audio Stream (UDP / RTP / Opus)
- `47999`: Control Stream (UDP / Encrypted Loss Feedback / Ping / IDR Request)

## 2. Cryptographic Protocol
- Pairing requires AES-128-GCM or AES-128-CBC with key derived as `SHA256(ClientSalt + PIN)[0..16]`.
- Ephemeral self-signed X.509 client certificates are exchanged during `/pair?phrase=getservercert`.
- Ephemeral client challenges must be verified by the server before `/pair` returns `paired: 1`.

## 3. Video & Audio Encoders
- **Codecs**: H.264 (Constrained Baseline / High), HEVC (Main / Main10 for HDR), AV1.
- **Color Spaces**: Rec.709 (SDR) and Rec.2020 / HDR10 (PQ Transfer function).
