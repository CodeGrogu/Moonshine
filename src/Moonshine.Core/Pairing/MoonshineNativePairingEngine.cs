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
    Expired = 3,
    TrustConflict = 4,
    InvalidPayload = 5,
    RateLimited = 6
}

public readonly record struct HostPairingSession(
    string Pin,
    byte[] Salt,
    ulong HostNonce,
    string HostDeviceId,
    string HostFriendlyName,
    string HostFingerprintSha256,
    DateTimeOffset CreatedUtc);

public readonly record struct ClientPairingRequest(
    string ClientDeviceId,
    string ClientFriendlyName,
    string ClientFingerprintSha256,
    ulong ClientNonce,
    byte[] ClientProof);

public readonly record struct HostPairingResponse(
    PairingCeremonyStatus Status,
    string Message,
    ulong HostNonce,
    string HostDeviceId,
    string HostFriendlyName,
    string HostFingerprintSha256,
    byte[]? HostProof);

public readonly record struct NativePairingResult(
    PairingCeremonyStatus Status,
    string Message,
    PeerIdentity? PairedPeer);

/// <summary>
/// First-party Moonshine cryptographic pairing engine orchestrating mutual PBKDF2-HMAC-SHA256
/// trust establishment between Host and Client with explicit certificate/key fingerprint pinning.
/// </summary>
public sealed class MoonshineNativePairingEngine
{
    private const int Pbkdf2Iterations = 10000;
    private const int SaltSizeBytes = 16;
    private const int PinDigits = 6;
    private readonly IPeerTrustStore _trustStore;
    private readonly Lock _lock = new();
    private HostPairingSession? _activeHostSession;

    public MoonshineNativePairingEngine(IPeerTrustStore trustStore)
    {
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    /// <summary>
    /// Host starts an authenticated pairing session by generating an ephemeral PIN and cryptographic salt.
    /// </summary>
    public HostPairingSession HostInitiatePairing(
        string hostDeviceId,
        string hostFriendlyName,
        string hostFingerprintSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFriendlyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFingerprintSha256);

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
            HostFingerprintSha256: hostFingerprintSha256,
            CreatedUtc: DateTimeOffset.UtcNow);

        lock (_lock)
        {
            _activeHostSession = session;
        }

        return session;
    }

    /// <summary>
    /// Client computes pairing proof from entered PIN and received Host salt and nonce.
    /// </summary>
    public static (ClientPairingRequest Request, byte[] DerivedKey) ClientCreatePairingRequest(
        string pin,
        ReadOnlySpan<byte> hostSalt,
        ulong hostNonce,
        string clientDeviceId,
        string clientFriendlyName,
        string clientFingerprintSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFriendlyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFingerprintSha256);

        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: hostSalt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        Span<byte> clientNonceBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(clientNonceBytes);
        ulong clientNonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(clientNonceBytes);

        byte[] proof = ComputeProof(
            derivedKey,
            "Moonshine-Pairing-ClientProof",
            clientNonce,
            hostNonce,
            clientFingerprintSha256);

        var request = new ClientPairingRequest(
            ClientDeviceId: clientDeviceId,
            ClientFriendlyName: clientFriendlyName,
            ClientFingerprintSha256: clientFingerprintSha256,
            ClientNonce: clientNonce,
            ClientProof: proof);

        return (request, derivedKey);
    }

    /// <summary>
    /// Host verifies client's pairing proof, records trusted client identity, and returns mutual host proof.
    /// </summary>
    public async ValueTask<HostPairingResponse> HostProcessPairingRequestAsync(
        ClientPairingRequest request,
        AuthorisationLevel clientAuthorisationLevel = AuthorisationLevel.Controller,
        bool forceReplaceTrust = false,
        CancellationToken ct = default)
    {
        HostPairingSession session;
        lock (_lock)
        {
            if (_activeHostSession is null)
            {
                return new HostPairingResponse(PairingCeremonyStatus.Expired, "No active pairing session on host.", 0, "", "", "", null);
            }
            session = _activeHostSession.Value;
        }

        // Expire ceremony after 60 seconds
        if (DateTimeOffset.UtcNow - session.CreatedUtc > TimeSpan.FromSeconds(60))
        {
            lock (_lock)
            {
                _activeHostSession = null;
            }
            return new HostPairingResponse(PairingCeremonyStatus.Expired, "Host pairing ceremony expired.", 0, "", "", "", null);
        }

        // Derive verification key from host's active PIN & Salt
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password: session.Pin,
            salt: session.Salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] expectedClientProof = ComputeProof(
            derivedKey,
            "Moonshine-Pairing-ClientProof",
            request.ClientNonce,
            session.HostNonce,
            request.ClientFingerprintSha256);

        if (!CryptographicOperations.FixedTimeEquals(expectedClientProof, request.ClientProof))
        {
            return new HostPairingResponse(PairingCeremonyStatus.ProofMismatch, "Client pairing proof mismatch or invalid PIN.", 0, "", "", "", null);
        }

        // Register client in trust store
        var clientIdentity = new PeerIdentity(
            DeviceId: request.ClientDeviceId,
            FriendlyName: request.ClientFriendlyName,
            Role: ApplicationRole.Client,
            PublicKeyFingerprintSha256: request.ClientFingerprintSha256,
            CertificatePem: null,
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
                "Client public key fingerprint conflict. Explicit re-authorisation required.",
                0, "", "", "", null);
        }

        // Generate mutual Host proof
        byte[] hostProof = ComputeProof(
            derivedKey,
            "Moonshine-Pairing-HostProof",
            request.ClientNonce,
            session.HostNonce,
            session.HostFingerprintSha256);

        // Wipe active session on successful pairing
        lock (_lock)
        {
            _activeHostSession = null;
        }

        return new HostPairingResponse(
            Status: PairingCeremonyStatus.Success,
            Message: "Pairing ceremony verified by host.",
            HostNonce: session.HostNonce,
            HostDeviceId: session.HostDeviceId,
            HostFriendlyName: session.HostFriendlyName,
            HostFingerprintSha256: session.HostFingerprintSha256,
            HostProof: hostProof);
    }

    /// <summary>
    /// Client processes Host's pairing response, verifies Host's mutual proof, and records trusted Host identity.
    /// </summary>
    public async ValueTask<NativePairingResult> ClientFinalizePairingAsync(
        HostPairingResponse hostResponse,
        ulong clientNonce,
        byte[] derivedKey,
        bool forceReplaceTrust = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(derivedKey);

        if (hostResponse.Status != PairingCeremonyStatus.Success || hostResponse.HostProof is null)
        {
            return new NativePairingResult(hostResponse.Status, hostResponse.Message, null);
        }

        byte[] expectedHostProof = ComputeProof(
            derivedKey,
            "Moonshine-Pairing-HostProof",
            clientNonce,
            hostResponse.HostNonce,
            hostResponse.HostFingerprintSha256);

        if (!CryptographicOperations.FixedTimeEquals(expectedHostProof, hostResponse.HostProof))
        {
            return new NativePairingResult(PairingCeremonyStatus.ProofMismatch, "Host mutual proof verification failed.", null);
        }

        var hostIdentity = new PeerIdentity(
            DeviceId: hostResponse.HostDeviceId,
            FriendlyName: hostResponse.HostFriendlyName,
            Role: ApplicationRole.Host,
            PublicKeyFingerprintSha256: hostResponse.HostFingerprintSha256,
            CertificatePem: null,
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

        return new NativePairingResult(PairingCeremonyStatus.Success, "Pairing completed and mutual trust established.", hostIdentity);
    }

    private static byte[] ComputeProof(
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
}
