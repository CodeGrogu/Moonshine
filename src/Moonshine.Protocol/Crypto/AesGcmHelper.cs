using System.Security.Cryptography;

namespace Moonshine.Protocol.Crypto;

/// <summary>
/// Cryptographic utilities for Moonlight/Sunshine authentication and control encryption.
/// Implements AES-128/256-GCM, AES-CBC, SHA-256 key derivation, and secure zeroing.
/// </summary>
public static class AesGcmHelper
{
    public const int TagSize = MoonshineCryptoEngine.GcmTagSize;
    public const int NonceSize = MoonshineCryptoEngine.GcmNonceSize;

    public static byte[] DeriveKeyFromPinAndSalt(string pin, ReadOnlySpan<byte> salt)
        => MoonshineCryptoEngine.DeriveKeyFromPinAndSalt(pin, salt);

    public static void EncryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
        => MoonshineCryptoEngine.EncryptGcm(key, nonce, plaintext, ciphertext, tag, associatedData);

    public static void DecryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
        => MoonshineCryptoEngine.DecryptGcm(key, nonce, ciphertext, tag, plaintext, associatedData);

    public static void WipeMemory(Span<byte> buffer)
        => MoonshineCryptoEngine.SecureZero(buffer);
}
