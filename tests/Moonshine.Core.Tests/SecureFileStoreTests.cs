using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Moonshine.Core.Security;
using Xunit;

namespace Moonshine.Core.Tests;

public sealed class SecureFileStoreTests : IDisposable
{
    private readonly string _testDirectory;

    public SecureFileStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "Moonshine_SecureFileStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Suppress test cleanup exceptions
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SecureFileStore_WriteAllTextSecureAsync_DisablesInheritanceAndGrantsExclusiveAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string filePath = Path.Combine(_testDirectory, "test_key.pem");
        string sampleKey = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0...\n-----END RSA PRIVATE KEY-----";

        await SecureFileStore.WriteAllTextSecureAsync(filePath, sampleKey);

        File.Exists(filePath).Should().BeTrue();
        string readBack = await SecureFileStore.ReadAllTextSecureAsync(filePath);
        readBack.Should().Be(sampleKey);

        var fileInfo = new FileInfo(filePath);
        FileSecurity security = fileInfo.GetAccessControl(AccessControlSections.Access);

        // Invariant 1: DACL inheritance is explicitly disabled and protected
        security.AreAccessRulesProtected.Should().BeTrue();

        // Invariant 2: Zero inherited rules
        var inheritedRules = security.GetAccessRules(includeExplicit: false, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        inheritedRules.Should().BeEmpty();

        // Invariant 3: Explicit rules contain only CurrentUser and SYSTEM with FullControl
        var explicitRules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        explicitRules.Should().HaveCount(2);

        var currentUserSid = WindowsIdentity.GetCurrent().User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        var userRule = explicitRules.SingleOrDefault(r => r.IdentityReference.Value == currentUserSid.Value);
        userRule.Should().NotBeNull();
        userRule!.AccessControlType.Should().Be(AccessControlType.Allow);
        userRule.FileSystemRights.Should().HaveFlag(FileSystemRights.FullControl);

        var systemRule = explicitRules.SingleOrDefault(r => r.IdentityReference.Value == systemSid.Value);
        systemRule.Should().NotBeNull();
        systemRule!.AccessControlType.Should().Be(AccessControlType.Allow);
        systemRule.FileSystemRights.Should().HaveFlag(FileSystemRights.FullControl);

        // Invariant 4: No prohibited identities exist
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var builtInUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        explicitRules.Any(r => r.IdentityReference.Value == everyoneSid.Value).Should().BeFalse();
        explicitRules.Any(r => r.IdentityReference.Value == authUsersSid.Value).Should().BeFalse();
        explicitRules.Any(r => r.IdentityReference.Value == builtInUsersSid.Value).Should().BeFalse();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SecureFileStore_AtomicReplacement_OverwritesExistingFilePreservingDacl()
    {
        string filePath = Path.Combine(_testDirectory, "atomic_key.pem");
        string originalContent = "INITIAL_SECRET_PAYLOAD_V1";
        string updatedContent = "ROTATED_SECRET_PAYLOAD_V2";

        await SecureFileStore.WriteAllTextSecureAsync(filePath, originalContent);
        string v1 = await SecureFileStore.ReadAllTextSecureAsync(filePath);
        v1.Should().Be(originalContent);

        await SecureFileStore.WriteAllTextSecureAsync(filePath, updatedContent);
        string v2 = await SecureFileStore.ReadAllTextSecureAsync(filePath);
        v2.Should().Be(updatedContent);

        if (OperatingSystem.IsWindows())
        {
            var fileInfo = new FileInfo(filePath);
            FileSecurity security = fileInfo.GetAccessControl(AccessControlSections.Access);
            security.AreAccessRulesProtected.Should().BeTrue();

            var explicitRules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            explicitRules.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task SecureFileStore_Roundtrip_WritesAndReadsSuccessfully()
    {
        string filePath = Path.Combine(_testDirectory, "binary_payload.dat");
        byte[] payload = new byte[65536];
        Random.Shared.NextBytes(payload);

        await SecureFileStore.WriteAllBytesSecureAsync(filePath, payload);
        byte[] readBack = await SecureFileStore.ReadAllBytesSecureAsync(filePath);

        readBack.Should().Equal(payload);
    }

    [Fact]
    public async Task SecureFileStore_FailureAndCancellation_CleansUpTempFileAndPreservesOriginal()
    {
        string filePath = Path.Combine(_testDirectory, "cancellation_test.key");
        string originalContent = "UNTOUCHED_KEY_MATERIAL";
        await SecureFileStore.WriteAllTextSecureAsync(filePath, originalContent);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        Func<Task> act = async () =>
        {
            await SecureFileStore.WriteAllTextSecureAsync(filePath, "CORRUPT_PAYLOAD", cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert original file is preserved
        string readBack = await SecureFileStore.ReadAllTextSecureAsync(filePath);
        readBack.Should().Be(originalContent);

        // Assert zero temp files remain in directory
        string[] tempFiles = Directory.GetFiles(_testDirectory, "*.tmp.*");
        tempFiles.Should().BeEmpty();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void SecureFileStore_ApplyStrictDirectoryDacl_DisablesInheritanceAndGrantsExclusiveAccess()
    {
        if (!OperatingSystem.IsWindows()) return;

        string subDir = Path.Combine(_testDirectory, "Keys_Dacl_Test");
        SecureFileStore.ApplyStrictDirectoryDacl(subDir);

        Directory.Exists(subDir).Should().BeTrue();

        var dirInfo = new DirectoryInfo(subDir);
        DirectorySecurity security = dirInfo.GetAccessControl(AccessControlSections.Access);

        // Invariant 1: Inheritance is disabled and protected
        security.AreAccessRulesProtected.Should().BeTrue();

        // Invariant 2: Explicit rules contain only CurrentUser and SYSTEM
        var explicitRules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        explicitRules.Should().HaveCount(2);

        var currentUserSid = WindowsIdentity.GetCurrent().User!;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        var userRule = explicitRules.SingleOrDefault(r => r.IdentityReference.Value == currentUserSid.Value);
        userRule.Should().NotBeNull();
        userRule!.FileSystemRights.Should().HaveFlag(FileSystemRights.FullControl);

        var systemRule = explicitRules.SingleOrDefault(r => r.IdentityReference.Value == systemSid.Value);
        systemRule.Should().NotBeNull();
        systemRule!.FileSystemRights.Should().HaveFlag(FileSystemRights.FullControl);

        // Invariant 3: Prohibited broad identities are stripped
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var builtInUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        explicitRules.Any(r => r.IdentityReference.Value == everyoneSid.Value).Should().BeFalse();
        explicitRules.Any(r => r.IdentityReference.Value == authUsersSid.Value).Should().BeFalse();
        explicitRules.Any(r => r.IdentityReference.Value == builtInUsersSid.Value).Should().BeFalse();
    }
}
