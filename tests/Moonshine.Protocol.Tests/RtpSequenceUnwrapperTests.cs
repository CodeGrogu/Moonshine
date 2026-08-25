using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

/// <summary>
/// Exhaustive unit test suite for RtpSequenceUnwrapper verifying monotonic unwrapping,
/// multi-epoch rollover handling, late retransmission recovery, boundary initialisation, and reset behaviour.
/// </summary>
public class RtpSequenceUnwrapperTests
{
    [Fact]
    public void Unwrap_MonotonicIncrease_ReturnsConsecutive64BitSequences()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        unwrapper.Unwrap(100).Should().Be(100);
        unwrapper.Unwrap(101).Should().Be(101);
        unwrapper.Unwrap(102).Should().Be(102);
    }

    [Fact]
    public void Unwrap_Across16BitOverflowBoundary_IncrementsEpochCorrectly()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        unwrapper.Unwrap(65534).Should().Be(65534);
        unwrapper.Unwrap(65535).Should().Be(65535);
        unwrapper.Unwrap(0).Should().Be(65536);
        unwrapper.Unwrap(1).Should().Be(65537);
    }

    [Fact]
    public void Unwrap_MinorOutOfOrderPackets_PreservesCorrectEpoch()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        unwrapper.Unwrap(65535).Should().Be(65535);
        unwrapper.Unwrap(0).Should().Be(65536);
        // Late arriving packet from previous epoch
        unwrapper.Unwrap(65534).Should().Be(65534);
        // Next packet from current epoch
        unwrapper.Unwrap(1).Should().Be(65537);
    }

    [Fact]
    public void Unwrap_MultiEpochSustainedRollover_Maintains64BitContinuity()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        // Initialise at 65534
        unwrapper.Unwrap(65534).Should().Be(65534UL);

        // Simulate 100 consecutive 16-bit rollovers with valid RFC 1982 forward step intervals
        for (ulong epoch = 0; epoch < 100; epoch++)
        {
            ulong currentEpochBase = epoch * 65536UL;
            ulong nextEpochBase = (epoch + 1) * 65536UL;

            unwrapper.Unwrap(65535).Should().Be(currentEpochBase + 65535UL);
            unwrapper.Unwrap(0).Should().Be(nextEpochBase + 0UL);
            unwrapper.Unwrap(1).Should().Be(nextEpochBase + 1UL);

            // Step forward through mid-epoch
            unwrapper.Unwrap(32768).Should().Be(nextEpochBase + 32768UL);
            unwrapper.Unwrap(65534).Should().Be(nextEpochBase + 65534UL);
        }
    }

    [Fact]
    public void Unwrap_JitterAndDuplicateRetransmissionNearBoundary_ResolvesDeterministicEpochs()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        // 1. Packet 65534
        unwrapper.Unwrap(65534).Should().Be(65534);

        // 2. Packet 65535
        unwrapper.Unwrap(65535).Should().Be(65535);

        // 3. Packet 0 (Rollover)
        unwrapper.Unwrap(0).Should().Be(65536);

        // 4. Retransmission of packet 65535 (from previous epoch)
        unwrapper.Unwrap(65535).Should().Be(65535);

        // 5. Out-of-order packet 2 (arrived before 1)
        unwrapper.Unwrap(2).Should().Be(65538);

        // 6. Packet 1 (arrived late in current epoch)
        unwrapper.Unwrap(1).Should().Be(65537);

        // 7. Duplicate of packet 0 (from current epoch)
        unwrapper.Unwrap(0).Should().Be(65536);

        // 8. Subsequent monotonic progression
        unwrapper.Unwrap(3).Should().Be(65539);
    }

    [Fact]
    public void Unwrap_InitialisedAtMax16Bit_TransitionsSmoothly()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        // Stream begins with initial sequence 65535
        unwrapper.Unwrap(65535).Should().Be(65535);
        unwrapper.Unwrap(0).Should().Be(65536);
        unwrapper.Unwrap(1).Should().Be(65537);
        unwrapper.Unwrap(2).Should().Be(65538);
    }

    [Fact]
    public void Unwrap_InitialisedAtHalfRange_TransitionsSmoothly()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        unwrapper.Unwrap(32768).Should().Be(32768);
        unwrapper.Unwrap(32769).Should().Be(32769);
        unwrapper.Unwrap(32767).Should().Be(32767); // Late packet
        unwrapper.Unwrap(32770).Should().Be(32770);
    }

    [Fact]
    public void Unwrap_Reset_RestoresCleanInitialState()
    {
        var unwrapper = new RtpSequenceUnwrapper();

        unwrapper.Unwrap(65535).Should().Be(65535);
        unwrapper.Unwrap(0).Should().Be(65536);

        // Reset
        unwrapper.Reset();

        // New stream beginning at sequence 10
        unwrapper.Unwrap(10).Should().Be(10);
        unwrapper.Unwrap(11).Should().Be(11);
    }
}
