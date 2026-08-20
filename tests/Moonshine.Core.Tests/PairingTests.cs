using System.Net;
using System.Text;
using FluentAssertions;
using Moonshine.Core.Pairing;
using Moonshine.Protocol.Crypto;
using Xunit;

namespace Moonshine.Core.Tests;

public class PairingTests
{
    private sealed class SunshineMockServerHandler : HttpMessageHandler
    {
        public string ExpectedPin { get; set; } = "1234";
        public bool ShouldRejectCertExchange { get; set; }
        public string ServerCertPem { get; } = "-----BEGIN CERTIFICATE-----\nMIIB...SERVERCERT\n-----END CERTIFICATE-----";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.Contains("phrase=getservercert"))
            {
                if (ShouldRejectCertExchange)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<root status_code=\"400\"><paired>0</paired></root>")
                    });
                }

                string certHex = Convert.ToHexString(Encoding.UTF8.GetBytes(ServerCertPem)).ToLowerInvariant();
                string xml = $"<root status_code=\"200\"><paired>1</paired><plaincert>{certHex}</plaincert></root>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
            }

            if (url.Contains("clientchallenge="))
            {
                // Verify challenge response
                int challengeIdx = url.IndexOf("clientchallenge=", StringComparison.Ordinal);
                string challengeHex = url[(challengeIdx + 16)..];
                int ampIdx = challengeHex.IndexOf('&');
                if (ampIdx > 0) challengeHex = challengeHex[..ampIdx];

                if (ExpectedPin != "1234") // Incorrect PIN simulation
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<root status_code=\"400\"><paired>0</paired></root>")
                    });
                }

                // Return server challenge
                byte[] serverChallenge = new byte[16];
                MoonshineCryptoEngine.GenerateRandomBytes(serverChallenge);
                byte[] fakeSalt = new byte[16];
                byte[] key = MoonshineCryptoEngine.DeriveKeyFromPinAndSalt("1234", fakeSalt);
                byte[] enc = new byte[16];
                MoonshineCryptoEngine.EncryptAesEcb(key, serverChallenge, enc);

                string challengeRespHex = Convert.ToHexString(enc).ToLowerInvariant();
                string xml = $"<root status_code=\"200\"><paired>1</paired><challengeresponse>{challengeRespHex}</challengeresponse></root>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
            }

            if (url.Contains("serverchallengeresp="))
            {
                string xml = "<root status_code=\"200\"><paired>1</paired><pairingsecret>aabbccdd11223344</pairingsecret></root>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
            }

            if (url.Contains("phrase=getclientcert"))
            {
                string xml = "<root status_code=\"200\"><paired>1</paired></root>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
            }

            if (url.Contains("/unpair"))
            {
                string xml = "<root status_code=\"200\"><paired>0</paired></root>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public void GenerateClientCertificate_ProducesValidX509PemPair()
    {
        var (certPem, keyPem, cert) = MoonshinePairingManager.GenerateClientCertificate();

        certPem.Should().StartWith("-----BEGIN CERTIFICATE-----");
        certPem.Should().Contain("-----END CERTIFICATE-----");
        keyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        keyPem.Should().Contain("-----END PRIVATE KEY-----");
        cert.Subject.Should().Contain("Moonshine Client");
    }

    [Fact]
    public async Task GetOrCreateClientIdentityAsync_PersistsAndReusesIdentity()
    {
        var keyStore = new InMemoryPairingKeyStore();
        var manager = new MoonshinePairingManager(keyStore: keyStore);

        var (cert1, key1) = await manager.GetOrCreateClientIdentityAsync();
        var (cert2, key2) = await manager.GetOrCreateClientIdentityAsync();

        cert1.Should().Be(cert2);
        key1.Should().Be(key2);
    }

    [Fact]
    public async Task PairAsync_CompleteFiveStepHandshake_SucceedsAndPersistsServerCert()
    {
        var mockServer = new SunshineMockServerHandler();
        var httpClient = new HttpClient(mockServer);
        var keyStore = new InMemoryPairingKeyStore();
        var manager = new MoonshinePairingManager(httpClient, keyStore);

        var result = await manager.PairAsync("192.168.1.100", 47984, "1234", "sunshine-rig-01");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("successfully");
        result.ServerCertPem.Should().Be(mockServer.ServerCertPem);

        bool isPaired = await manager.IsHostPairedAsync("sunshine-rig-01");
        isPaired.Should().BeTrue();
    }

    [Fact]
    public async Task PairAsync_IncorrectPin_ReturnsDescriptiveFailure()
    {
        var mockServer = new SunshineMockServerHandler { ExpectedPin = "9999" };
        var httpClient = new HttpClient(mockServer);
        var keyStore = new InMemoryPairingKeyStore();
        var manager = new MoonshinePairingManager(httpClient, keyStore);

        var result = await manager.PairAsync("192.168.1.100", 47984, "0000", "sunshine-rig-01");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("incorrect PIN");

        bool isPaired = await manager.IsHostPairedAsync("sunshine-rig-01");
        isPaired.Should().BeFalse();
    }

    [Fact]
    public async Task PairAsync_HostRejectsCertExchange_ReturnsFailure()
    {
        var mockServer = new SunshineMockServerHandler { ShouldRejectCertExchange = true };
        var httpClient = new HttpClient(mockServer);
        var keyStore = new InMemoryPairingKeyStore();
        var manager = new MoonshinePairingManager(httpClient, keyStore);

        var result = await manager.PairAsync("192.168.1.100", 47984, "1234", "sunshine-rig-01");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("rejected initial");
    }

    [Fact]
    public async Task UnpairAsync_RemovesServerFromKeyStore()
    {
        var mockServer = new SunshineMockServerHandler();
        var httpClient = new HttpClient(mockServer);
        var keyStore = new InMemoryPairingKeyStore();
        var manager = new MoonshinePairingManager(httpClient, keyStore);

        // Pre-save server certificate
        await keyStore.SaveServerCertificateAsync("sunshine-rig-02", "CERT");

        bool unpairResult = await manager.UnpairAsync("192.168.1.100", 47984, "sunshine-rig-02");

        unpairResult.Should().BeTrue();
        bool isPaired = await manager.IsHostPairedAsync("sunshine-rig-02");
        isPaired.Should().BeFalse();
    }

    [Fact]
    public async Task FilePairingKeyStore_PersistsAndLoadsAcrossInstances()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "moonshine_test_keystore_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store1 = new FilePairingKeyStore(tempDir);
            await store1.SaveClientIdentityAsync("CLIENT_CERT_PEM", "CLIENT_KEY_PEM");
            await store1.SaveServerCertificateAsync("server-123", "SERVER_CERT_PEM");

            var store2 = new FilePairingKeyStore(tempDir);
            string? cert = await store2.GetClientCertificatePemAsync();
            string? key = await store2.GetClientPrivateKeyPemAsync();
            string? serverCert = await store2.GetServerCertificatePemAsync("server-123");

            cert.Should().Be("CLIENT_CERT_PEM");
            key.Should().Be("CLIENT_KEY_PEM");
            serverCert.Should().Be("SERVER_CERT_PEM");

            await store2.RemoveServerCertificateAsync("server-123");
            string? removedCert = await store2.GetServerCertificatePemAsync("server-123");
            removedCert.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
