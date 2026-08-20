using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Moonshine.Protocol.Crypto;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class AesGcmHelperTests
{
    [Fact]
    public void DeriveKeyFromPinAndSalt_ProducesDeterministic16ByteKey()
    {
        byte[] salt = new byte[16];
        Array.Fill(salt, (byte)0x42);
        string pin = "1234";

        byte[] key1 = AesGcmHelper.DeriveKeyFromPinAndSalt(pin, salt);
        byte[] key2 = AesGcmHelper.DeriveKeyFromPinAndSalt(pin, salt);

        key1.Length.Should().Be(16);
        key1.Should().Equal(key2);
    }

    [Fact]
    public void EncryptAndDecryptGcm_RoundtripsSuccessfully()
    {
        byte[] key = new byte[16];
        RandomNumberGenerator.Fill(key);
        byte[] nonce = new byte[AesGcmHelper.NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] plaintext = Encoding.UTF8.GetBytes("Moonshine Ultra Low Latency Stream");
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcmHelper.TagSize];
        byte[] decrypted = new byte[plaintext.Length];

        AesGcmHelper.EncryptGcm(key, nonce, plaintext, ciphertext, tag);
        AesGcmHelper.DecryptGcm(key, nonce, ciphertext, tag, decrypted);

        decrypted.Should().Equal(plaintext);
    }
}
