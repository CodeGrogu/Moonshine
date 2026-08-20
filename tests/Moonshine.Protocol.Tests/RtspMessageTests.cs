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
}
