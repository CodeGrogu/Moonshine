using FluentAssertions;
using Moonshine.Core.Runtime;
using Moonshine.Core.Security;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineTrustStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MoonshineTrustStore _store;

    public MoonshineTrustStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"Moonshine_TrustStore_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _store = new MoonshineTrustStore(_tempDirectory);
    }

    [Fact]
    public async Task RegisterAndValidatePeer_Success()
    {
        string fingerprint = new string('a', 64);
        var peer = new PeerIdentity(
            DeviceId: "client-device-123",
            FriendlyName: "Living Room PC",
            Role: ApplicationRole.Client,
            PublicKeyFingerprintSha256: fingerprint,
            CertificatePem: null,
            AuthorisationLevel: AuthorisationLevel.Controller,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: null,
            IsRevoked: false);

        TrustRegistrationResult result = await _store.RegisterOrUpdatePeerAsync(peer);
        result.Status.Should().Be(TrustRegistrationStatus.Trusted);

        bool isTrusted = await _store.IsPeerTrustedAsync("client-device-123", fingerprint);
        isTrusted.Should().BeTrue();

        bool wrongFingerprint = await _store.IsPeerTrustedAsync("client-device-123", new string('b', 64));
        wrongFingerprint.Should().BeFalse();
    }

    [Fact]
    public async Task FingerprintMismatch_ReturnsConflict_UnlessForced()
    {
        string originalFingerprint = new string('1', 64);
        string newFingerprint = new string('2', 64);

        var originalPeer = new PeerIdentity(
            DeviceId: "host-456",
            FriendlyName: "Main Server",
            Role: ApplicationRole.Host,
            PublicKeyFingerprintSha256: originalFingerprint,
            CertificatePem: null,
            AuthorisationLevel: AuthorisationLevel.Administrator,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: null,
            IsRevoked: false);

        await _store.RegisterOrUpdatePeerAsync(originalPeer);

        var replacementPeer = originalPeer with { PublicKeyFingerprintSha256 = newFingerprint };

        // Attempt without forceReplaceTrust -> must return conflict
        TrustRegistrationResult conflictResult = await _store.RegisterOrUpdatePeerAsync(replacementPeer, forceReplaceTrust: false);
        conflictResult.Status.Should().Be(TrustRegistrationStatus.FingerprintConflict);

        // Verify original fingerprint is still trusted
        (await _store.IsPeerTrustedAsync("host-456", originalFingerprint)).Should().BeTrue();
        (await _store.IsPeerTrustedAsync("host-456", newFingerprint)).Should().BeFalse();

        // Attempt with forceReplaceTrust -> must succeed
        TrustRegistrationResult replaceResult = await _store.RegisterOrUpdatePeerAsync(replacementPeer, forceReplaceTrust: true);
        replaceResult.Status.Should().Be(TrustRegistrationStatus.Updated);

        (await _store.IsPeerTrustedAsync("host-456", newFingerprint)).Should().BeTrue();
        (await _store.IsPeerTrustedAsync("host-456", originalFingerprint)).Should().BeFalse();
    }

    [Fact]
    public async Task RevokePeer_FailsClosed()
    {
        string fingerprint = new string('f', 64);
        var peer = new PeerIdentity(
            DeviceId: "laptop-789",
            FriendlyName: "Remote Laptop",
            Role: ApplicationRole.Client,
            PublicKeyFingerprintSha256: fingerprint,
            CertificatePem: null,
            AuthorisationLevel: AuthorisationLevel.Viewer,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: null,
            IsRevoked: false);

        await _store.RegisterOrUpdatePeerAsync(peer);
        (await _store.IsPeerTrustedAsync("laptop-789", fingerprint)).Should().BeTrue();

        bool revokeOk = await _store.RevokePeerAsync("laptop-789");
        revokeOk.Should().BeTrue();

        (await _store.IsPeerTrustedAsync("laptop-789", fingerprint)).Should().BeFalse();

        PeerIdentity? retrieved = await _store.GetPeerAsync("laptop-789");
        retrieved.Should().NotBeNull();
        retrieved!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task PersistenceAcrossStoreInstances_RestoresTrustCorrectly()
    {
        string fingerprint = new string('c', 64);
        var peer = new PeerIdentity(
            DeviceId: "desktop-001",
            FriendlyName: "Desktop 1",
            Role: ApplicationRole.Host,
            PublicKeyFingerprintSha256: fingerprint,
            CertificatePem: null,
            AuthorisationLevel: AuthorisationLevel.Administrator,
            CreatedUtc: DateTimeOffset.UtcNow,
            LastAuthenticatedUtc: null,
            IsRevoked: false);

        await _store.RegisterOrUpdatePeerAsync(peer);

        // Instantiate second instance with same directory
        using var store2 = new MoonshineTrustStore(_tempDirectory);
        bool isTrusted = await store2.IsPeerTrustedAsync("desktop-001", fingerprint);
        isTrusted.Should().BeTrue();

        IReadOnlyList<PeerIdentity> list = await store2.ListPeersAsync();
        list.Should().HaveCount(1);
        list[0].DeviceId.Should().Be("desktop-001");
    }

    public void Dispose()
    {
        _store.Dispose();
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
