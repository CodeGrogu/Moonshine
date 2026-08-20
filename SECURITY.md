# 🛡 Security Policy

## Supported Versions

| Version | Supported          |
| :---    | :---               |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

Moonshine handles sensitive cryptographic keys, pairing certificates, and real-time streaming traffic. We take security vulnerabilities seriously.

If you discover a security vulnerability within Moonshine, please **do not open a public issue**. Instead, follow these steps:

1. Send an email to the security response team: `security@moonshine-stream.org`
2. Include the following details:
   - Detailed description of the vulnerability.
   - Steps to reproduce or proof-of-concept (PoC) code.
   - Potential impact on the client or host systems.
   - Any suggested mitigations.
3. We will acknowledge receipt of your report within 48 hours and provide an estimated timeline for a patch.

## Cryptographic Standards

- **Pairing Authentication**: Moonshine strictly uses authenticated AES-128/256-GCM / CBC with SHA-256 key derivation and ephemeral client/server random challenges.
- **Certificate Verification**: All TLS/HTTPS and RTSP sessions mandate strict validation of host certificate fingerprints.
- **Memory Zeroing**: Sensitive buffers (private keys, pin numbers, salts) are securely wiped from memory using `CryptographicOperations.ZeroMemory` / `SecureZeroMemory` after use.
