using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Moonshine.Protocol.Crypto;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class CryptoEngineTests
{
    [Fact]
    public void GeneratePin_DefaultLength_ReturnsFourDigitNumericString()
    {
        string pin = MoonshineCryptoEngine.GeneratePin();

        pin.Should().HaveLength(4);
        pin.Should().MatchRegex(@"^\d{4}$");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    public void GeneratePin_CustomLength_ReturnsExactDigits(int digits)
    {
        string pin = MoonshineCryptoEngine.GeneratePin(digits);
        pin.Should().HaveLength(digits);
        pin.Should().MatchRegex(@"^\d+$");
    }

    [Fact]
    public void DeriveKeyFromPinAndSalt_DeterministicOutput()
    {
        byte[] salt = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];
        string pin = "1234";

        byte[] key1 = MoonshineCryptoEngine.DeriveKeyFromPinAndSalt(pin, salt);
        byte[] key2 = MoonshineCryptoEngine.DeriveKeyFromPinAndSalt(pin, salt);

        key1.Should().HaveCount(16);
        key1.Should().BeEquivalentTo(key2);
    }

    [Fact]
    public void DeriveKeyPbkdf2_ValidParameters_ReturnsSpecifiedKeyLength()
    {
        byte[] salt = new byte[16];
        MoonshineCryptoEngine.GenerateRandomBytes(salt);

        byte[] key = MoonshineCryptoEngine.DeriveKeyPbkdf2("5678", salt, iterations: 1000, keySize: 32);
        key.Should().HaveCount(32);
    }

    [Fact]
    public void EncryptAndDecryptAesEcb_RoundtripsSuccessfully()
    {
        byte[] key = new byte[16];
        MoonshineCryptoEngine.GenerateRandomBytes(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("1234567890abcdef1234567890abcdef"); // 32 bytes (2 blocks)
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] decrypted = new byte[plaintext.Length];

        MoonshineCryptoEngine.EncryptAesEcb(key, plaintext, ciphertext);
        ciphertext.Should().NotBeEquivalentTo(plaintext);

        MoonshineCryptoEngine.DecryptAesEcb(key, ciphertext, decrypted);
        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public void EncryptAndDecryptAesCbc_RoundtripsSuccessfully()
    {
        byte[] key = new byte[16];
        byte[] iv = new byte[16];
        MoonshineCryptoEngine.GenerateRandomBytes(key);
        MoonshineCryptoEngine.GenerateRandomBytes(iv);

        byte[] plaintext = Encoding.UTF8.GetBytes("SecretMessage123"); // 16 bytes
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] decrypted = new byte[plaintext.Length];

        MoonshineCryptoEngine.EncryptAesCbc(key, iv, plaintext, ciphertext);
        MoonshineCryptoEngine.DecryptAesCbc(key, iv, ciphertext, decrypted);

        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public void EncryptAndDecryptAesGcm_RoundtripsSuccessfully()
    {
        byte[] key = new byte[16];
        byte[] nonce = new byte[12];
        MoonshineCryptoEngine.GenerateRandomBytes(key);
        MoonshineCryptoEngine.GenerateRandomBytes(nonce);

        byte[] plaintext = Encoding.UTF8.GetBytes("Sub5msUltraLowLatencyGamingEngineStream");
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        byte[] decrypted = new byte[plaintext.Length];

        MoonshineCryptoEngine.EncryptGcm(key, nonce, plaintext, ciphertext, tag);
        MoonshineCryptoEngine.DecryptGcm(key, nonce, ciphertext, tag, decrypted);

        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public void ConstantTimeEquals_EqualAndDifferent_EvaluatesAccurately()
    {
        byte[] a = [0xAA, 0xBB, 0xCC, 0xDD];
        byte[] b = [0xAA, 0xBB, 0xCC, 0xDD];
        byte[] c = [0xAA, 0xBB, 0xCC, 0xEE];

        MoonshineCryptoEngine.ConstantTimeEquals(a, b).Should().BeTrue();
        MoonshineCryptoEngine.ConstantTimeEquals(a, c).Should().BeFalse();
    }

    [Fact]
    public void GenerateSelfSignedCertificate_ProducesParsableRsa2048X509()
    {
        var (certPem, keyPem, cert) = MoonshineCryptoEngine.GenerateSelfSignedCertificate("CN=Test Moonshine", 2048, 5);

        certPem.Should().StartWith("-----BEGIN CERTIFICATE-----");
        keyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        cert.Subject.Should().Be("CN=Test Moonshine");
        cert.PublicKey.Oid.FriendlyName.Should().Be("RSA");
    }
}
