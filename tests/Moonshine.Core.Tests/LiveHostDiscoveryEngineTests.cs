using System.Net;
using FluentAssertions;
using Moonshine.Core.Discovery;
using Xunit;

namespace Moonshine.Core.Tests;

public class LiveHostDiscoveryEngineTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _xmlResponse;

        public MockHttpMessageHandler(string xmlResponse)
        {
            _xmlResponse = xmlResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_xmlResponse)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ProbeHostAsync_ValidSunshineXml_FiresHostDiscoveredEventAndRegistersHost()
    {
        string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <root status_code="200">
            <hostname>LIVINGROOM-HOST</hostname>
            <appversion>0.23.1</appversion>
            <gputype>AMD Radeon RX 7900 XTX</gputype>
            <mac>11:22:33:44:55:66</mac>
            <LocalIP>192.168.1.75</LocalIP>
            <HttpPort>47989</HttpPort>
            <HttpsPort>47984</HttpsPort>
            <PairStatus>0</PairStatus>
            <uniqueid>livingroom-rx7900xtx</uniqueid>
        </root>
        """;

        var httpClient = new HttpClient(new MockHttpMessageHandler(xml));
        await using var engine = new LiveHostDiscoveryEngine(httpClient);

        DiscoveredHost? discovered = null;
        engine.HostDiscovered += host => discovered = host;

        var hostResult = await engine.ProbeHostAsync("192.168.1.75", 47989);

        hostResult.Should().NotBeNull();
        hostResult!.Hostname.Should().Be("LIVINGROOM-HOST");
        hostResult.IpAddress.Should().Be("192.168.1.75");
        hostResult.GpuModel.Should().Be("AMD Radeon RX 7900 XTX");
        hostResult.IsPaired.Should().BeFalse();
        hostResult.IsOnline.Should().BeTrue();

        discovered.Should().NotBeNull();
        discovered!.HostId.Should().Be("livingroom-rx7900xtx");

        engine.ActiveHosts.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartAndDisposeAsync_LifecycleRunsAndCleansUpSmoothly()
    {
        var httpClient = new HttpClient(new MockHttpMessageHandler("<root></root>"));
        var engine = new LiveHostDiscoveryEngine(httpClient, sweepInterval: TimeSpan.FromMilliseconds(50));

        engine.Start();
        await Task.Delay(100);

        var act = async () => await engine.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
