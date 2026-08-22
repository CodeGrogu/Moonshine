using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MoonshineFeedbackCodecTests
{
    [Fact]
    public void FeedbackLossStats_WriteAndRead_RoundtripsSuccessfully()
    {
        var originalPayload = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 42,
            LastReceivedFrameIndex = 1205,
            PacketsReceived = 15000,
            PacketsLost = 75,
            PacketsRecoveredFec = 60,
            RoundTripTimeUs = 8500,
            JitterUs = 450,
            EstimatedBandwidthKbps = 65000
        };

        byte[] buffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];
        bool writeSuccess = MoonshineFeedbackCodec.TryWriteLossStats(
            in originalPayload,
            buffer,
            out int bytesWritten,
            sessionId: 0xDEADBEEFCAFE,
            sequenceNumber: 101);

        writeSuccess.Should().BeTrue();
        bytesWritten.Should().Be(MoonshineFeedbackCodec.LossStatsPacketSize);

        MoonshineErrorCode readResult = MoonshineFeedbackCodec.TryReadLossStats(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineFeedbackLossStatsPayload readPayload);

        readResult.Should().Be(MoonshineErrorCode.Success);
        header.Magic.Should().Be(MoonshineProtocolConstants.Magic);
        header.Version.Should().Be(MoonshineProtocolConstants.Version10);
        header.MessageType.Should().Be(MoonshineMessageType.FeedbackLossStats);
        header.PayloadSize.Should().Be((uint)MoonshineFeedbackCodec.LossStatsPayloadSize);
        header.SessionId.Should().Be(0xDEADBEEFCAFE);
        header.SequenceNumber.Should().Be(101);

        readPayload.StreamId.Should().Be(originalPayload.StreamId);
        readPayload.LastReceivedFrameIndex.Should().Be(originalPayload.LastReceivedFrameIndex);
        readPayload.PacketsReceived.Should().Be(originalPayload.PacketsReceived);
        readPayload.PacketsLost.Should().Be(originalPayload.PacketsLost);
        readPayload.PacketsRecoveredFec.Should().Be(originalPayload.PacketsRecoveredFec);
        readPayload.RoundTripTimeUs.Should().Be(originalPayload.RoundTripTimeUs);
        readPayload.JitterUs.Should().Be(originalPayload.JitterUs);
        readPayload.EstimatedBandwidthKbps.Should().Be(originalPayload.EstimatedBandwidthKbps);
    }

    [Fact]
    public void IdrRequest_WriteAndRead_RoundtripsSuccessfully()
    {
        var originalPayload = new MoonshineIdrRequestPayload
        {
            StreamId = 7,
            LastValidFrameIndex = 999,
            ReasonCode = 2 // Packet loss recovery failure
        };

        byte[] buffer = new byte[MoonshineFeedbackCodec.IdrRequestPacketSize];
        bool writeSuccess = MoonshineFeedbackCodec.TryWriteIdrRequest(
            in originalPayload,
            buffer,
            out int bytesWritten,
            sessionId: 0xCAFEBABEDEAD,
            sequenceNumber: 202);

        writeSuccess.Should().BeTrue();
        bytesWritten.Should().Be(MoonshineFeedbackCodec.IdrRequestPacketSize);

        MoonshineErrorCode readResult = MoonshineFeedbackCodec.TryReadIdrRequest(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineIdrRequestPayload readPayload);

        readResult.Should().Be(MoonshineErrorCode.Success);
        header.Magic.Should().Be(MoonshineProtocolConstants.Magic);
        header.MessageType.Should().Be(MoonshineMessageType.IdrRequest);
        header.PayloadSize.Should().Be((uint)MoonshineFeedbackCodec.IdrRequestPayloadSize);
        header.SessionId.Should().Be(0xCAFEBABEDEAD);
        header.SequenceNumber.Should().Be(202);

        readPayload.StreamId.Should().Be(originalPayload.StreamId);
        readPayload.LastValidFrameIndex.Should().Be(originalPayload.LastValidFrameIndex);
        readPayload.ReasonCode.Should().Be(originalPayload.ReasonCode);
    }

    [Fact]
    public void FeedbackCodec_BufferTooSmall_ReturnsFalseOrError()
    {
        var payload = new MoonshineFeedbackLossStatsPayload { StreamId = 1 };
        byte[] smallBuffer = new byte[10];

        bool writeSuccess = MoonshineFeedbackCodec.TryWriteLossStats(
            in payload,
            smallBuffer,
            out int bytesWritten);

        writeSuccess.Should().BeFalse();
        bytesWritten.Should().Be(0);

        MoonshineErrorCode readResult = MoonshineFeedbackCodec.TryReadLossStats(
            smallBuffer,
            out _,
            out _);

        readResult.Should().Be(MoonshineErrorCode.BufferTooSmall);
    }

    [Fact]
    public void FeedbackCodec_WrongMessageType_ReturnsMalformedHeader()
    {
        // Construct valid MSHN envelope but with wrong message type (VideoPacket instead of FeedbackLossStats)
        byte[] buffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: MoonshineFeedbackCodec.LossStatsPayloadSize,
            SequenceNumber: 1,
            SessionId: 1,
            TimestampUs: 1000);

        MoonshineProtocolCodec.TryWriteHeader(in header, buffer);

        MoonshineErrorCode readResult = MoonshineFeedbackCodec.TryReadLossStats(
            buffer,
            out _,
            out _);

        readResult.Should().Be(MoonshineErrorCode.MalformedHeader);
    }
}
