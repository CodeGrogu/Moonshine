using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonshine.Core.Security;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, PeerIdentity>))]
[JsonSerializable(typeof(PeerIdentity))]
internal sealed partial class TrustStoreJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Production DACL-hardened trust store maintaining pinned Moonshine peer identities.
/// Enforces fail-closed trust verification, explicit trust replacement, and atomic disk writes.
/// </summary>
public sealed class MoonshineTrustStore : IPeerTrustStore, IDisposable
{
    private readonly string _storagePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Dictionary<string, PeerIdentity> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialised;
    private bool _disposed;

    public MoonshineTrustStore(string? baseDirectory = null)
    {
        string directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Moonshine",
            "security");

        _storagePath = Path.Combine(directory, "trusted_peers.json");
    }

    private async ValueTask EnsureLoadedAsync(CancellationToken ct)
    {
        if (_initialised) return;

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialised) return;

            if (File.Exists(_storagePath))
            {
                string json = await SecureFileStore.ReadAllTextSecureAsync(_storagePath, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize(json, TrustStoreJsonContext.Default.DictionaryStringPeerIdentity);
                        if (loaded is not null)
                        {
                            foreach (var kvp in loaded)
                            {
                                _cache[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    // ALLOWED_EXCEPTION: If file is corrupted, preserve empty cache and fail closed rather than crashing.
                    catch (JsonException)
                    {
                        _cache.Clear();
                    }
                }
            }

            _initialised = true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async ValueTask SaveUnderLockAsync(CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(_cache, TrustStoreJsonContext.Default.DictionaryStringPeerIdentity);
        await SecureFileStore.WriteAllTextSecureAsync(_storagePath, json, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsPeerTrustedAsync(string deviceId, string fingerprintSha256, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(deviceId, out PeerIdentity? peer))
            {
                return false;
            }

            if (peer.IsRevoked)
            {
                return false;
            }

            return string.Equals(peer.PublicKeyFingerprintSha256, fingerprintSha256, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<PeerIdentity?> GetPeerAsync(string deviceId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _cache.GetValueOrDefault(deviceId);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<IReadOnlyList<PeerIdentity>> ListPeersAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _cache.Values.ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<TrustRegistrationResult> RegisterOrUpdatePeerAsync(
        PeerIdentity peer,
        bool forceReplaceTrust = false,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(peer);

        if (!peer.IsValid())
        {
            return new TrustRegistrationResult(TrustRegistrationStatus.InvalidPayload, "Peer identity payload is invalid or malformed.", null);
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(peer.DeviceId, out PeerIdentity? existing))
            {
                if (existing.IsRevoked && !forceReplaceTrust)
                {
                    return new TrustRegistrationResult(TrustRegistrationStatus.Revoked, "Peer identity is revoked. Explicit unrevocation required.", existing);
                }

                bool fingerprintMatches = string.Equals(
                    existing.PublicKeyFingerprintSha256,
                    peer.PublicKeyFingerprintSha256,
                    StringComparison.OrdinalIgnoreCase);

                if (!fingerprintMatches && !forceReplaceTrust)
                {
                    return new TrustRegistrationResult(
                        TrustRegistrationStatus.FingerprintConflict,
                        "Peer public key fingerprint mismatch. Explicit user re-authorisation is required to replace trusted identity.",
                        existing);
                }

                _cache[peer.DeviceId] = peer;
                await SaveUnderLockAsync(ct).ConfigureAwait(false);
                return new TrustRegistrationResult(TrustRegistrationStatus.Updated, "Trusted peer identity updated successfully.", peer);
            }

            _cache[peer.DeviceId] = peer;
            await SaveUnderLockAsync(ct).ConfigureAwait(false);
            return new TrustRegistrationResult(TrustRegistrationStatus.Trusted, "Peer identity registered as trusted.", peer);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<bool> RecordAuthenticationSuccessAsync(string deviceId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(deviceId, out PeerIdentity? peer))
            {
                return false;
            }

            PeerIdentity updated = peer with { LastAuthenticatedUtc = DateTimeOffset.UtcNow };
            _cache[deviceId] = updated;
            await SaveUnderLockAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<bool> RevokePeerAsync(string deviceId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(deviceId, out PeerIdentity? peer))
            {
                return false;
            }

            PeerIdentity updated = peer with { IsRevoked = true };
            _cache[deviceId] = updated;
            await SaveUnderLockAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async ValueTask<bool> DeletePeerAsync(string deviceId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.Remove(deviceId))
            {
                await SaveUnderLockAsync(ct).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fileLock.Dispose();
    }
}
