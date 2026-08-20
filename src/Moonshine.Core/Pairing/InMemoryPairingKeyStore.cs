using System.Collections.Concurrent;

namespace Moonshine.Core.Pairing;

/// <summary>
/// Thread-safe in-memory keystore implementation for ephemeral sessions and test fixtures.
/// </summary>
public sealed class InMemoryPairingKeyStore : IPairingKeyStore
{
    private string? _clientCertPem;
    private string? _clientKeyPem;
    private readonly ConcurrentDictionary<string, string> _serverCertificates = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetClientCertificatePemAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_clientCertPem);
    }

    public Task<string?> GetClientPrivateKeyPemAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_clientKeyPem);
    }

    public Task SaveClientIdentityAsync(string certPem, string keyPem, CancellationToken ct = default)
    {
        _clientCertPem = certPem;
        _clientKeyPem = keyPem;
        return Task.CompletedTask;
    }

    public Task<string?> GetServerCertificatePemAsync(string serverUniqueId, CancellationToken ct = default)
    {
        _serverCertificates.TryGetValue(serverUniqueId, out string? cert);
        return Task.FromResult(cert);
    }

    public Task SaveServerCertificateAsync(string serverUniqueId, string serverCertPem, CancellationToken ct = default)
    {
        _serverCertificates[serverUniqueId] = serverCertPem;
        return Task.CompletedTask;
    }

    public Task RemoveServerCertificateAsync(string serverUniqueId, CancellationToken ct = default)
    {
        _serverCertificates.TryRemove(serverUniqueId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> GetPairedServerIdsAsync(CancellationToken ct = default)
    {
        IReadOnlyCollection<string> ids = _serverCertificates.Keys.ToList().AsReadOnly();
        return Task.FromResult(ids);
    }
}
