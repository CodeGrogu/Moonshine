using FluentAssertions;
using Moonshine.Core.Feedback;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineFeedbackTests
{
    [Fact]
    public void FeedbackReporter_RecordPacketsAndBuildFeedback_CalculatesAccurateMetrics()
    {
        using var reporter = new MoonshineFeedbackReporter(
            streamId: 1,
            sessionId: 0x1234567890ABCDEFUL,
            reportIntervalMs: 50);

        reporter.UpdateRtt(7500); // 7.5 ms
        reporter.UpdateQueueDepth(3);

        // Record a sequence of packets
        for (ulong frame = 1; frame <= 10; frame++)
        {
            for (uint pkt = 0; pkt < 5; pkt++)
            {
                ulong senderTimeUs = frame * 16666 + pkt * 100;
                reporter.RecordPacketReceived(
                    frameIndex: frame,
                    packetBytes: 1188,
                    senderTimestampUs: senderTimeUs,
                    isCompleteFrame: pkt == 4);
            }
        }

        reporter.RecordPacketLost(lostCount: 4);
        reporter.RecordPacketRecoveredFec(recoveredCount: 3);

        byte[] buffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];
        bool success = reporter.TryBuildFeedbackPacket(buffer, out int written);

        success.Should().BeTrue();
        written.Should().Be(MoonshineFeedbackCodec.LossStatsPacketSize);

        MoonshineErrorCode err = MoonshineFeedbackCodec.TryReadLossStats(
            buffer,
            out MoonshinePacketHeader header,
            out MoonshineFeedbackLossStatsPayload payload);

        err.Should().Be(MoonshineErrorCode.Success);
        header.SessionId.Should().Be(0x1234567890ABCDEFUL);
        payload.StreamId.Should().Be(1);
        payload.LastReceivedFrameIndex.Should().Be(10);
        payload.PacketsReceived.Should().Be(50);
        payload.PacketsLost.Should().Be(4);
        payload.PacketsRecoveredFec.Should().Be(3);
        payload.RoundTripTimeUs.Should().Be(7500);
        reporter.ReceiveQueueDepth.Should().Be(3);
    }

    [Fact]
    public void FeedbackReporter_ImmediateIdrRequest_EmitsValidDatagram()
    {
        byte[]? receivedDatagram = null;
        using var reporter = new MoonshineFeedbackReporter(
            streamId: 2,
            sessionId: 0xCAFE,
            reportIntervalMs: 100,
            sink: datagram =>
            {
                receivedDatagram = datagram.ToArray();
            });

        bool sent = reporter.SendIdrRequest(reasonCode: 3);
        sent.Should().BeTrue();
        receivedDatagram.Should().NotBeNull();

        MoonshineErrorCode err = MoonshineFeedbackCodec.TryReadIdrRequest(
            receivedDatagram!,
            out MoonshinePacketHeader header,
            out MoonshineIdrRequestPayload payload);

        err.Should().Be(MoonshineErrorCode.Success);
        header.MessageType.Should().Be(MoonshineMessageType.IdrRequest);
        header.SessionId.Should().Be(0xCAFE);
        payload.StreamId.Should().Be(2);
        payload.ReasonCode.Should().Be(3);
    }

    [Fact]
    public async Task FeedbackReporter_PeriodicLoop_EmitsReportsAtConfiguredCadence()
    {
        int reportCount = 0;
        using var reporter = new MoonshineFeedbackReporter(
            streamId: 3,
            sessionId: 0x999,
            reportIntervalMs: 25,
            sink: datagram =>
            {
                if (datagram.Length == MoonshineFeedbackCodec.LossStatsPacketSize)
                {
                    Interlocked.Increment(ref reportCount);
                }
            });

        // Wait ~120ms to allow ~4 reports to be generated
        await Task.Delay(120);

        reportCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void FeedbackReporter_DoubleDispose_IsSafe()
    {
        var reporter = new MoonshineFeedbackReporter(streamId: 4, sessionId: 0x111);
        reporter.Dispose();
        var action = () => reporter.Dispose();
        action.Should().NotThrow();
    }
}
