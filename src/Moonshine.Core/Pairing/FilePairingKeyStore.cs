using System.Text.Json;

namespace Moonshine.Core.Pairing;

/// <summary>
/// Persistent file-based keystore storing certificates and keys in the user application data directory.
/// </summary>
public sealed class FilePairingKeyStore : IPairingKeyStore, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly string _storageDirectory;
    private readonly string _clientCertPath;
    private readonly string _clientKeyPath;
    private readonly string _serversJsonPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FilePairingKeyStore(string? baseDirectory = null)
    {
        _storageDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Moonshine",
            "keystore"
        );

        Directory.CreateDirectory(_storageDirectory);
        _clientCertPath = Path.Combine(_storageDirectory, "client.crt");
        _clientKeyPath = Path.Combine(_storageDirectory, "client.key");
        _serversJsonPath = Path.Combine(_storageDirectory, "servers.json");
    }

    public async Task<string?> GetClientCertificatePemAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_clientCertPath)) return null;
            return await File.ReadAllTextAsync(_clientCertPath, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetClientPrivateKeyPemAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_clientKeyPath)) return null;
            return await File.ReadAllTextAsync(_clientKeyPath, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveClientIdentityAsync(string certPem, string keyPem, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(_clientCertPath, certPem, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(_clientKeyPath, keyPem, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetServerCertificatePemAsync(string serverUniqueId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await ReadServersMapAsync(ct).ConfigureAwait(false);
            map.TryGetValue(serverUniqueId, out string? cert);
            return cert;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveServerCertificateAsync(string serverUniqueId, string serverCertPem, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await ReadServersMapAsync(ct).ConfigureAwait(false);
            map[serverUniqueId] = serverCertPem;
            await WriteServersMapAsync(map, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveServerCertificateAsync(string serverUniqueId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await ReadServersMapAsync(ct).ConfigureAwait(false);
            if (map.Remove(serverUniqueId))
            {
                await WriteServersMapAsync(map, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyCollection<string>> GetPairedServerIdsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = await ReadServersMapAsync(ct).ConfigureAwait(false);
            return map.Keys.ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadServersMapAsync(CancellationToken ct)
    {
        if (!File.Exists(_serversJsonPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            string json = await File.ReadAllTextAsync(_serversJsonPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_jsonOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteServersMapAsync(Dictionary<string, string> map, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(map, s_jsonOptions);
        string tempPath = _serversJsonPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, _serversJsonPath, overwrite: true);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
