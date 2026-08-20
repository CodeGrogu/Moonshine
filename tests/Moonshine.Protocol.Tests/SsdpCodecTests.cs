using System.Net;
using System.Text;
using FluentAssertions;
using Moonshine.Protocol.Discovery.Ssdp;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class SsdpCodecTests
{
    [Fact]
    public void EncodeSearchRequest_CustomPortAndMx_ProducesValidHttpHeaders()
    {
        byte[] buffer = new byte[256];
        int len = SsdpCodec.EncodeSearchRequest(buffer, targetPort: 48010, mxSeconds: 3);

        string requestStr = Encoding.ASCII.GetString(buffer, 0, len);

        requestStr.Should().StartWith("M-SEARCH * HTTP/1.1\r\n");
        requestStr.Should().Contain("HOST: 239.255.255.250:48010\r\n");
        requestStr.Should().Contain("MAN: \"ssdp:discover\"\r\n");
        requestStr.Should().Contain($"ST: {SsdpCodec.SunshineServiceType}\r\n");
        requestStr.Should().Contain("MX: 3\r\n");
        requestStr.Should().EndWith("\r\n\r\n");
    }

    [Fact]
    public void TryParseResponse_ValidSsdpOkResponse_ExtractsLocationAndIp()
    {
        string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            "LOCATION: http://192.168.1.50:47989/serverinfo\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaServer:1\r\n" +
            "USN: uuid:12345678-abcd-ef01-2345-6789abcdef01::urn:schemas-upnp-org:device:MediaServer:1\r\n" +
            "SERVER: Windows/10.0 UPnP/1.0 Sunshine/0.23.1\r\n\r\n";

        byte[] bytes = Encoding.ASCII.GetBytes(rawResponse);

        bool success = SsdpCodec.TryParseResponse(bytes, out var record);

        success.Should().BeTrue();
        record.Should().NotBeNull();
        record!.HostIp.Should().Be(IPAddress.Parse("192.168.1.50"));
        record.HostPort.Should().Be(47989);
        record.ServiceType.Should().Be("urn:schemas-upnp-org:device:MediaServer:1");
        record.MaxAgeSeconds.Should().Be(1800);
        record.ServerHeader.Should().Contain("Sunshine/0.23.1");
    }

    [Fact]
    public void TryParseResponse_NonHttpResponse_ReturnsFalse()
    {
        byte[] invalid = Encoding.ASCII.GetBytes("RANDOM_DATA_PACKET_NOT_HTTP\r\n\r\n");
        bool success = SsdpCodec.TryParseResponse(invalid, out var record);

        success.Should().BeFalse();
        record.Should().BeNull();
    }
}
