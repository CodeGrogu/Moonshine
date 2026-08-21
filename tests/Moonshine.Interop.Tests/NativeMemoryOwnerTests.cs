using FluentAssertions;
using Xunit;

namespace Moonshine.Interop.Tests;

public class NativeMemoryOwnerTests
{
    [Fact]
    public void NativeMemoryOwner_AllocatesAndProvidesZeroedSpan()
    {
        using var owner = new NativeMemoryOwner(1024);
        owner.Length.Should().Be(1024);
        owner.ActiveLeases.Should().Be(0);

        Span<byte> span = owner.GetSpan();
        span.Length.Should().Be(1024);
        span[0].Should().Be(0);
        span[1023].Should().Be(0);

        span[42] = 0xBE;
        owner.GetSpan()[42].Should().Be(0xBE);
    }

    [Fact]
    public void NativeMemoryOwner_LeaseAndRelease_GuardsLifecycle()
    {
        using var owner = new NativeMemoryOwner(256);
        owner.ActiveLeases.Should().Be(0);

        NativeBufferLease lease1 = owner.Lease();
        owner.ActiveLeases.Should().Be(1);
        lease1.Length.Should().Be(256);

        lease1.Span[0] = 0xAA;
        owner.GetSpan()[0].Should().Be(0xAA);

        // Multiple concurrent leases
        NativeBufferLease lease2 = owner.Lease();
        owner.ActiveLeases.Should().Be(2);

        lease1.Dispose();
        owner.ActiveLeases.Should().Be(1);

        // Disposed lease should fail closed
        Action accessAction = () => { var _ = lease1.Span[0]; };
        accessAction.Should().Throw<ObjectDisposedException>();

        lease2.Dispose();
        owner.ActiveLeases.Should().Be(0);

        // Double dispose of lease is safe no-op
        lease1.Dispose();
        owner.ActiveLeases.Should().Be(0);
    }

    [Fact]
    public void NativeMemoryOwner_Disposed_FailsClosed()
    {
        var owner = new NativeMemoryOwner(128);
        ((IDisposable)owner).Dispose();

        Action accessSpan = () => { var _ = owner.GetSpan(); };
        accessSpan.Should().Throw<ObjectDisposedException>();

        Action lease = () => { var _ = owner.Lease(); };
        lease.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void MoonshineErrorCode_Extensions_ClassifyCorrectly()
    {
        MoonshineErrorCode.Success.IsSuccess().Should().BeTrue();
        MoonshineErrorCode.Success.IsFatal().Should().BeFalse();

        MoonshineErrorCode.TransientBusy.IsTransient().Should().BeTrue();
        MoonshineErrorCode.Timeout.IsTransient().Should().BeTrue();

        MoonshineErrorCode.Fatal.IsFatal().Should().BeTrue();
        MoonshineErrorCode.UseAfterFree.IsFatal().Should().BeTrue();
        MoonshineErrorCode.DoubleRelease.IsFatal().Should().BeTrue();
    }
}
