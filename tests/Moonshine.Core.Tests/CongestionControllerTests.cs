using FluentAssertions;
using Moonshine.Core.Congestion;
using Moonshine.Protocol.Contracts;
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
            hysteresisHoldMs: 0, // 0 for immediate step test
            onBitrateChanged: b => currentBitrate = b
        );

        var cleanStats = new RtcpLossStatsPacket(1, 1000, 0, 0, 1000, 50);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(51000);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(52000);

        controller.ProcessFeedback(cleanStats, rttMs: 5.0);
        controller.CurrentBitrateKbps.Should().Be(53000);
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

    [Fact]
    public void CongestionController_MoonshineNativeFeedback_AdaptsBitrateAndJitter()
    {
        uint lastBitrate = 0;
        uint lastPacing = 0;
        var controller = new CongestionController(
            initialBitrateKbps: 60000,
            minBitrateKbps: 10000,
            maxBitrateKbps: 100000,
            onBitrateChanged: b => lastBitrate = b,
            onPacingChanged: p => lastPacing = p);

        var nativeStats = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 250,
            PacketsReceived = 1000,
            PacketsLost = 80, // 8% loss
            PacketsRecoveredFec = 10,
            RoundTripTimeUs = 12000,
            JitterUs = 800,
            EstimatedBandwidthKbps = 45000,
            ReceiveQueueDepth = 2
        };

        controller.ProcessFeedback(in nativeStats);

        controller.CurrentBitrateKbps.Should().Be(42000); // 60000 * 0.70
        controller.MeasuredRttMs.Should().Be(12.0);
        controller.SmoothedJitterUs.Should().Be(800.0);
        controller.EffectiveThroughputKbps.Should().Be(45000);
        controller.ClientQueueDepth.Should().Be(2);
    }

    [Fact]
    public void CongestionController_QueueDepthBloat_BacksOffBitrateAndIncreasesPacing()
    {
        uint pacingReported = 0;
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            onPacingChanged: p => pacingReported = p);

        var bloatedStats = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 1000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            RoundTripTimeUs = 6000,
            JitterUs = 200,
            EstimatedBandwidthKbps = 50000,
            ReceiveQueueDepth = 10 // High queue depth (> 8)
        };

        controller.ProcessFeedback(in bloatedStats);

        // Bitrate should back off due to queue bloat
        controller.CurrentBitrateKbps.Should().Be(42500); // 50000 * 0.85
        controller.PacingAdjustmentUs.Should().Be(2500); // 10 * 250
        pacingReported.Should().Be(2500);
    }

    [Fact]
    public void CongestionController_RealTrafficTrace_StepDownAndRecovery()
    {
        var bitrates = new List<uint>();
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            minBitrateKbps: 10000,
            maxBitrateKbps: 80000,
            hysteresisHoldMs: 0,
            onBitrateChanged: b => bitrates.Add(b));

        // Phase 1: Clean network for 5 intervals
        for (int i = 0; i < 5; i++)
        {
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                PacketsReceived = 1000,
                PacketsLost = 0,
                RoundTripTimeUs = 5000,
                ReceiveQueueDepth = 1
            });
        }

        controller.CurrentBitrateKbps.Should().Be(55000);

        // Phase 2: Sudden network congestion and 10% packet loss for 3 intervals
        for (int i = 0; i < 3; i++)
        {
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                PacketsReceived = 900,
                PacketsLost = 100,
                RoundTripTimeUs = 25000,
                ReceiveQueueDepth = 6
            });
        }

        controller.CurrentBitrateKbps.Should().BeLessThan(40000);
        controller.PacingAdjustmentUs.Should().BeGreaterThan(0);

        // Phase 3: Network clears and stabilizes for 35 intervals
        for (int i = 0; i < 35; i++)
        {
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                PacketsReceived = 1000,
                PacketsLost = 0,
                RoundTripTimeUs = 5000,
                ReceiveQueueDepth = 1
            });
        }

        // Recovering upwards
        controller.CurrentBitrateKbps.Should().BeGreaterThan(45000);
        controller.PacingAdjustmentUs.Should().Be(0);
    }

    [Fact]
    public void CongestionController_ProcessIdrRequest_TriggersCallback()
    {
        bool idrRequested = false;
        var controller = new CongestionController(
            onIdrRequested: () => idrRequested = true);

        var idrPayload = new MoonshineIdrRequestPayload
        {
            StreamId = 1,
            LastValidFrameIndex = 500,
            ReasonCode = 1
        };

        controller.ProcessIdrRequest(in idrPayload);

        idrRequested.Should().BeTrue();
        controller.IdrRequestsSent.Should().Be(1);
    }
}
