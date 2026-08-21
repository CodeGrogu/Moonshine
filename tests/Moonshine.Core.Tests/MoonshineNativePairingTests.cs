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

    public MoonshineNativePairingTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"Moonshine_Pairing_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _hostTrustStore = new MoonshineTrustStore(Path.Combine(_tempDirectory, "host"));
        _clientTrustStore = new MoonshineTrustStore(Path.Combine(_tempDirectory, "client"));

        _hostPairingEngine = new MoonshineNativePairingEngine(_hostTrustStore);
        _clientPairingEngine = new MoonshineNativePairingEngine(_clientTrustStore);
    }

    [Fact]
    public async Task CompletePairingHandshake_EstablishesMutualTrust()
    {
        string hostFingerprint = new string('h', 64);
        string clientFingerprint = new string('c', 64);

        // 1. Host initiates pairing session
        HostPairingSession session = _hostPairingEngine.HostInitiatePairing(
            hostDeviceId: "host-rig-01",
            hostFriendlyName: "Gaming Desktop",
            hostFingerprintSha256: hostFingerprint);

        session.Pin.Should().HaveLength(6);
        session.Salt.Should().HaveCount(16);

        // 2. Client creates pairing request using the PIN displayed on host
        var (clientRequest, clientDerivedKey) = MoonshineNativePairingEngine.ClientCreatePairingRequest(
            pin: session.Pin,
            hostSalt: session.Salt,
            hostNonce: session.HostNonce,
            clientDeviceId: "client-laptop-01",
            clientFriendlyName: "Living Room Laptop",
            clientFingerprintSha256: clientFingerprint);

        // 3. Host processes pairing request
        HostPairingResponse hostResponse = await _hostPairingEngine.HostProcessPairingRequestAsync(
            clientRequest,
            clientAuthorisationLevel: AuthorisationLevel.Controller);

        hostResponse.Status.Should().Be(PairingCeremonyStatus.Success);
        hostResponse.HostProof.Should().NotBeNull();

        // 4. Client finalizes pairing using host's response
        NativePairingResult clientResult = await _clientPairingEngine.ClientFinalizePairingAsync(
            hostResponse,
            clientRequest.ClientNonce,
            clientDerivedKey);

        clientResult.Status.Should().Be(PairingCeremonyStatus.Success);
        clientResult.PairedPeer.Should().NotBeNull();
        clientResult.PairedPeer!.DeviceId.Should().Be("host-rig-01");

        // 5. Verify mutual trust is recorded in both trust stores
        bool hostTrustsClient = await _hostTrustStore.IsPeerTrustedAsync("client-laptop-01", clientFingerprint);
        hostTrustsClient.Should().BeTrue();

        bool clientTrustsHost = await _clientTrustStore.IsPeerTrustedAsync("host-rig-01", hostFingerprint);
        clientTrustsHost.Should().BeTrue();
    }

    [Fact]
    public async Task IncorrectPin_FailsClosed()
    {
        string hostFingerprint = new string('h', 64);
        string clientFingerprint = new string('c', 64);

        HostPairingSession session = _hostPairingEngine.HostInitiatePairing(
            hostDeviceId: "host-rig-02",
            hostFriendlyName: "Gaming Desktop",
            hostFingerprintSha256: hostFingerprint);

        // Client enters wrong PIN (e.g. "000000" vs real PIN)
        string wrongPin = session.Pin == "123456" ? "654321" : "123456";

        var (clientRequest, _) = MoonshineNativePairingEngine.ClientCreatePairingRequest(
            pin: wrongPin,
            hostSalt: session.Salt,
            hostNonce: session.HostNonce,
            clientDeviceId: "client-laptop-02",
            clientFriendlyName: "Living Room Laptop",
            clientFingerprintSha256: clientFingerprint);

        HostPairingResponse hostResponse = await _hostPairingEngine.HostProcessPairingRequestAsync(clientRequest);

        hostResponse.Status.Should().Be(PairingCeremonyStatus.ProofMismatch);

        // Host must NOT have registered client
        bool hostTrustsClient = await _hostTrustStore.IsPeerTrustedAsync("client-laptop-02", clientFingerprint);
        hostTrustsClient.Should().BeFalse();
    }

    public void Dispose()
    {
        _hostTrustStore.Dispose();
        _clientTrustStore.Dispose();
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
