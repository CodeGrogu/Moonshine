using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Protocol.Tests;

/// <summary>
/// Exhaustive unit test suite verifying RFC 1982 modular sequence arithmetic parity,
/// half-range ambiguity resolution, rollover transitions, and signed modular distance calculations
/// across 16-bit, 32-bit, and 64-bit sequence spaces.
/// </summary>
public class MoonshineModularSequenceArithmeticTests
{
    // ========================================================================
    // 16-Bit Sequence Space Tests (2^16 = 65,536, Half-Range = 32,768)
    // ========================================================================

    [Fact]
    public void Sequence16_MonotonicIncrements_EvaluatesCorrectly()
    {
        MoonshineProtocolCodec.IsNewerSequence16(1, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(2, 1).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(32767, 32766).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(65534, 65533).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(65535, 65534).Should().BeTrue();
    }

    [Fact]
    public void Sequence16_IdenticalValues_EvaluatesAsNotNewer()
    {
        MoonshineProtocolCodec.IsNewerSequence16(0, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(1, 1).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(32768, 32768).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(65534, 65534).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(65535, 65535).Should().BeFalse();
    }

    [Fact]
    public void Sequence16_RolloverBoundaries_WrapsDeterministically()
    {
        // MAX -> 0 transition (0 is newer than 65535)
        MoonshineProtocolCodec.IsNewerSequence16(0, 65535).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(1, 65535).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(2, 65535).Should().BeTrue();

        // 0 -> MAX transition (65535 is stale/older than 0)
        MoonshineProtocolCodec.IsNewerSequence16(65535, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(65534, 0).Should().BeFalse();

        // MAX-1 -> 0 transition
        MoonshineProtocolCodec.IsNewerSequence16(0, 65534).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(65534, 0).Should().BeFalse();
    }

    [Fact]
    public void Sequence16_HalfRangeAmbiguity_FollowsRfc1982Specification()
    {
        // Distance exactly 2^(16-1) = 32768 (0x8000): RFC 1982 defines neither as newer (undefined/false)
        MoonshineProtocolCodec.IsNewerSequence16(32768, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(0, 32768).Should().BeFalse();

        // Arbitrary base half-range ambiguity (base = 10000, candidate = 42768)
        ushort baseSeq = 10000;
        ushort halfSeq = unchecked((ushort)(baseSeq + 32768));
        MoonshineProtocolCodec.IsNewerSequence16(halfSeq, baseSeq).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(baseSeq, halfSeq).Should().BeFalse();

        // Just below half-range: 32767 is newer than 0
        MoonshineProtocolCodec.IsNewerSequence16(32767, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence16(0, 32767).Should().BeFalse();

        // Just above half-range: 32769 is older than 0 (i.e. 0 is newer than 32769)
        MoonshineProtocolCodec.IsNewerSequence16(32769, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence16(0, 32769).Should().BeTrue();
    }

    [Fact]
    public void Sequence16_SignedDistanceCalculations_ReturnsExactDeltas()
    {
        MoonshineProtocolCodec.SequenceDistance16(1, 0).Should().Be(1);
        MoonshineProtocolCodec.SequenceDistance16(0, 1).Should().Be(-1);
        MoonshineProtocolCodec.SequenceDistance16(0, 65535).Should().Be(1);
        MoonshineProtocolCodec.SequenceDistance16(65535, 0).Should().Be(-1);
        MoonshineProtocolCodec.SequenceDistance16(10, 65530).Should().Be(16);
        MoonshineProtocolCodec.SequenceDistance16(65530, 10).Should().Be(-16);
        MoonshineProtocolCodec.SequenceDistance16(32767, 0).Should().Be(32767);
        MoonshineProtocolCodec.SequenceDistance16(32768, 0).Should().Be(-32768);
        MoonshineProtocolCodec.SequenceDistance16(32769, 0).Should().Be(-32767);
    }

    [Fact]
    public void Sequence16_OutOfOrderJitterAndRetransmission_ClassifiesAccurately()
    {
        ushort previous = 65534;

        // Arrival 1: 65535 (newer)
        MoonshineProtocolCodec.IsNewerSequence16(65535, previous).Should().BeTrue();
        previous = 65535;

        // Arrival 2: 0 (wrapped newer)
        MoonshineProtocolCodec.IsNewerSequence16(0, previous).Should().BeTrue();
        previous = 0;

        // Arrival 3: 65535 (retransmitted packet from previous epoch, stale)
        MoonshineProtocolCodec.IsNewerSequence16(65535, previous).Should().BeFalse();

        // Arrival 4: 1 (newer)
        MoonshineProtocolCodec.IsNewerSequence16(1, previous).Should().BeTrue();
        previous = 1;

        // Arrival 5: 0 (delayed duplicate from current epoch, stale)
        MoonshineProtocolCodec.IsNewerSequence16(0, previous).Should().BeFalse();

        // Arrival 6: 2 (newer)
        MoonshineProtocolCodec.IsNewerSequence16(2, previous).Should().BeTrue();
    }

    // ========================================================================
    // 32-Bit Sequence Space Tests (2^32 = 4,294,967,296, Half-Range = 2,147,483,648)
    // ========================================================================

    [Fact]
    public void Sequence32_MonotonicIncrements_EvaluatesCorrectly()
    {
        MoonshineProtocolCodec.IsNewerSequence32(1, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(2, 1).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(2147483647U, 2147483646U).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(4294967294U, 4294967293U).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(4294967295U, 4294967294U).Should().BeTrue();

        // Verify IsNewerSequence alias parity
        MoonshineProtocolCodec.IsNewerSequence(1, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence(4294967295U, 4294967294U).Should().BeTrue();
    }

    [Fact]
    public void Sequence32_IdenticalValues_EvaluatesAsNotNewer()
    {
        MoonshineProtocolCodec.IsNewerSequence32(0, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(1, 1).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(0x80000000U, 0x80000000U).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(4294967294U, 4294967294U).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(4294967295U, 4294967295U).Should().BeFalse();
    }

    [Fact]
    public void Sequence32_RolloverBoundaries_WrapsDeterministically()
    {
        // MAX -> 0 transition (0 is newer than 4294967295)
        MoonshineProtocolCodec.IsNewerSequence32(0, 4294967295U).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(1, 4294967295U).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(2, 4294967295U).Should().BeTrue();

        // 0 -> MAX transition (4294967295 is stale/older than 0)
        MoonshineProtocolCodec.IsNewerSequence32(4294967295U, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(4294967294U, 0).Should().BeFalse();

        // MAX-1 -> 0 transition
        MoonshineProtocolCodec.IsNewerSequence32(0, 4294967294U).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(4294967294U, 0).Should().BeFalse();
    }

    [Fact]
    public void Sequence32_HalfRangeAmbiguity_FollowsRfc1982Specification()
    {
        // Distance exactly 2^(32-1) = 2147483648 (0x80000000): RFC 1982 defines neither as newer
        MoonshineProtocolCodec.IsNewerSequence32(0x80000000U, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(0, 0x80000000U).Should().BeFalse();

        // Arbitrary base half-range ambiguity (base = 500000, candidate = 500000 + 0x80000000)
        uint baseSeq = 500000U;
        uint halfSeq = unchecked(baseSeq + 0x80000000U);
        MoonshineProtocolCodec.IsNewerSequence32(halfSeq, baseSeq).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(baseSeq, halfSeq).Should().BeFalse();

        // Just below half-range: 2147483647 (0x7FFFFFFF) is newer than 0
        MoonshineProtocolCodec.IsNewerSequence32(0x7FFFFFFFU, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence32(0, 0x7FFFFFFFU).Should().BeFalse();

        // Just above half-range: 2147483649 (0x80000001) is older than 0 (i.e. 0 is newer than 2147483649)
        MoonshineProtocolCodec.IsNewerSequence32(0x80000001U, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence32(0, 0x80000001U).Should().BeTrue();
    }

    [Fact]
    public void Sequence32_SignedDistanceCalculations_ReturnsExactDeltas()
    {
        MoonshineProtocolCodec.SequenceDistance32(1, 0).Should().Be(1);
        MoonshineProtocolCodec.SequenceDistance32(0, 1).Should().Be(-1);
        MoonshineProtocolCodec.SequenceDistance32(0, uint.MaxValue).Should().Be(1);
        MoonshineProtocolCodec.SequenceDistance32(uint.MaxValue, 0).Should().Be(-1);
        MoonshineProtocolCodec.SequenceDistance32(50, uint.MaxValue - 49).Should().Be(100);
        MoonshineProtocolCodec.SequenceDistance32(uint.MaxValue - 49, 50).Should().Be(-100);
        MoonshineProtocolCodec.SequenceDistance32(0x7FFFFFFFU, 0).Should().Be(int.MaxValue);
        MoonshineProtocolCodec.SequenceDistance32(0x80000000U, 0).Should().Be(int.MinValue);
        MoonshineProtocolCodec.SequenceDistance32(0x80000001U, 0).Should().Be(int.MinValue + 1);
    }

    [Fact]
    public void Sequence32_OutOfOrderJitterAndRetransmission_ClassifiesAccurately()
    {
        uint previous = 0xFFFFFFFEU;

        // Arrival 1: 0xFFFFFFFF (newer)
        MoonshineProtocolCodec.IsNewerSequence32(0xFFFFFFFFU, previous).Should().BeTrue();
        previous = 0xFFFFFFFFU;

        // Arrival 2: 0 (wrapped newer)
        MoonshineProtocolCodec.IsNewerSequence32(0x00000000U, previous).Should().BeTrue();
        previous = 0x00000000U;

        // Arrival 3: 0xFFFFFFFF (retransmission from previous epoch, stale)
        MoonshineProtocolCodec.IsNewerSequence32(0xFFFFFFFFU, previous).Should().BeFalse();

        // Arrival 4: 1 (newer)
        MoonshineProtocolCodec.IsNewerSequence32(0x00000001U, previous).Should().BeTrue();
        previous = 0x00000001U;

        // Arrival 5: 0 (delayed duplicate from current epoch, stale)
        MoonshineProtocolCodec.IsNewerSequence32(0x00000000U, previous).Should().BeFalse();

        // Arrival 6: 2 (newer)
        MoonshineProtocolCodec.IsNewerSequence32(0x00000002U, previous).Should().BeTrue();
    }

    // ========================================================================
    // 64-Bit Sequence Space Tests (2^64, Half-Range = 2^63)
    // ========================================================================

    [Fact]
    public void Sequence64_MonotonicIncrements_EvaluatesCorrectly()
    {
        MoonshineProtocolCodec.IsNewerSequence64(1, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(2, 1).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(0x7FFFFFFFFFFFFFFEUL, 0x7FFFFFFFFFFFFFFDUL).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(0xFFFFFFFFFFFFFFFEUL, 0xFFFFFFFFFFFFFFFDUL).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(0xFFFFFFFFFFFFFFFFUL, 0xFFFFFFFFFFFFFFFEUL).Should().BeTrue();

        // Verify IsNewerFrameIndex alias parity
        MoonshineProtocolCodec.IsNewerFrameIndex(1, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerFrameIndex(0xFFFFFFFFFFFFFFFFUL, 0xFFFFFFFFFFFFFFFEUL).Should().BeTrue();
    }

    [Fact]
    public void Sequence64_IdenticalValues_EvaluatesAsNotNewer()
    {
        MoonshineProtocolCodec.IsNewerSequence64(0, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(1, 1).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(0x8000000000000000UL, 0x8000000000000000UL).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(0xFFFFFFFFFFFFFFFEUL, 0xFFFFFFFFFFFFFFFEUL).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(0xFFFFFFFFFFFFFFFFUL, 0xFFFFFFFFFFFFFFFFUL).Should().BeFalse();
    }

    [Fact]
    public void Sequence64_RolloverBoundaries_WrapsDeterministically()
    {
        // MAX -> 0 transition (0 is newer than ulong.MaxValue)
        MoonshineProtocolCodec.IsNewerSequence64(0, ulong.MaxValue).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(1, ulong.MaxValue).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(2, ulong.MaxValue).Should().BeTrue();

        // 0 -> MAX transition (ulong.MaxValue is stale/older than 0)
        MoonshineProtocolCodec.IsNewerSequence64(ulong.MaxValue, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(ulong.MaxValue - 1, 0).Should().BeFalse();

        // MAX-1 -> 0 transition
        MoonshineProtocolCodec.IsNewerSequence64(0, ulong.MaxValue - 1).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(ulong.MaxValue - 1, 0).Should().BeFalse();
    }

    [Fact]
    public void Sequence64_HalfRangeAmbiguity_FollowsRfc1982Specification()
    {
        // Distance exactly 2^(64-1) = 0x8000000000000000UL: RFC 1982 defines neither as newer
        MoonshineProtocolCodec.IsNewerSequence64(0x8000000000000000UL, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(0, 0x8000000000000000UL).Should().BeFalse();

        // Arbitrary base half-range ambiguity
        ulong baseSeq = 1_000_000UL;
        ulong halfSeq = unchecked(baseSeq + 0x8000000000000000UL);
        MoonshineProtocolCodec.IsNewerSequence64(halfSeq, baseSeq).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(baseSeq, halfSeq).Should().BeFalse();

        // Just below half-range: 0x7FFFFFFFFFFFFFFFUL is newer than 0
        MoonshineProtocolCodec.IsNewerSequence64(0x7FFFFFFFFFFFFFFFUL, 0).Should().BeTrue();
        MoonshineProtocolCodec.IsNewerSequence64(0, 0x7FFFFFFFFFFFFFFFUL).Should().BeFalse();

        // Just above half-range: 0x8000000000000001UL is older than 0 (i.e. 0 is newer)
        MoonshineProtocolCodec.IsNewerSequence64(0x8000000000000001UL, 0).Should().BeFalse();
        MoonshineProtocolCodec.IsNewerSequence64(0, 0x8000000000000001UL).Should().BeTrue();
    }

    [Fact]
    public void Sequence64_SignedDistanceCalculations_ReturnsExactDeltas()
    {
        MoonshineProtocolCodec.SequenceDistance64(1, 0).Should().Be(1L);
        MoonshineProtocolCodec.SequenceDistance64(0, 1).Should().Be(-1L);
        MoonshineProtocolCodec.SequenceDistance64(0, ulong.MaxValue).Should().Be(1L);
        MoonshineProtocolCodec.SequenceDistance64(ulong.MaxValue, 0).Should().Be(-1L);
        MoonshineProtocolCodec.SequenceDistance64(100, ulong.MaxValue - 99).Should().Be(200L);
        MoonshineProtocolCodec.SequenceDistance64(ulong.MaxValue - 99, 100).Should().Be(-200L);
        MoonshineProtocolCodec.SequenceDistance64(0x7FFFFFFFFFFFFFFFUL, 0).Should().Be(long.MaxValue);
        MoonshineProtocolCodec.SequenceDistance64(0x8000000000000000UL, 0).Should().Be(long.MinValue);
        MoonshineProtocolCodec.SequenceDistance64(0x8000000000000001UL, 0).Should().Be(long.MinValue + 1);
    }
}
