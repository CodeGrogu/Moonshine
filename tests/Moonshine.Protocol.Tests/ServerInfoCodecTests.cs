using FluentAssertions;
using Moonshine.Protocol.Discovery.Xml;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class ServerInfoCodecTests
{
    [Fact]
    public void Parse_FullSunshineXmlPayload_CorrectlyDeserializesAllMetadata()
    {
        string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <root status_code="200">
            <hostname>GAMING-RIG</hostname>
            <appversion>0.23.1</appversion>
            <gputype>NVIDIA GeForce RTX 4090</gputype>
            <mac>AA:BB:CC:DD:EE:FF</mac>
            <LocalIP>192.168.1.150</LocalIP>
            <ExternalIP>203.0.113.1</ExternalIP>
            <HttpPort>47989</HttpPort>
            <HttpsPort>47984</HttpsPort>
            <PairStatus>1</PairStatus>
            <currentgame>Cyberpunk 2077</currentgame>
            <uniqueid>uuid-gaming-rig-4090</uniqueid>
            <ServerCodecModeSupport>15</ServerCodecModeSupport>
            <MaxLumaPixelsHEVC>35651584</MaxLumaPixelsHEVC>
            <MaxLumaPixelsH264>8912896</MaxLumaPixelsH264>
            <SupportedDisplayModes>
                <DisplayMode>
                    <Width>1920</Width>
                    <Height>1080</Height>
                    <RefreshRate>144</RefreshRate>
                </DisplayMode>
                <DisplayMode>
                    <Width>2560</Width>
                    <Height>1440</Height>
                    <RefreshRate>120</RefreshRate>
                </DisplayMode>
                <DisplayMode>
                    <Width>3840</Width>
                    <Height>2160</Height>
                    <RefreshRate>60</RefreshRate>
                </DisplayMode>
            </SupportedDisplayModes>
        </root>
        """;

        var details = ServerInfoCodec.Parse(xml, fallbackIp: "192.168.1.150");

        details.Should().NotBeNull();
        details!.Hostname.Should().Be("GAMING-RIG");
        details.LocalIp.Should().Be("192.168.1.150");
        details.ExternalIp.Should().Be("203.0.113.1");
        details.HttpPort.Should().Be(47989);
        details.HttpsPort.Should().Be(47984);
        details.AppVersion.Should().Be("0.23.1");
        details.GpuModel.Should().Be("NVIDIA GeForce RTX 4090");
        details.IsPaired.Should().BeTrue();
        details.CurrentGame.Should().Be("Cyberpunk 2077");
        details.UniqueId.Should().Be("uuid-gaming-rig-4090");
        details.MaxLumaPixelsHevc.Should().Be(35651584);

        // Codec check (15 = 1 | 2 | 4 | 8 -> H264, HEVC, AV1, HevcMain10)
        details.CodecCapabilities.HasFlag(ServerCodecCapabilities.H264).Should().BeTrue();
        details.CodecCapabilities.HasFlag(ServerCodecCapabilities.Hevc).Should().BeTrue();
        details.CodecCapabilities.HasFlag(ServerCodecCapabilities.Av1).Should().BeTrue();
        details.CodecCapabilities.HasFlag(ServerCodecCapabilities.HevcMain10).Should().BeTrue();

        details.SupportedDisplayModes.Should().HaveCount(3);
        details.SupportedDisplayModes[0].Should().Be(new DisplayMode(1920, 1080, 144));
        details.SupportedDisplayModes[1].Should().Be(new DisplayMode(2560, 1440, 120));
        details.SupportedDisplayModes[2].Should().Be(new DisplayMode(3840, 2160, 60));
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsNullGracefully()
    {
        string corrupted = "<root><hostname>Broken<unclosed>";
        var details = ServerInfoCodec.Parse(corrupted, fallbackIp: "127.0.0.1");
        details.Should().BeNull();
    }
}
