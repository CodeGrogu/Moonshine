using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Moonshine.Protocol.Crypto;

/// <summary>
/// High-performance cryptographic primitives and key derivation algorithms for GameStream and Sunshine.
/// Provides zero-allocation AES-128-GCM, AES-CBC, PBKDF2, SHA-256, and constant-time operations.
/// </summary>
public static class MoonshineCryptoEngine
{
    public const int GcmTagSize = 16;
    public const int GcmNonceSize = 12;
    public const int AesBlockSize = 16;
    public const int DefaultSaltSize = 16;
    public const int DefaultChallengeSize = 16;

    /// <summary>
    /// Generates a cryptographically secure numeric PIN (e.g. 4-digit PIN "4829").
    /// </summary>
    public static string GeneratePin(int digits = 4)
    {
        if (digits <= 0 || digits > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "PIN length must be between 1 and 9 digits.");
        }

        int max = (int)Math.Pow(10, digits);
        int value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Generates cryptographically secure random bytes into destination span.
    /// </summary>
    public static void GenerateRandomBytes(Span<byte> destination)
    {
        RandomNumberGenerator.Fill(destination);
    }

    /// <summary>
    /// Derives a 16-byte AES key from PIN and salt via SHA-256 (standard GameStream / Sunshine scheme):
    /// AESKey = SHA256(salt || PIN)[0..16]
    /// </summary>
    public static byte[] DeriveKeyFromPinAndSalt(string pin, ReadOnlySpan<byte> salt)
    {
        byte[] pinBytes = Encoding.UTF8.GetBytes(pin);
        Span<byte> combined = stackalloc byte[salt.Length + pinBytes.Length];
        salt.CopyTo(combined);
        pinBytes.CopyTo(combined[salt.Length..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(combined, hash);

        byte[] key = new byte[16];
        hash[..16].CopyTo(key);
        return key;
    }

    /// <summary>
    /// Derives an AES key via PBKDF2 (HMAC-SHA256) for modern enhanced handshake modes.
    /// </summary>
    public static byte[] DeriveKeyPbkdf2(string pin, ReadOnlySpan<byte> salt, int iterations = 1000, int keySize = 16)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: keySize
        );
    }

    /// <summary>
    /// Encrypts data using AES-128 for GameStream 16-byte challenge-response blocks.
    /// Uses CBC mode with a zero initialization vector (mathematically equivalent to single-block ECB).
    /// </summary>
    [SuppressMessage("Security", "CA5358:Do not use unsafe cipher modes", Justification = "GameStream and Sunshine pairing wire protocol strictly specifies single-block AES-128 challenge encryption.")]
    public static void EncryptAesEcb(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        if (plaintext.Length % AesBlockSize != 0)
        {
            throw new ArgumentException("Plaintext length must be a multiple of 16 bytes for AES block operations.", nameof(plaintext));
        }
        if (destination.Length < plaintext.Length)
        {
            throw new ArgumentException("Destination buffer too small.", nameof(destination));
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();
        aes.IV = new byte[AesBlockSize];

        using var encryptor = aes.CreateEncryptor();
        byte[] input = plaintext.ToArray();
        byte[] output = encryptor.TransformFinalBlock(input, 0, input.Length);
        output.AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Decrypts data using AES-128 for GameStream 16-byte challenge-response blocks.
    /// Uses CBC mode with a zero initialization vector (mathematically equivalent to single-block ECB).
    /// </summary>
    [SuppressMessage("Security", "CA5358:Do not use unsafe cipher modes", Justification = "GameStream and Sunshine pairing wire protocol strictly specifies single-block AES-128 challenge decryption.")]
    public static void DecryptAesEcb(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext, Span<byte> destination)
    {
        if (ciphertext.Length % AesBlockSize != 0)
        {
            throw new ArgumentException("Ciphertext length must be a multiple of 16 bytes for AES block operations.", nameof(ciphertext));
        }
        if (destination.Length < ciphertext.Length)
        {
            throw new ArgumentException("Destination buffer too small.", nameof(destination));
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();
        aes.IV = new byte[AesBlockSize];

        using var decryptor = aes.CreateDecryptor();
        byte[] input = ciphertext.ToArray();
        byte[] output = decryptor.TransformFinalBlock(input, 0, input.Length);
        output.AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Encrypts data using AES-128 in CBC mode.
    /// </summary>
    public static void EncryptAesCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plaintext, Span<byte> destination, PaddingMode padding = PaddingMode.None)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = padding;
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();

        using var encryptor = aes.CreateEncryptor();
        byte[] input = plaintext.ToArray();
        byte[] output = encryptor.TransformFinalBlock(input, 0, input.Length);
        output.AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Decrypts data using AES-128 in CBC mode.
    /// </summary>
    public static void DecryptAesCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext, Span<byte> destination, PaddingMode padding = PaddingMode.None)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = padding;
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();

        using var decryptor = aes.CreateDecryptor();
        byte[] input = ciphertext.ToArray();
        byte[] output = decryptor.TransformFinalBlock(input, 0, input.Length);
        output.AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Zero-allocation AES-GCM authenticated encryption.
    /// </summary>
    public static void EncryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aes = new AesGcm(key, GcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    /// <summary>
    /// Zero-allocation AES-GCM authenticated decryption.
    /// </summary>
    public static void DecryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aes = new AesGcm(key, GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }

    /// <summary>
    /// Computes SHA-256 digest directly into destination span.
    /// </summary>
    public static void ComputeSha256(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        SHA256.HashData(source, destination);
    }

    /// <summary>
    /// Performs constant-time comparison of two byte spans to prevent timing side-channel attacks.
    /// </summary>
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Securely zeros memory buffer containing sensitive key material.
    /// </summary>
    public static void SecureZero(Span<byte> buffer)
    {
        CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>
    /// Generates an RSA 2048-bit self-signed X.509 certificate and private key.
    /// </summary>
    public static (string CertificatePem, string PrivateKeyPem, X509Certificate2 Certificate) GenerateSelfSignedCertificate(
        string subjectName = "CN=Moonshine Client",
        int keySizeBits = 2048,
        int validYears = 10)
    {
        using var rsa = RSA.Create(keySizeBits);
        var req = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(validYears));

        string certPem = cert.ExportCertificatePem();
        string keyPem = rsa.ExportPkcs8PrivateKeyPem();

        return (certPem, keyPem, cert);
    }
}
