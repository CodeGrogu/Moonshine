using System.Text.Json;
using System.Text.Json.Serialization;
using Moonshine.Core.Security;

namespace Moonshine.Core.Pairing;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class KeyStoreJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Persistent file-based keystore storing certificates and keys in the user application data directory.
/// Uses source-generated JSON serialization for 100% Native AOT trimming safety.
/// Employs SecureFileStore to enforce Windows DACLs and atomic replacement on private keys and certificates.
/// </summary>
public sealed class FilePairingKeyStore : IPairingKeyStore, IDisposable
{
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
        CleanupStaleTempFiles(_storageDirectory);
        _clientCertPath = Path.Combine(_storageDirectory, "client.crt");
        _clientKeyPath = Path.Combine(_storageDirectory, "client.key");
        _serversJsonPath = Path.Combine(_storageDirectory, "servers.json");
    }

    private static void CleanupStaleTempFiles(string directory)
    {
        try
        {
            string fullDirPath = Path.GetFullPath(directory);
            DateTime thresholdUtc = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5));

            foreach (string file in Directory.EnumerateFiles(directory, "*.tmp.*"))
            {
                try
                {
                    string fullFilePath = Path.GetFullPath(file);
                    // Ensure the file is strictly contained within the intended keystore directory namespace
                    if (!fullFilePath.StartsWith(fullDirPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Prune only files exceeding the conservative age threshold to protect concurrent active writes
                    if (File.GetLastWriteTimeUtc(file) < thresholdUtc)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Suppress transient lock or access errors during background cleanup
                }
            }
        }
        catch
        {
            // Suppress directory enumeration failures
        }
    }

    public async Task<string?> GetClientCertificatePemAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_clientCertPath)) return null;
            return await SecureFileStore.ReadAllTextSecureAsync(_clientCertPath, ct).ConfigureAwait(false);
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
            return await SecureFileStore.ReadAllTextSecureAsync(_clientKeyPath, ct).ConfigureAwait(false);
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
            await SecureFileStore.WriteAllTextSecureAsync(_clientCertPath, certPem, ct).ConfigureAwait(false);
            await SecureFileStore.WriteAllTextSecureAsync(_clientKeyPath, keyPem, ct).ConfigureAwait(false);
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
            string json = await SecureFileStore.ReadAllTextSecureAsync(_serversJsonPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, KeyStoreJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteServersMapAsync(Dictionary<string, string> map, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(map, KeyStoreJsonContext.Default.DictionaryStringString);
        await SecureFileStore.WriteAllTextSecureAsync(_serversJsonPath, json, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
