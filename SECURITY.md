# Security Policy

## Supported Versions

| Version | Status | Supported |
| :--- | :--- | :---: |
| 0.5.6-alpha | Active Development (Pre-release) | :white_check_mark: |

> **Note**: Moonshine is in active pre-release development. No stable release has been published yet. The first planned stable release is `1.0.0-alpha`.

## Reporting a Vulnerability

Moonshine handles sensitive cryptographic keys, pairing certificates, and real-time streaming traffic. We take security vulnerabilities seriously.

If you discover a security vulnerability within Moonshine, please **do not open a public issue**. Instead, follow these steps:

1. Use [GitHub Security Advisories](https://github.com/CodeGrogu/Moonshine/security/advisories/new) to report the vulnerability privately.
2. Include the following details:
   - Detailed description of the vulnerability.
   - Steps to reproduce or proof-of-concept (PoC) code.
   - Potential impact on the client or host systems.
   - Any suggested mitigations.
3. We will acknowledge receipt of your report within 48 hours and provide an estimated timeline for a patch.

## Cryptographic Standards

- **Pairing Authentication**: Moonshine strictly uses authenticated AES-128/256-GCM / CBC with SHA-256 key derivation and ephemeral client/server random challenges.
- **Certificate Verification**: All TLS/HTTPS and control sessions mandate strict validation of host certificate fingerprints.
- **Memory Zeroing**: Sensitive buffers (private keys, pin numbers, salts) are securely wiped from memory using `CryptographicOperations.ZeroMemory` / `SecureZeroMemory` after use.
