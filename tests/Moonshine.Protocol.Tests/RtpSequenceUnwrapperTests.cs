using FluentAssertions;
using Moonshine.Protocol.RTP;
using Xunit;

namespace Moonshine.Protocol.Tests;

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
    }
}
