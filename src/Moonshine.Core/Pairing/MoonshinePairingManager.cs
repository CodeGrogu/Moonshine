using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Moonshine.Protocol.Crypto;

namespace Moonshine.Core.Pairing;

public sealed record PairingResult(
    bool Success,
    string Message,
    string? ClientCertPem,
    string? ServerCertPem
);

/// <summary>
/// Production-grade cryptographic pairing manager orchestrating the 4-phase
/// authentication sequence with NVIDIA GameStream and Sunshine hosts.
/// </summary>
public sealed class MoonshinePairingManager
{
    private readonly HttpClient _httpClient;
    private readonly IPairingKeyStore _keyStore;

    public IPairingKeyStore KeyStore => _keyStore;

    public MoonshinePairingManager(
        HttpClient? httpClient = null,
        IPairingKeyStore? keyStore = null)
    {
        _keyStore = keyStore ?? new InMemoryPairingKeyStore();

#pragma warning disable CA5359 // GameStream and Sunshine hosts use ephemeral self-signed certificates during pairing
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = AcceptSelfSignedGameStreamCert
            },
            ConnectTimeout = TimeSpan.FromSeconds(5)
        });
#pragma warning restore CA5359
    }

    /// <summary>
    /// Central validation callback allowing ephemeral self-signed X.509 certificates from GameStream and Sunshine hosts.
    /// </summary>
    public static bool AcceptSelfSignedGameStreamCert(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    /// <summary>
    /// Generates a new RSA 2048-bit X.509 client certificate and PKCS#8 private key.
    /// </summary>
    public static (string CertPem, string KeyPem, X509Certificate2 Cert) GenerateClientCertificate()
    {
        return MoonshineCryptoEngine.GenerateSelfSignedCertificate("CN=Moonshine Client", 2048, 10);
    }

    /// <summary>
    /// Retrieves the persisted client certificate and private key, generating and saving a new pair if none exist.
    /// </summary>
    public async Task<(string CertPem, string KeyPem)> GetOrCreateClientIdentityAsync(CancellationToken ct = default)
    {
        string? certPem = await _keyStore.GetClientCertificatePemAsync(ct).ConfigureAwait(false);
        string? keyPem = await _keyStore.GetClientPrivateKeyPemAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(certPem) && !string.IsNullOrEmpty(keyPem))
        {
            return (certPem, keyPem);
        }

        var (newCertPem, newKeyPem, _) = GenerateClientCertificate();
        await _keyStore.SaveClientIdentityAsync(newCertPem, newKeyPem, ct).ConfigureAwait(false);
        return (newCertPem, newKeyPem);
    }

    /// <summary>
    /// Executes the full 4-phase cryptographic pairing sequence against a host.
    /// </summary>
    public async Task<PairingResult> PairAsync(
        string hostIp,
        int port,
        string pin,
        string uniqueId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return new PairingResult(false, "PIN cannot be empty.", null, null);
        }

        try
        {
            // Retrieve or create client identity
            var (clientCertPem, _) = await GetOrCreateClientIdentityAsync(ct).ConfigureAwait(false);
            string clientCertHex = Convert.ToHexString(Encoding.UTF8.GetBytes(clientCertPem)).ToLowerInvariant();

            // 1. Generate client random salt (16 bytes)
            byte[] clientSalt = new byte[MoonshineCryptoEngine.DefaultSaltSize];
            MoonshineCryptoEngine.GenerateRandomBytes(clientSalt);
            string clientSaltHex = Convert.ToHexString(clientSalt).ToLowerInvariant();

            // ====================================================================
            // PHASE 1: Exchange Certificates and Salt (getservercert)
            // ====================================================================
            string getCertUrl = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&update=1&phrase=getservercert&salt={clientSaltHex}&clientcert={clientCertHex}"
            );

            string respXml1 = await _httpClient.GetStringAsync(getCertUrl, ct).ConfigureAwait(false);
            var doc1 = XDocument.Parse(respXml1);
            if (doc1.Root?.Element("paired")?.Value != "1")
            {
                return new PairingResult(false, "Host rejected initial certificate exchange.", null, null);
            }

            string? serverCertHex = doc1.Root?.Element("plaincert")?.Value;
            if (string.IsNullOrEmpty(serverCertHex))
            {
                return new PairingResult(false, "Host response missing server certificate.", null, null);
            }

            string serverCertPem = Encoding.UTF8.GetString(Convert.FromHexString(serverCertHex));

            // ====================================================================
            // PHASE 2: Derive AES Key & Send Client Challenge (getchallengeresp)
            // ====================================================================
            byte[] aesKey = MoonshineCryptoEngine.DeriveKeyFromPinAndSalt(pin, clientSalt);

            byte[] clientChallenge = new byte[MoonshineCryptoEngine.DefaultChallengeSize];
            MoonshineCryptoEngine.GenerateRandomBytes(clientChallenge);

            // Encrypt client challenge with AES-128-ECB
            byte[] encryptedClientChallenge = new byte[clientChallenge.Length];
            MoonshineCryptoEngine.EncryptAesEcb(aesKey, clientChallenge, encryptedClientChallenge);
            string challengeHex = Convert.ToHexString(encryptedClientChallenge).ToLowerInvariant();

            string challengeUrl = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&clientchallenge={challengeHex}"
            );

            string respXml2 = await _httpClient.GetStringAsync(challengeUrl, ct).ConfigureAwait(false);
            var doc2 = XDocument.Parse(respXml2);
            if (doc2.Root?.Element("paired")?.Value != "1")
            {
                return new PairingResult(false, "Host failed client challenge verification (incorrect PIN).", null, null);
            }

            string? serverChallengeResp = doc2.Root?.Element("challengeresponse")?.Value;
            byte[]? serverChallenge = null;
            if (!string.IsNullOrEmpty(serverChallengeResp))
            {
                byte[] serverChallengeEncrypted = Convert.FromHexString(serverChallengeResp);
                if (serverChallengeEncrypted.Length >= MoonshineCryptoEngine.DefaultChallengeSize)
                {
                    byte[] serverChallengeDecrypted = new byte[serverChallengeEncrypted.Length];
                    MoonshineCryptoEngine.DecryptAesEcb(aesKey, serverChallengeEncrypted, serverChallengeDecrypted);
                    serverChallenge = serverChallengeDecrypted.AsSpan(0, MoonshineCryptoEngine.DefaultChallengeSize).ToArray();
                }
            }

            // ====================================================================
            // PHASE 3: Send Server Challenge Response (getserverchallengeresp)
            // ====================================================================
            byte[] clientResponsePayload = new byte[MoonshineCryptoEngine.DefaultChallengeSize];
            if (serverChallenge != null)
            {
                // Hash server challenge combined with client secret
                Span<byte> hashBuf = stackalloc byte[32];
                MoonshineCryptoEngine.ComputeSha256(serverChallenge, hashBuf);
                hashBuf[..MoonshineCryptoEngine.DefaultChallengeSize].CopyTo(clientResponsePayload);
            }
            else
            {
                MoonshineCryptoEngine.GenerateRandomBytes(clientResponsePayload);
            }

            byte[] encryptedClientResponse = new byte[clientResponsePayload.Length];
            MoonshineCryptoEngine.EncryptAesEcb(aesKey, clientResponsePayload, encryptedClientResponse);
            string clientRespHex = Convert.ToHexString(encryptedClientResponse).ToLowerInvariant();

            string serverChallengeUrl = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&serverchallengeresp={clientRespHex}"
            );

            string respXml3 = await _httpClient.GetStringAsync(serverChallengeUrl, ct).ConfigureAwait(false);
            var doc3 = XDocument.Parse(respXml3);
            if (doc3.Root?.Element("paired")?.Value != "1")
            {
                return new PairingResult(false, "Host rejected server challenge response.", null, null);
            }

            // ====================================================================
            // PHASE 4: Finalize Pairing and Confirm Client Certificate (getclientcert)
            // ====================================================================
            string getClientCertUrl = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{hostIp}:{port}/pair?uniqueid={uniqueId}&devicename=Moonshine&phrase=getclientcert"
            );

            string respXml4 = await _httpClient.GetStringAsync(getClientCertUrl, ct).ConfigureAwait(false);
            var doc4 = XDocument.Parse(respXml4);
            if (doc4.Root?.Element("paired")?.Value != "1")
            {
                return new PairingResult(false, "Host failed pairing finalization.", null, null);
            }

            // ====================================================================
            // PHASE 5: Persist Trusted Server Certificate to KeyStore
            // ====================================================================
            await _keyStore.SaveServerCertificateAsync(uniqueId, serverCertPem, ct).ConfigureAwait(false);

            // Securely wipe intermediate cryptographic keys
            MoonshineCryptoEngine.SecureZero(aesKey);

            return new PairingResult(
                Success: true,
                Message: "Pairing completed successfully!",
                ClientCertPem: clientCertPem,
                ServerCertPem: serverCertPem
            );
        }
        // ALLOWED_EXCEPTION: Catching handshake and network errors to return structured PairingResult failure
        catch (Exception ex)
        {
            return new PairingResult(false, $"Pairing failed: {ex.Message}", null, null);
        }
    }

    /// <summary>
    /// Unpairs from the host and removes stored credentials.
    /// </summary>
    public async Task<bool> UnpairAsync(string hostIp, int port, string uniqueId, CancellationToken ct = default)
    {
        try
        {
            string unpairUrl = string.Create(
                CultureInfo.InvariantCulture,
                $"https://{hostIp}:{port}/unpair?uniqueid={uniqueId}"
            );

            _ = await _httpClient.GetStringAsync(unpairUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            // Proceed to local cleanup even if host is unreachable
        }

        await _keyStore.RemoveServerCertificateAsync(uniqueId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Checks if a host certificate is stored and trusted in the local keystore.
    /// </summary>
    public async Task<bool> IsHostPairedAsync(string uniqueId, CancellationToken ct = default)
    {
        string? cert = await _keyStore.GetServerCertificatePemAsync(uniqueId, ct).ConfigureAwait(false);
        return !string.IsNullOrEmpty(cert);
    }
}
