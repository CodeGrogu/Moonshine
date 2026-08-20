using FluentAssertions;
using Moonshine.Core.Discovery;
using Xunit;

namespace Moonshine.Core.Tests;

public class DiscoveryTests
{
    [Fact]
    public void ParseServerInfoXml_ValidXml_CorrectlyMapsToRecord()
    {
        string xml = """
            <root status_code="200">
                <hostname>GAMING-RIG</hostname>
                <appversion>0.23.1</appversion>
                <gputype>NVIDIA GeForce RTX 4090</gputype>
                <mac>AA:BB:CC:DD:EE:FF</mac>
                <PairStatus>1</PairStatus>
                <ServerCodecModeSupport>3</ServerCodecModeSupport>
            </root>
            """;

        var info = MoonshineDiscoveryService.ParseServerInfoXml("192.168.1.50", 47989, xml);

        info.Should().NotBeNull();
        info!.Hostname.Should().Be("GAMING-RIG");
        info.AppVersion.Should().Be("0.23.1");
        info.GpuModel.Should().Be("NVIDIA GeForce RTX 4090");
        info.MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
        info.IsPaired.Should().BeTrue();
        info.ServerCodecModeSupport.Should().Be(3);
    }
}
