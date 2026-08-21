using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Moonshine.Core.Runtime;
using Moonshine.Core.Security;

namespace Moonshine.Core.Pairing;

public enum PairingCeremonyStatus
{
    Success = 0,
    InvalidPin = 1,
    ProofMismatch = 2,
    InvalidSignature = 3,
    Expired = 4,
    TrustConflict = 5,
    InvalidPayload = 6,
    RateLimited = 7
}

public readonly record struct HostPairingSession(
    string Pin,
    byte[] Salt,
    ulong HostNonce,
    string HostDeviceId,
    string HostFriendlyName,
    string HostPublicKeyPem,
    string HostFingerprintSha256,
    DateTimeOffset CreatedUtc);

public readonly record struct ClientPairingRequest(
    string ClientDeviceId,
    string ClientFriendlyName,
    string ClientPublicKeyPem,
    string ClientFingerprintSha256,
    ulong ClientNonce,
    byte[] ClientPinAuthToken,
    byte[] ClientSignature);

public readonly record struct HostPairingResponse(
    PairingCeremonyStatus Status,
    string Message,
    ulong HostNonce,
    string HostDeviceId,
    string HostFriendlyName,
    string HostPublicKeyPem,
    string HostFingerprintSha256,
    byte[]? HostPinAuthToken,
    byte[]? HostSignature);

public readonly record struct NativePairingResult(
    PairingCeremonyStatus Status,
    string Message,
    PeerIdentity? PairedPeer);

/// <summary>
/// First-party Moonshine pairing ceremony orchestrator combining human-authorised ephemeral PIN
/// authorisation tokens with asymmetric ECDSA NIST P-256 cryptographic proof-of-possession.
/// </summary>
public sealed class MoonshineNativePairingEngine
{
    private const int Pbkdf2Iterations = 10000;
    private const int SaltSizeBytes = 16;
    private readonly IPeerTrustStore _trustStore;
    private readonly Lock _lock = new();
    private HostPairingSession? _activeHostSession;
    private MoonshineIdentityKeyPair? _hostKeyPair;

    public MoonshineNativePairingEngine(IPeerTrustStore trustStore)
    {
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    /// <summary>
    /// Host starts an authenticated pairing ceremony with its long-term identity key pair,
    /// generating an ephemeral 6-digit PIN and cryptographic salt for human verification.
    /// </summary>
    public HostPairingSession HostInitiatePairing(
        MoonshineIdentityKeyPair hostKeyPair,
        string hostDeviceId,
        string hostFriendlyName)
    {
        ArgumentNullException.ThrowIfNull(hostKeyPair);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFriendlyName);

        int rawPin = RandomNumberGenerator.GetInt32(100000, 1000000);
        string pin = rawPin.ToString("D6", CultureInfo.InvariantCulture);

        byte[] salt = new byte[SaltSizeBytes];
        RandomNumberGenerator.Fill(salt);

        Span<byte> nonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(nonceBytes);
        ulong hostNonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(nonceBytes);

        var session = new HostPairingSession(
            Pin: pin,
            Salt: salt,
            HostNonce: hostNonce,
            HostDeviceId: hostDeviceId,
            HostFriendlyName: hostFriendlyName,
            HostPublicKeyPem: hostKeyPair.PublicKeyPem,
            HostFingerprintSha256: hostKeyPair.PublicKeyFingerprintSha256,
            CreatedUtc: DateTimeOffset.UtcNow);

        lock (_lock)
        {
            _activeHostSession = session;
            _hostKeyPair = hostKeyPair;
        }

        return session;
    }

    /// <summary>
    /// Client computes pairing request using its long-term identity key, the displayed PIN, and host parameters.
    /// </summary>
    public static (ClientPairingRequest Request, byte[] DerivedKey) ClientCreatePairingRequest(
        MoonshineIdentityKeyPair clientKeyPair,
        string pin,
        ReadOnlySpan<byte> hostSalt,
        ulong hostNonce,
        string hostPublicKeyPem,
        string clientDeviceId,
        string clientFriendlyName)
    {
        ArgumentNullException.ThrowIfNull(clientKeyPair);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPublicKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFriendlyName);

        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: hostSalt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        Span<byte> clientNonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(clientNonceBytes);
        ulong clientNonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(clientNonceBytes);

        // 1. PIN Authorisation Token (proves knowledge of ephemeral PIN displayed to user)
        byte[] pinAuthToken = ComputePinAuthToken(
            derivedKey,
            "Moonshine-Ceremony-ClientAuth",
            clientNonce,
            hostNonce,
            clientKeyPair.PublicKeyFingerprintSha256);

        // 2. Cryptographic Proof-of-Possession Signature (proves possession of client's private key)
        byte[] transcript = BuildTranscriptBytes(clientNonce, hostNonce, clientKeyPair.PublicKeyPem, hostPublicKeyPem);
        byte[] signature = clientKeyPair.SignData(transcript);

        var request = new ClientPairingRequest(
            ClientDeviceId: clientDeviceId,
            ClientFriendlyName: clientFriendlyName,
            ClientPublicKeyPem: clientKeyPair.PublicKeyPem,
            ClientFingerprintSha256: clientKeyPair.PublicKeyFingerprintSha256,
            ClientNonce: clientNonce,
            ClientPinAuthToken: pinAuthToken,
            ClientSignature: signature);

        return (request, derivedKey);
    }

    /// <summary>
    /// Host verifies client's PIN authorization token and asymmetric proof-of-possession signature.
    /// </summary>
    public async ValueTask<HostPairingResponse> HostProcessPairingRequestAsync(
        ClientPairingRequest request,
        AuthorisationLevel clientAuthorisationLevel = AuthorisationLevel.Controller,
        bool forceReplaceTrust = false,
        CancellationToken ct = default)
    {
        HostPairingSession session;
        MoonshineIdentityKeyPair hostKeyPair;
        lock (_lock)
        {
            if (_activeHostSession is null || _hostKeyPair is null)
            {
                return new HostPairingResponse(PairingCeremonyStatus.Expired, "No active pairing session on host.", 0, "", "", "", "", null, null);
            }
            session = _activeHostSession.Value;
            hostKeyPair = _hostKeyPair;
        }

        // Expire ceremony after 60 seconds
        if (DateTimeOffset.UtcNow - session.CreatedUtc > TimeSpan.FromSeconds(60))
        {
            lock (_lock)
            {
                _activeHostSession = null;
                _hostKeyPair = null;
            }
            return new HostPairingResponse(PairingCeremonyStatus.Expired, "Host pairing ceremony expired.", 0, "", "", "", "", null, null);
        }

        // 1. Verify PIN Authorisation Token
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password: session.Pin,
            salt: session.Salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] expectedClientAuthToken = ComputePinAuthToken(
            derivedKey,
            "Moonshine-Ceremony-ClientAuth",
            request.ClientNonce,
            session.HostNonce,
            request.ClientFingerprintSha256);

        if (!CryptographicOperations.FixedTimeEquals(expectedClientAuthToken, request.ClientPinAuthToken))
        {
            return new HostPairingResponse(PairingCeremonyStatus.ProofMismatch, "Client pairing PIN authorisation token mismatch.", 0, "", "", "", "", null, null);
        }

        // 2. Verify Cryptographic Proof-of-Possession Signature
        byte[] transcript = BuildTranscriptBytes(request.ClientNonce, session.HostNonce, request.ClientPublicKeyPem, session.HostPublicKeyPem);
        bool isSignatureValid = MoonshineIdentityKeyPair.VerifySignature(request.ClientPublicKeyPem, transcript, request.ClientSignature);
        if (!isSignatureValid)
        {
            return new HostPairingResponse(PairingCeremonyStatus.InvalidSignature, "Client asymmetric proof-of-possession signature verification failed.", 0, "", "", "", "", null, null);
        }

        // 3. Register client identity into Host Trust Store
        string computedFingerprint = MoonshineIdentityKeyPair.ComputeFingerprintFromPem(request.ClientPublicKeyPem);
        var clientIdentity = new PeerIdentity(
            DeviceId: request.ClientDeviceId,
            FriendlyName: request.ClientFriendlyName,
            Role: ApplicationRole.Client,
            PublicKeyFingerprintSha256: computedFingerprint,
            CertificatePem: request.ClientPublicKeyPem,
            AuthorisationLevel: clientAuthorisationLevel,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: DateTimeOffset.UtcNow,
            IsRevoked: false);

        TrustRegistrationResult trustResult = await _trustStore.RegisterOrUpdatePeerAsync(
            clientIdentity,
            forceReplaceTrust,
            ct).ConfigureAwait(false);

        if (trustResult.Status == TrustRegistrationStatus.FingerprintConflict)
        {
            return new HostPairingResponse(
                PairingCeremonyStatus.TrustConflict,
                "Client public key fingerprint conflict. Explicit user re-authorisation required to replace trust.",
                0, "", "", "", "", null, null);
        }

        // 4. Generate Host PIN token and asymmetric proof-of-possession signature
        byte[] hostPinAuthToken = ComputePinAuthToken(
            derivedKey,
            "Moonshine-Ceremony-HostAuth",
            request.ClientNonce,
            session.HostNonce,
            session.HostFingerprintSha256);

        byte[] hostSignature = hostKeyPair.SignData(transcript);

        // Wipe active session on successful pairing
        lock (_lock)
        {
            _activeHostSession = null;
            _hostKeyPair = null;
        }

        return new HostPairingResponse(
            Status: PairingCeremonyStatus.Success,
            Message: "Pairing ceremony verified by host.",
            HostNonce: session.HostNonce,
            HostDeviceId: session.HostDeviceId,
            HostFriendlyName: session.HostFriendlyName,
            HostPublicKeyPem: session.HostPublicKeyPem,
            HostFingerprintSha256: session.HostFingerprintSha256,
            HostPinAuthToken: hostPinAuthToken,
            HostSignature: hostSignature);
    }

    /// <summary>
    /// Client verifies Host's PIN authorization token and asymmetric proof-of-possession signature,
    /// recording the trusted Host public key and fingerprint in the client's trust store.
    /// </summary>
    public async ValueTask<NativePairingResult> ClientFinalizePairingAsync(
        HostPairingResponse hostResponse,
        MoonshineIdentityKeyPair clientKeyPair,
        ulong clientNonce,
        byte[] derivedKey,
        bool forceReplaceTrust = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clientKeyPair);
        ArgumentNullException.ThrowIfNull(derivedKey);

        if (hostResponse.Status != PairingCeremonyStatus.Success || hostResponse.HostPinAuthToken is null || hostResponse.HostSignature is null)
        {
            return new NativePairingResult(hostResponse.Status, hostResponse.Message, null);
        }

        // 1. Verify Host PIN token
        byte[] expectedHostAuthToken = ComputePinAuthToken(
            derivedKey,
            "Moonshine-Ceremony-HostAuth",
            clientNonce,
            hostResponse.HostNonce,
            hostResponse.HostFingerprintSha256);

        if (!CryptographicOperations.FixedTimeEquals(expectedHostAuthToken, hostResponse.HostPinAuthToken))
        {
            return new NativePairingResult(PairingCeremonyStatus.ProofMismatch, "Host PIN authorization token mismatch.", null);
        }

        // 2. Verify Host Proof-of-Possession Signature
        byte[] transcript = BuildTranscriptBytes(clientNonce, hostResponse.HostNonce, clientKeyPair.PublicKeyPem, hostResponse.HostPublicKeyPem);
        bool isSignatureValid = MoonshineIdentityKeyPair.VerifySignature(hostResponse.HostPublicKeyPem, transcript, hostResponse.HostSignature);
        if (!isSignatureValid)
        {
            return new NativePairingResult(PairingCeremonyStatus.InvalidSignature, "Host asymmetric proof-of-possession signature verification failed.", null);
        }

        // 3. Register Host identity in Client Trust Store
        string computedHostFingerprint = MoonshineIdentityKeyPair.ComputeFingerprintFromPem(hostResponse.HostPublicKeyPem);
        var hostIdentity = new PeerIdentity(
            DeviceId: hostResponse.HostDeviceId,
            FriendlyName: hostResponse.HostFriendlyName,
            Role: ApplicationRole.Host,
            PublicKeyFingerprintSha256: computedHostFingerprint,
            CertificatePem: hostResponse.HostPublicKeyPem,
            AuthorisationLevel: AuthorisationLevel.Administrator,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: DateTimeOffset.UtcNow,
            IsRevoked: false);

        TrustRegistrationResult trustResult = await _trustStore.RegisterOrUpdatePeerAsync(
            hostIdentity,
            forceReplaceTrust,
            ct).ConfigureAwait(false);

        if (trustResult.Status == TrustRegistrationStatus.FingerprintConflict)
        {
            return new NativePairingResult(
                PairingCeremonyStatus.TrustConflict,
                "Host public key fingerprint conflict. Explicit user re-pairing required.",
                null);
        }

        return new NativePairingResult(PairingCeremonyStatus.Success, "Pairing completed and mutual asymmetric trust established.", hostIdentity);
    }

    private static byte[] ComputePinAuthToken(
        byte[] key,
        string label,
        ulong clientNonce,
        ulong hostNonce,
        string fingerprint)
    {
        byte[] labelBytes = Encoding.UTF8.GetBytes(label);
        byte[] fingerprintBytes = Encoding.UTF8.GetBytes(fingerprint);

        Span<byte> message = stackalloc byte[labelBytes.Length + 16 + fingerprintBytes.Length];
        labelBytes.CopyTo(message);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(message[labelBytes.Length..(labelBytes.Length + 8)], clientNonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(message[(labelBytes.Length + 8)..(labelBytes.Length + 16)], hostNonce);
        fingerprintBytes.CopyTo(message[(labelBytes.Length + 16)..]);

        return HMACSHA256.HashData(key, message);
    }

    private static byte[] BuildTranscriptBytes(
        ulong clientNonce,
        ulong hostNonce,
        string clientPublicKeyPem,
        string hostPublicKeyPem)
    {
        byte[] clientPemBytes = Encoding.UTF8.GetBytes(clientPublicKeyPem);
        byte[] hostPemBytes = Encoding.UTF8.GetBytes(hostPublicKeyPem);

        byte[] transcript = new byte[16 + clientPemBytes.Length + hostPemBytes.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(transcript.AsSpan(0, 8), clientNonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(transcript.AsSpan(8, 8), hostNonce);
        Buffer.BlockCopy(clientPemBytes, 0, transcript, 16, clientPemBytes.Length);
        Buffer.BlockCopy(hostPemBytes, 0, transcript, 16 + clientPemBytes.Length, hostPemBytes.Length);

        return transcript;
    }
}
