using FluentAssertions;
using Moonshine.Core.Pairing;
using Moonshine.Core.Security;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineNativePairingTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MoonshineTrustStore _hostTrustStore;
    private readonly MoonshineTrustStore _clientTrustStore;
    private readonly MoonshineNativePairingEngine _hostPairingEngine;
    private readonly MoonshineNativePairingEngine _clientPairingEngine;
    private readonly MoonshineIdentityKeyPair _hostKeyPair;
    private readonly MoonshineIdentityKeyPair _clientKeyPair;

    public MoonshineNativePairingTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"Moonshine_Pairing_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _hostTrustStore = new MoonshineTrustStore(Path.Combine(_tempDirectory, "host"));
        _clientTrustStore = new MoonshineTrustStore(Path.Combine(_tempDirectory, "client"));

        _hostPairingEngine = new MoonshineNativePairingEngine(_hostTrustStore);
        _clientPairingEngine = new MoonshineNativePairingEngine(_clientTrustStore);

        _hostKeyPair = MoonshineIdentityKeyPair.GenerateNew();
        _clientKeyPair = MoonshineIdentityKeyPair.GenerateNew();
    }

    [Fact]
    public async Task CompletePairingHandshake_WithAsymmetricKeys_EstablishesMutualTrust()
    {
        // 1. Host initiates pairing session with its long-term ECDSA key pair
        HostPairingSession session = _hostPairingEngine.HostInitiatePairing(
            hostKeyPair: _hostKeyPair,
            hostDeviceId: "host-rig-01",
            hostFriendlyName: "Gaming Desktop");

        session.Pin.Should().HaveLength(6);
        session.Salt.Should().HaveCount(16);
        session.HostPublicKeyPem.Should().NotBeNullOrWhiteSpace();
        session.HostFingerprintSha256.Should().Be(_hostKeyPair.PublicKeyFingerprintSha256);

        // 2. Client creates pairing request using the PIN and its long-term ECDSA key pair
        var (clientRequest, clientDerivedKey) = MoonshineNativePairingEngine.ClientCreatePairingRequest(
            clientKeyPair: _clientKeyPair,
            pin: session.Pin,
            hostSalt: session.Salt,
            hostNonce: session.HostNonce,
            hostPublicKeyPem: session.HostPublicKeyPem,
            clientDeviceId: "client-laptop-01",
            clientFriendlyName: "Living Room Laptop");

        clientRequest.ClientSignature.Should().HaveCount(64);
        clientRequest.ClientPinAuthToken.Should().HaveCount(32);

        // 3. Host processes pairing request, verifying both PIN and ECDSA signature
        HostPairingResponse hostResponse = await _hostPairingEngine.HostProcessPairingRequestAsync(
            clientRequest,
            clientAuthorisationLevel: AuthorisationLevel.Controller);

        hostResponse.Status.Should().Be(PairingCeremonyStatus.Success);
        hostResponse.HostPinAuthToken.Should().NotBeNull();
        hostResponse.HostSignature.Should().NotBeNull();
        hostResponse.HostSignature!.Length.Should().Be(64);

        // 4. Client finalizes pairing, verifying host's PIN token and ECDSA signature
        NativePairingResult clientResult = await _clientPairingEngine.ClientFinalizePairingAsync(
            hostResponse,
            _clientKeyPair,
            clientRequest.ClientNonce,
            clientDerivedKey);

        clientResult.Status.Should().Be(PairingCeremonyStatus.Success);
        clientResult.PairedPeer.Should().NotBeNull();
        clientResult.PairedPeer!.DeviceId.Should().Be("host-rig-01");

        // 5. Verify mutual trust is recorded in both trust stores with pinned fingerprints
        bool hostTrustsClient = await _hostTrustStore.IsPeerTrustedAsync("client-laptop-01", _clientKeyPair.PublicKeyFingerprintSha256);
        hostTrustsClient.Should().BeTrue();

        bool clientTrustsHost = await _clientTrustStore.IsPeerTrustedAsync("host-rig-01", _hostKeyPair.PublicKeyFingerprintSha256);
        clientTrustsHost.Should().BeTrue();
    }

    [Fact]
    public async Task IncorrectPin_FailsClosed()
    {
        HostPairingSession session = _hostPairingEngine.HostInitiatePairing(
            hostKeyPair: _hostKeyPair,
            hostDeviceId: "host-rig-02",
            hostFriendlyName: "Gaming Desktop");

        string wrongPin = session.Pin == "123456" ? "654321" : "123456";

        var (clientRequest, _) = MoonshineNativePairingEngine.ClientCreatePairingRequest(
            clientKeyPair: _clientKeyPair,
            pin: wrongPin,
            hostSalt: session.Salt,
            hostNonce: session.HostNonce,
            hostPublicKeyPem: session.HostPublicKeyPem,
            clientDeviceId: "client-laptop-02",
            clientFriendlyName: "Living Room Laptop");

        HostPairingResponse hostResponse = await _hostPairingEngine.HostProcessPairingRequestAsync(clientRequest);

        hostResponse.Status.Should().Be(PairingCeremonyStatus.ProofMismatch);

        bool hostTrustsClient = await _hostTrustStore.IsPeerTrustedAsync("client-laptop-02", _clientKeyPair.PublicKeyFingerprintSha256);
        hostTrustsClient.Should().BeFalse();
    }

    [Fact]
    public async Task ForgedSignature_FailsClosed()
    {
        HostPairingSession session = _hostPairingEngine.HostInitiatePairing(
            hostKeyPair: _hostKeyPair,
            hostDeviceId: "host-rig-03",
            hostFriendlyName: "Gaming Desktop");

        var (clientRequest, _) = MoonshineNativePairingEngine.ClientCreatePairingRequest(
            clientKeyPair: _clientKeyPair,
            pin: session.Pin,
            hostSalt: session.Salt,
            hostNonce: session.HostNonce,
            hostPublicKeyPem: session.HostPublicKeyPem,
            clientDeviceId: "client-laptop-03",
            clientFriendlyName: "Living Room Laptop");

        // Forge signature by flipping bytes
        byte[] forgedSignature = new byte[64];
        clientRequest.ClientSignature.CopyTo(forgedSignature, 0);
        forgedSignature[0] ^= 0xFF;

        var forgedRequest = clientRequest with { ClientSignature = forgedSignature };

        HostPairingResponse hostResponse = await _hostPairingEngine.HostProcessPairingRequestAsync(forgedRequest);

        hostResponse.Status.Should().Be(PairingCeremonyStatus.InvalidSignature);

        bool hostTrustsClient = await _hostTrustStore.IsPeerTrustedAsync("client-laptop-03", _clientKeyPair.PublicKeyFingerprintSha256);
        hostTrustsClient.Should().BeFalse();
    }

    public void Dispose()
    {
        _hostTrustStore.Dispose();
        _clientTrustStore.Dispose();
        _hostKeyPair.Dispose();
        _clientKeyPair.Dispose();
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        // ALLOWED_EXCEPTION: Suppress temp folder cleanup error during test tear-down.
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }
}
