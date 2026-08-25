# End-to-End Cryptographic Pairing Pipeline

> [!WARNING]
> **LEGACY COMPATIBILITY REFERENCE**
> This document describes the legacy GameStream challenge-response pairing protocol, not Moonshine's native authentication. This code is classified as **Incompatible** and is unreachable from the production composition root. Moonshine is its own platform with its own protocol (MNBP v1), defined in `docs/PROTOCOL_SPEC_V1.md`.

Moonshine implements an industrial-grade cryptographic pairing subsystem designed to authenticate and establish mutual trust with Sunshine and NVIDIA GameStream hosts. The protocol uses RSA 2048-bit X.509 certificates, SHA-256 / PBKDF2 key derivation, and AES-128 cryptographic challenge-response validation.

## 1. Cryptographic Handshake Architecture

The pairing sequence consists of five sequential phases executed over HTTPS (port 47984):

```
Client (Moonshine)                                  Host (Sunshine / GameStream)
      |                                                        |
      | 1. Phase 1: getservercert                             |
      |    (Salt_client, Cert_client)                          |
      | -----------------------------------------------------> |
      |    Response: (paired=1, Cert_server)                   |
      | <----------------------------------------------------- |
      |                                                        |
      |    [Derive AES Key: K = SHA256(Salt_client || PIN)[0..16]]
      |                                                        |
      | 2. Phase 2: getchallengeresp                          |
      |    Enc_K(Challenge_client)                             |
      | -----------------------------------------------------> |
      |    Response: (paired=1, Enc_K(Challenge_server))       |
      | <----------------------------------------------------- |
      |                                                        |
      | 3. Phase 3: getserverchallengeresp                     |
      |    Enc_K(SHA256(Challenge_server)[0..16])             |
      | -----------------------------------------------------> |
      |    Response: (paired=1, pairingsecret)                 |
      | <----------------------------------------------------- |
      |                                                        |
      | 4. Phase 4: getclientcert (Verification)              |
      | -----------------------------------------------------> |
      |    Response: (paired=1)                                |
      | <----------------------------------------------------- |
      |                                                        |
      | 5. Phase 5: Persist Trusted Cert in KeyStore           |
      v                                                        v
```

## 2. Mathematical Specification and Key Derivation

### Client Identity Generation
On initial setup or first launch, Moonshine generates an RSA 2048-bit keypair and self-signed X.509 certificate using PKCS#1 padding and SHA-256 hashing:

$$\text{Cert}_{\text{client}} = \text{X509v3}(\text{RSA}_{2048}, \text{SHA256}, \text{CN}=\text{"Moonshine Client"})$$

### AES Key Derivation
When the user enters a generated 4-digit PIN (e.g. `4829`), an ephemeral 16-byte random salt is generated:

$$S_{\text{client}} \leftarrow \text{CSPRNG}(16)$$

The 128-bit AES pairing key $K_{\text{AES}}$ is derived by hashing the concatenation of the salt and UTF-8 encoded PIN:

$$K_{\text{AES}} = \text{SHA256}(S_{\text{client}} \mathbin{\Vert} \text{PIN})[0 \dots 15]$$

For modern extensions, Moonshine also supports PBKDF2 key derivation:

$$K_{\text{PBKDF2}} = \text{PBKDF2}(\text{HMAC-SHA256}, \text{PIN}, S_{\text{client}}, 1000, 16)$$

## 3. Challenge-Response Authentication Protocol

### Phase 1: Certificate & Salt Exchange (`getservercert`)
The client sends an HTTP GET request containing the unique ID, client device name, client salt (hex), and client X.509 certificate (hex):

```http
GET /pair?uniqueid=UUID&devicename=Moonshine&update=1&phrase=getservercert&salt=SALT_HEX&clientcert=CERT_HEX HTTP/1.1
```

The host responds with XML containing the host's X.509 certificate:
```xml
<root status_code="200">
    <paired>1</paired>
    <plaincert>SERVER_CERT_HEX</plaincert>
</root>
```

### Phase 2: Client Challenge (`getchallengeresp`)
The client generates a 16-byte random challenge $C_{\text{client}} \leftarrow \text{CSPRNG}(16)$, encrypts it using $K_{\text{AES}}$ in AES-128-ECB mode, and transmits it to the host:

```http
GET /pair?uniqueid=UUID&devicename=Moonshine&clientchallenge=ENCRYPTED_CHALLENGE_HEX HTTP/1.1
```

The host decrypts $C_{\text{client}}$ using the PIN entered into the Sunshine Web UI. If the PIN matches, the host returns an encrypted challenge response $E_K(C_{\text{server}})$.

### Phase 3: Server Challenge Response (`getserverchallengeresp`)
The client decrypts the host's challenge $C_{\text{server}}$, computes its SHA-256 digest, encrypts the first 16 bytes using $K_{\text{AES}}$, and submits the response:

$$R_{\text{client}} = \text{AES-ECB}_{K}(\text{SHA256}(C_{\text{server}})[0 \dots 15])$$

```http
GET /pair?uniqueid=UUID&devicename=Moonshine&serverchallengeresp=ENCRYPTED_RESP_HEX HTTP/1.1
```

### Phase 4: Final Confirmation (`getclientcert`)
The client confirms pairing completion by issuing:

```http
GET /pair?uniqueid=UUID&devicename=Moonshine&phrase=getclientcert HTTP/1.1
```

## 4. KeyStore and Credential Persistence

Moonshine separates key management into clear storage abstractions:

- **`IPairingKeyStore`**: High-level contract for identity retrieval and host certificate storage.
- **`InMemoryPairingKeyStore`**: Thread-safe ephemeral store used during testing and transient sessions.
- **`FilePairingKeyStore`**: Atomic JSON and PEM storage located in `%LOCALAPPDATA%/Moonshine/keystore/`.

### Sensitive Memory Zeroing
All intermediate AES keys, PIN buffers, and plaintexts are explicitly cleared from memory using `CryptographicOperations.ZeroMemory` immediately upon completion of the handshake.
