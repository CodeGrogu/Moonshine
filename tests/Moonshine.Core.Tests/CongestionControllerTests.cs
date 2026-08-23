using FluentAssertions;
using Moonshine.Core.Congestion;
using Moonshine.Protocol.Contracts;
#if MOONSHINE_LEGACY_INTEROP
using Moonshine.Protocol.RTP;
#endif
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

        var cleanStats = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 1000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 1000,
            JitterUs = 50,
            RoundTripTimeUs = 5000, // 5.0 ms
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in cleanStats);
        controller.CurrentBitrateKbps.Should().Be(51000);

        var cleanStats2 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 2000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 2000,
            JitterUs = 50,
            RoundTripTimeUs = 5000,
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in cleanStats2);
        controller.CurrentBitrateKbps.Should().Be(52000);

        var cleanStats3 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 3000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 3000,
            JitterUs = 50,
            RoundTripTimeUs = 5000,
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in cleanStats3);
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
        var moderateLoss = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 970,
            PacketsLost = 30,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 1000,
            JitterUs = 100,
            RoundTripTimeUs = 0,
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in moderateLoss);
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
        var severeLoss = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 900,
            PacketsLost = 100,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 1000,
            JitterUs = 100,
            RoundTripTimeUs = 0,
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in severeLoss);
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
        var lossStats = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 500,
            PacketsLost = 10,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 510,
            JitterUs = 100,
            RoundTripTimeUs = 0,
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };
        controller.ProcessFeedback(in lossStats);

        idrTriggered.Should().BeTrue();
        controller.Metrics.IdrRequestsSent.Should().Be(1);
    }

    [Fact]
    public void CongestionController_RttSmoothing_CalculatesEma()
    {
        var controller = new CongestionController(initialBitrateKbps: 50000);
        var stats1 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 1000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 1000,
            JitterUs = 50,
            RoundTripTimeUs = 10000, // 10.0 ms
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in stats1);
        controller.MeasuredRttMs.Should().Be(10.0);

        var stats2 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            PacketsReceived = 2000,
            PacketsLost = 0,
            PacketsRecoveredFec = 0,
            LastReceivedFrameIndex = 2000,
            JitterUs = 50,
            RoundTripTimeUs = 20000, // 20.0 ms
            EstimatedBandwidthKbps = 0,
            ReceiveQueueDepth = 0
        };

        controller.ProcessFeedback(in stats2);
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

        ulong frameIndex = 1;
        uint totalReceived = 0;
        uint totalLost = 0;

        // Phase 1: Clean network for 5 intervals
        for (int i = 0; i < 5; i++)
        {
            totalReceived += 1000;
            frameIndex += 10;
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                StreamId = 1,
                LastReceivedFrameIndex = frameIndex,
                PacketsReceived = totalReceived,
                PacketsLost = totalLost,
                RoundTripTimeUs = 5000,
                ReceiveQueueDepth = 1
            });
        }

        controller.CurrentBitrateKbps.Should().Be(55000);

        // Phase 2: Sudden network congestion and 10% packet loss for 3 intervals
        for (int i = 0; i < 3; i++)
        {
            totalReceived += 900;
            totalLost += 100;
            frameIndex += 10;
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                StreamId = 1,
                LastReceivedFrameIndex = frameIndex,
                PacketsReceived = totalReceived,
                PacketsLost = totalLost,
                RoundTripTimeUs = 25000,
                ReceiveQueueDepth = 6
            });
        }

        controller.CurrentBitrateKbps.Should().BeLessThan(40000);
        controller.PacingAdjustmentUs.Should().BeGreaterThan(0);

        // Phase 3: Network clears and stabilizes for 35 intervals
        for (int i = 0; i < 35; i++)
        {
            totalReceived += 1000;
            frameIndex += 10;
            controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
            {
                StreamId = 1,
                LastReceivedFrameIndex = frameIndex,
                PacketsReceived = totalReceived,
                PacketsLost = totalLost,
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

    [Fact]
    public void CongestionController_OutOfOrderStaleFeedback_IsDiscardedSafely()
    {
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            hysteresisHoldMs: 0);

        // Report 1: Frame 100, 1000 packets received, 0 lost -> clean network
        controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 100,
            PacketsReceived = 1000,
            PacketsLost = 0,
            RoundTripTimeUs = 5000,
            ReceiveQueueDepth = 0
        });

        uint bitrateAfterClean = controller.CurrentBitrateKbps;
        bitrateAfterClean.Should().BeGreaterThanOrEqualTo(50000);

        // Report 2: Stale / Out-of-Order report from Frame 80 delayed in network with 50% simulated loss
        controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 80, // Stale!
            PacketsReceived = 800,
            PacketsLost = 400,
            RoundTripTimeUs = 250000,
            ReceiveQueueDepth = 10
        });

        // Stale report must be discarded without degrading current bitrate or inflating queue depth
        controller.CurrentBitrateKbps.Should().Be(bitrateAfterClean);
        controller.CongestionEventsCount.Should().Be(0);

        // Report 3: In-order newer report Frame 110 continues cleanly
        controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 110,
            PacketsReceived = 1100,
            PacketsLost = 0,
            RoundTripTimeUs = 5000,
            ReceiveQueueDepth = 0
        });

        controller.CurrentBitrateKbps.Should().BeGreaterThanOrEqualTo(bitrateAfterClean);
    }

    [Fact]
    public void CongestionController_StreamChangeAndSessionReset_ReanchorsBaseline()
    {
        var controller = new CongestionController(
            initialBitrateKbps: 50000,
            hysteresisHoldMs: 0);

        // Stream 1: High packet counters
        controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 100,
            LastReceivedFrameIndex = 5000,
            PacketsReceived = 50000,
            PacketsLost = 10,
            RoundTripTimeUs = 5000,
            ReceiveQueueDepth = 0
        });

        // Stream 2: Fresh session resets counters to low initial values
        controller.ProcessFeedback(new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 101, // New stream!
            LastReceivedFrameIndex = 1,
            PacketsReceived = 10,
            PacketsLost = 0,
            RoundTripTimeUs = 4000,
            ReceiveQueueDepth = 0
        });

        controller.CurrentBitrateKbps.Should().BeGreaterThanOrEqualTo(50000);
        controller.CongestionEventsCount.Should().Be(0);
    }
}

