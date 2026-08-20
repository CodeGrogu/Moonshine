---
name: moonshine-protocol-diagnostics
description: >-
  Provides runbooks and diagnostic workflows for testing GameStream and Sunshine host discovery,
  cryptographic pairing (AES-GCM / X.509 cert exchange), RTSP stream setup, and RTP packet handling.
  Use when troubleshooting connectivity, pairing failures, or RTSP handshake issues.
---

# Moonshine Protocol Diagnostics Skill

This skill assists in debugging and validating protocol exchanges between Moonshine and Sunshine / GameStream hosts.

## Protocol Diagnostic Workflow

### 1. Host Discovery
- Query `http://<HOST_IP>:47989/serverinfo` (or HTTPS port 47984).
- Validate XML output for `PairStatus` (0 = unpaired, 1 = paired), `ServerCodecModeSupport`, and `appversion`.

### 2. Pairing Handshake Debugging
- **Step 1**: Ensure 16-byte random salt and valid self-signed client certificate PEM are sent to `/pair?phrase=getservercert`.
- **Step 2**: Check that AES key derivation uses `SHA256(ClientSalt + PIN)[0..16]`.
- **Step 3**: Verify client challenge encryption uses 12-byte random nonce and 16-byte authentication tag with AES-GCM.

### 3. RTSP Stream Negotiation
- Check RTSP `OPTIONS`, `DESCRIBE`, `SETUP`, `PLAY` transactions on TCP port 48010.
- Verify SDP payload format (video codec, resolution, refresh rate, audio channels).
