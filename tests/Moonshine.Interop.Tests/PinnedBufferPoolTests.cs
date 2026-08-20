using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class PinnedBufferPoolTests
{
    [Fact]
    public unsafe void TryRent_ValidPool_ReturnsNonNullAlignedMemory()
    {
        using var pool = new PinnedBufferPool(slotCount: 16, slotSize: 1024);

        bool rented = pool.TryRent(out int slotIndex, out byte* ptr, out var span);

        rented.Should().BeTrue();
        slotIndex.Should().BeGreaterThanOrEqualTo(0);
        ((IntPtr)ptr).Should().NotBe(IntPtr.Zero);
        span.Length.Should().Be(1024);

        // Verify write safety
        span[0] = 0xAA;
        span[1023] = 0xBB;
        (*ptr).Should().Be(0xAA);
    }

    [Fact]
    public unsafe void TryRent_ExhaustPool_ReturnsFalse()
    {
        using var pool = new PinnedBufferPool(slotCount: 2, slotSize: 512);

        pool.TryRent(out int s1, out _, out _).Should().BeTrue();
        pool.TryRent(out int s2, out _, out _).Should().BeTrue();
        pool.TryRent(out int s3, out _, out _).Should().BeFalse();

        s1.Should().NotBe(s2);
        s3.Should().Be(-1);

        pool.Return(s1);
        pool.TryRent(out int s4, out _, out _).Should().BeTrue();
        s4.Should().Be(s1);
    }

    [Fact]
    public unsafe void Dispose_DoubleDispose_IsSafe()
    {
        var pool = new PinnedBufferPool(slotCount: 4, slotSize: 256);
        pool.Dispose();
        pool.Dispose(); // Idempotent
    }
}
