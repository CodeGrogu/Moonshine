using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Moonshine.Protocol.Crypto;

namespace Moonshine.Core.Pairing;

public record PairingResult(bool Success, string Message, string? ClientCertPem);

/// <summary>
/// Handles cryptographic pairing authentication between Moonshine client and Sunshine host.
/// </summary>
public sealed class MoonshinePairingManager
{
    private readonly HttpClient _httpClient;

    public MoonshinePairingManager(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true, // Sunshine self-signed certs
            ConnectTimeout = TimeSpan.FromSeconds(5)
        });
    }

    public static (string CertPem, string KeyPem, X509Certificate2 Cert) GenerateClientCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Moonshine Client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        string certPem = cert.ExportCertificatePem();
        string keyPem = rsa.ExportPkcs8PrivateKeyPem();

        return (certPem, keyPem, cert);
    }

    public async Task<PairingResult> PairAsync(string hostIp, int port, string pin, string uniqueId, CancellationToken ct = default)
    {
        try
        {
            // 1. Generate local salt (16 bytes random)
            byte[] clientSalt = new byte[16];
            RandomNumberGenerator.Fill(clientSalt);
            string clientSaltHex = Convert.ToHexString(clientSalt).ToLowerInvariant();

            // 2. Generate ephemeral client certificate
            var (certPem, _, _) = GenerateClientCertificate();
            string certHex = Convert.ToHexString(Encoding.UTF8.GetBytes(certPem)).ToLowerInvariant();

            // 3. Step 1: Exchange certificates & salts
            string getCertUrl = $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&update=1&phrase=getservercert&salt={clientSaltHex}&clientcert={certHex}";
            string respXml = await _httpClient.GetStringAsync(getCertUrl, ct).ConfigureAwait(false);

            var doc = XDocument.Parse(respXml);
            string? paired = doc.Root?.Element("paired")?.Value;
            if (paired != "1")
            {
                return new PairingResult(false, "Host rejected initial handshake.", null);
            }

            string? serverCertHex = doc.Root?.Element("plaincert")?.Value;
            if (string.IsNullOrEmpty(serverCertHex))
            {
                return new PairingResult(false, "Server certificate missing from response.", null);
            }

            // 4. Derive AES-128 key = SHA256(clientSalt + PIN)[0..16]
            byte[] aesKey = AesGcmHelper.DeriveKeyFromPinAndSalt(pin, clientSalt);

            // 5. Generate random client challenge (16 bytes)
            byte[] clientChallenge = new byte[16];
            RandomNumberGenerator.Fill(clientChallenge);

            // 6. Encrypt client challenge with AES-GCM
            byte[] nonce = new byte[AesGcmHelper.NonceSize];
            RandomNumberGenerator.Fill(nonce);
            byte[] cipherText = new byte[clientChallenge.Length];
            byte[] tag = new byte[AesGcmHelper.TagSize];

            AesGcmHelper.EncryptGcm(aesKey, nonce, clientChallenge, cipherText, tag);

            byte[] challengePayload = new byte[nonce.Length + cipherText.Length + tag.Length];
            nonce.CopyTo(challengePayload, 0);
            cipherText.CopyTo(challengePayload, nonce.Length);
            tag.CopyTo(challengePayload, nonce.Length + cipherText.Length);

            string challengeHex = Convert.ToHexString(challengePayload).ToLowerInvariant();

            // 7. Step 2: Send encrypted challenge
            string challengeUrl = $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&clientchallenge={challengeHex}";
            string challengeRespXml = await _httpClient.GetStringAsync(challengeUrl, ct).ConfigureAwait(false);

            var challengeDoc = XDocument.Parse(challengeRespXml);
            if (challengeDoc.Root?.Element("paired")?.Value != "1")
            {
                return new PairingResult(false, "Host failed client challenge verification (Check PIN).", null);
            }

            return new PairingResult(true, "Successfully paired with host!", certPem);
        }
        catch (Exception ex)
        {
            return new PairingResult(false, $"Pairing failed: {ex.Message}", null);
        }
    }
}
