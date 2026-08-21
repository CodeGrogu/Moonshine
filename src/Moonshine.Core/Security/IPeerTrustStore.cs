namespace Moonshine.Core.Security;

/// <summary>
/// Service contract for persisting and validating trusted peer identities with explicit pinning.
/// </summary>
public interface IPeerTrustStore
{
    ValueTask<bool> IsPeerTrustedAsync(string deviceId, string fingerprintSha256, CancellationToken ct = default);

    ValueTask<PeerIdentity?> GetPeerAsync(string deviceId, CancellationToken ct = default);

    ValueTask<IReadOnlyList<PeerIdentity>> ListPeersAsync(CancellationToken ct = default);

    ValueTask<TrustRegistrationResult> RegisterOrUpdatePeerAsync(PeerIdentity peer, bool forceReplaceTrust = false, CancellationToken ct = default);

    ValueTask<bool> RecordAuthenticationSuccessAsync(string deviceId, CancellationToken ct = default);

    ValueTask<bool> RevokePeerAsync(string deviceId, CancellationToken ct = default);

    ValueTask<bool> DeletePeerAsync(string deviceId, CancellationToken ct = default);
}
