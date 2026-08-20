using FluentAssertions;
using Moonshine.Core.Congestion;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Core.Tests;

public class CongestionControllerTests
{
    [Fact]
    public void CongestionController_CleanNetwork_AdditiveIncreaseUpToMax()
    {
        uint currentBitrate = 0;
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            minBitrateKbps: 10000,
            maxBitrateKbps: 55000,
            onBitrateChanged: b => currentBitrate = b
        );

        var cleanStats = new RtcpLossStatsPacket(1, 1000, 0, 0, 1000, 50);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(52000);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(54000);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(55000); // capped at max
    }

    [Fact]
    public void CongestionController_ModerateLoss_DecreasesBitrate10Percent()
    {
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            minBitrateKbps: 10000,
            maxBitrateKbps: 100000
        );

        // 3% unrecoverable loss
        var moderateLoss = new RtcpLossStatsPacket(1, 970, 30, 0, 1000, 100);

        controller.ProcessFeedback(moderateLoss);
        controller.CurrentBitrateKbps.Should().Be(45000); // 50000 * 0.90
        controller.Metrics.CongestionEventsCount.Should().Be(1);
    }

    [Fact]
    public void CongestionController_SevereLoss_DecreasesBitrate30PercentDownToMin()
    {
        var controller = new CongestionController(
            initialBitrateKbps: 20000,
            minBitrateKbps: 15000,
            maxBitrateKbps: 100000
        );

        // 10% unrecoverable loss
        var severeLoss = new RtcpLossStatsPacket(1, 900, 100, 0, 1000, 100);

        controller.ProcessFeedback(severeLoss);
        controller.CurrentBitrateKbps.Should().Be(15000); // 20000 * 0.70 = 14000 -> clamped to 15000 min
    }

    [Fact]
    public void CongestionController_MultiPacketUnrecoverableLoss_TriggersIdrRequest()
    {
        bool idrTriggered = false;
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            onIdrRequested: () => idrTriggered = true
        );

        // 10 lost packets, 0 recovered
        var lossStats = new RtcpLossStatsPacket(1, 500, 10, 0, 510, 100);
        controller.ProcessFeedback(lossStats);

        idrTriggered.Should().BeTrue();
        controller.Metrics.IdrRequestsSent.Should().Be(1);
    }

    [Fact]
    public void CongestionController_RttSmoothing_CalculatesEma()
    {
        var controller = new CongestionController(initialBitrateKbps: 50000);
        var stats = new RtcpLossStatsPacket(1, 1000, 0, 0, 1000, 50);

        controller.ProcessFeedback(stats, rttMs: 10.0);
        controller.MeasuredRttMs.Should().Be(10.0);

        controller.ProcessFeedback(stats, rttMs: 20.0);
        controller.MeasuredRttMs.Should().BeApproximately(13.0, 0.1);
    }
}
