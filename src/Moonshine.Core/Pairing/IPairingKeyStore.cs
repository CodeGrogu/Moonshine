namespace Moonshine.Core.Pairing;

/// <summary>
/// Secure storage abstraction for client cryptographic identities and paired server certificates.
/// </summary>
public interface IPairingKeyStore
{
    /// <summary>
    /// Retrieves the persisted client certificate PEM.
    /// </summary>
    Task<string?> GetClientCertificatePemAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the persisted client private key PEM.
    /// </summary>
    Task<string?> GetClientPrivateKeyPemAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the client's X.509 certificate and private key PEMs.
    /// </summary>
    Task SaveClientIdentityAsync(string certPem, string keyPem, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a paired server's trusted certificate PEM by its UniqueId.
    /// </summary>
    Task<string?> GetServerCertificatePemAsync(string serverUniqueId, CancellationToken ct = default);

    /// <summary>
    /// Persists a paired server's trusted certificate.
    /// </summary>
    Task SaveServerCertificateAsync(string serverUniqueId, string serverCertPem, CancellationToken ct = default);

    /// <summary>
    /// Removes a paired server's certificate (unpairing).
    /// </summary>
    Task RemoveServerCertificateAsync(string serverUniqueId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all paired server unique IDs.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetPairedServerIdsAsync(CancellationToken ct = default);
}
