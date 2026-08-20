using System.Buffers;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class NativeMemoryOwnerTests
{
    [Fact]
    public unsafe void NativeMemoryOwner_AllocatesAndAllowsZeroCopyMemoryAccess()
    {
        using var owner = new NativeMemoryOwner(1024);

        owner.Memory.Length.Should().Be(1024);
        var isNonNull = owner.Pointer != null;
        isNonNull.Should().BeTrue();

        var span = owner.Memory.Span;
        span[0] = 0xAA;
        span[1023] = 0x55;

        byte* rawPtr = owner.Pointer;
        rawPtr[0].Should().Be(0xAA);
        rawPtr[1023].Should().Be(0x55);
    }

    [Fact]
    public void NativeMemoryOwner_DoubleDispose_IsIdempotentAndSafe()
    {
        var owner = new NativeMemoryOwner(512);
        ((IDisposable)owner).Dispose();
        // Second dispose should be safe and idempotent
        var act = () => ((IDisposable)owner).Dispose();
        act.Should().NotThrow();
    }
}
