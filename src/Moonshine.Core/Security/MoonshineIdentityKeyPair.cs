using System.Security.Cryptography;
using System.Text;

namespace Moonshine.Core.Security;

/// <summary>
/// Long-term asymmetric cryptographic identity for Moonshine host and client instances.
/// Utilises NIST P-256 ECDSA key pairs for high-performance proof-of-possession and handshake signatures.
/// </summary>
public sealed class MoonshineIdentityKeyPair : IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly string _publicKeyPem;
    private readonly string _publicKeyFingerprintSha256;
    private bool _disposed;

    public string PublicKeyPem => _publicKeyPem;
    public string PublicKeyFingerprintSha256 => _publicKeyFingerprintSha256;

    public MoonshineIdentityKeyPair(ECDsa ecdsa)
    {
        _ecdsa = ecdsa ?? throw new ArgumentNullException(nameof(ecdsa));
        _publicKeyPem = _ecdsa.ExportSubjectPublicKeyInfoPem();

        byte[] rawPublicKey = _ecdsa.ExportSubjectPublicKeyInfo();
        byte[] hash = SHA256.HashData(rawPublicKey);
        _publicKeyFingerprintSha256 = Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a new fresh ECDSA NIST P-256 identity key pair.
    /// </summary>
    public static MoonshineIdentityKeyPair GenerateNew()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new MoonshineIdentityKeyPair(ecdsa);
    }

    /// <summary>
    /// Loads an identity key pair from PEM representations.
    /// </summary>
    public static MoonshineIdentityKeyPair FromPrivateKeyPem(string privateKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        return new MoonshineIdentityKeyPair(ecdsa);
    }

    /// <summary>
    /// Loads or creates a persistent identity key pair using DACL-hardened file storage.
    /// </summary>
    public static async Task<MoonshineIdentityKeyPair> GetOrCreatePersistentAsync(
        string storageDirectory,
        string keyFileName = "identity.key",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        string keyPath = Path.Combine(storageDirectory, keyFileName);

        if (File.Exists(keyPath))
        {
            string pem = await SecureFileStore.ReadAllTextSecureAsync(keyPath, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(pem))
            {
                try
                {
                    return FromPrivateKeyPem(pem);
                }
                // ALLOWED_EXCEPTION: If key file is corrupted, regenerate a new clean key pair and overwrite.
                catch (CryptographicException)
                {
                }
            }
        }

        var newIdentity = GenerateNew();
        string exportPem = newIdentity._ecdsa.ExportPkcs8PrivateKeyPem();
        await SecureFileStore.WriteAllTextSecureAsync(keyPath, exportPem, ct).ConfigureAwait(false);
        return newIdentity;
    }

    /// <summary>
    /// Computes an IEEE P1363 ECDSA-SHA256 signature (64 bytes) proving possession of the private key.
    /// </summary>
    public byte[] SignData(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Verifies an IEEE P1363 ECDSA-SHA256 signature against a peer's public key PEM.
    /// </summary>
    public static bool VerifySignature(string peerPublicKeyPem, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerPublicKeyPem);
        if (signature.Length != 64) return false;

        try
        {
            using var peerEcdsa = ECDsa.Create();
            peerEcdsa.ImportFromPem(peerPublicKeyPem);
            return peerEcdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        // ALLOWED_EXCEPTION: If public key PEM is invalid or corrupt, return false and fail closed.
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint from a public key PEM string.
    /// </summary>
    public static string ComputeFingerprintFromPem(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        using var peerEcdsa = ECDsa.Create();
        peerEcdsa.ImportFromPem(publicKeyPem);
        byte[] rawPublicKey = peerEcdsa.ExportSubjectPublicKeyInfo();
        byte[] hash = SHA256.HashData(rawPublicKey);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ecdsa.Dispose();
    }
}
