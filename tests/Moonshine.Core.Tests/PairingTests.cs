using FluentAssertions;
using Moonshine.Core.Pairing;
using Xunit;

namespace Moonshine.Core.Tests;

public class PairingTests
{
    [Fact]
    public void GenerateClientCertificate_ProducesValidX509PemPair()
    {
        var (certPem, keyPem, cert) = MoonshinePairingManager.GenerateClientCertificate();

        certPem.Should().StartWith("-----BEGIN CERTIFICATE-----");
        certPem.Should().EndWith("-----END CERTIFICATE-----\n");
        keyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        cert.Subject.Should().Contain("Moonshine Client");
    }
}
