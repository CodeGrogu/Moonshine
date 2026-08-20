using System.Buffers;
using System.Text;
using FluentAssertions;
using Moonshine.Protocol.RTSP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class RtspMessageTests
{
    [Fact]
    public void SerializeAndParse_RtspRequest_MatchesExpectedProtocolFormat()
    {
        var msg = RtspMessage.CreateRequest(RtspMethod.Describe, "rtsp://192.168.1.100:48010", 1);
        msg.Headers["Accept"] = "application/sdp";
        msg.Headers["User-Agent"] = "Moonshine/1.0";

        var arrayBufferWriter = new ArrayBufferWriter<byte>();
        msg.Serialize(arrayBufferWriter);

        byte[] serializedBytes = arrayBufferWriter.WrittenSpan.ToArray();
        string serializedText = Encoding.UTF8.GetString(serializedBytes);

        serializedText.Should().Contain("DESCRIBE rtsp://192.168.1.100:48010 RTSP/1.0");
        serializedText.Should().Contain("CSeq: 1");
        serializedText.Should().Contain("Accept: application/sdp");

        bool parsed = RtspMessage.TryParse(serializedBytes, out var parsedMsg);
        parsed.Should().BeTrue();
        parsedMsg.Method.Should().Be(RtspMethod.Describe);
        parsedMsg.CSeq.Should().Be(1);
        parsedMsg.Headers.Should().ContainKey("Accept");
    }

    [Theory]
    [InlineData(RtspMethod.Options)]
    [InlineData(RtspMethod.Setup)]
    [InlineData(RtspMethod.Play)]
    [InlineData(RtspMethod.Announce)]
    [InlineData(RtspMethod.Teardown)]
    public void SerializeAndParse_AllRtspMethods_RoundtripsSuccessfully(RtspMethod method)
    {
        var msg = RtspMessage.CreateRequest(method, "rtsp://192.168.1.50:48010/streamid=video", 42);
        msg.SessionId = "session-test-token-123";
        msg.Headers["Transport"] = "unicast;client_port=47998-47999";

        var writer = new ArrayBufferWriter<byte>();
        msg.Serialize(writer);

        bool parsed = RtspMessage.TryParse(writer.WrittenSpan, out var parsedMsg);
        parsed.Should().BeTrue();
        parsedMsg.Method.Should().Be(method);
        parsedMsg.CSeq.Should().Be(42);
        parsedMsg.SessionId.Should().Be("session-test-token-123");
        parsedMsg.Headers["Transport"].Should().Be("unicast;client_port=47998-47999");
    }

    [Fact]
    public void TryParse_RtspResponseWithBody_ExtractsContentAndStatusCode()
    {
        string rawResponse = """
            RTSP/1.0 200 OK
            CSeq: 2
            Session: sess-abc-123
            Content-Type: application/sdp
            Content-Length: 26

            v=0
            s=Sunshine Stream
            """;

        byte[] rawBytes = Encoding.UTF8.GetBytes(rawResponse);
        bool parsed = RtspMessage.TryParse(rawBytes, out var msg);

        parsed.Should().BeTrue();
        msg.IsResponse.Should().BeTrue();
        msg.StatusCode.Should().Be(200);
        msg.StatusMessage.Should().Be("OK");
        msg.CSeq.Should().Be(2);
        msg.SessionId.Should().Be("sess-abc-123");
        msg.Body.Should().Contain("s=Sunshine Stream");
    }

    [Fact]
    public void TryParse_MalformedInput_ReturnsFalse()
    {
        byte[] empty = [];
        bool parsedEmpty = RtspMessage.TryParse(empty, out _);
        parsedEmpty.Should().BeFalse();

        byte[] invalid = Encoding.UTF8.GetBytes("\r\n\r\n");
        bool parsedInvalid = RtspMessage.TryParse(invalid, out _);
        parsedInvalid.Should().BeFalse();
    }
}
