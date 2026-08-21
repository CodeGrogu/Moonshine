using Moonshine.Core.Runtime;

namespace Moonshine.Core.Security;

public enum AuthorisationLevel
{
    None = 0,
    Viewer = 1,
    Controller = 2,
    Administrator = 3
}

public enum TrustRegistrationStatus
{
    Trusted = 0,
    Updated = 1,
    FingerprintConflict = 2,
    Revoked = 3,
    InvalidPayload = 4
}

public readonly record struct TrustRegistrationResult(
    TrustRegistrationStatus Status,
    string Message,
    PeerIdentity? Peer);

/// <summary>
/// Immutable cryptographic identity record for a trusted Moonshine peer.
/// </summary>
public sealed record PeerIdentity(
    string DeviceId,
    string FriendlyName,
    ApplicationRole Role,
    string PublicKeyFingerprintSha256,
    string? CertificatePem,
    AuthorisationLevel AuthorisationLevel,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastAuthenticatedUtc,
    bool IsRevoked)
{
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(DeviceId) &&
               !string.IsNullOrWhiteSpace(FriendlyName) &&
               !string.IsNullOrWhiteSpace(PublicKeyFingerprintSha256) &&
               PublicKeyFingerprintSha256.Length == 64 &&
               AuthorisationLevel != AuthorisationLevel.None &&
               !IsRevoked;
    }
}
