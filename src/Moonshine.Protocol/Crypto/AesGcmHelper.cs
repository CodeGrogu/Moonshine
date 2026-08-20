using System.Security.Cryptography;

namespace Moonshine.Protocol.Crypto;

/// <summary>
/// Cryptographic utilities for Moonlight/Sunshine authentication and control encryption.
/// Implements AES-128/256-GCM, AES-CBC, SHA-256 key derivation, and secure zeroing.
/// </summary>
public static class AesGcmHelper
{
    public const int TagSize = 16;
    public const int NonceSize = 12;

    public static byte[] DeriveKeyFromPinAndSalt(string pin, ReadOnlySpan<byte> salt)
    {
        byte[] pinBytes = System.Text.Encoding.UTF8.GetBytes(pin);
        Span<byte> combined = stackalloc byte[salt.Length + pinBytes.Length];
        salt.CopyTo(combined);
        pinBytes.CopyTo(combined[salt.Length..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(combined, hash);

        // Moonlight/Sunshine uses the first 16 bytes of SHA-256 hash as AES-128 key
        byte[] key = new byte[16];
        hash[..16].CopyTo(key);
        return key;
    }

    public static void EncryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    public static void DecryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }

    public static void WipeMemory(Span<byte> buffer)
    {
        CryptographicOperations.ZeroMemory(buffer);
    }
}
