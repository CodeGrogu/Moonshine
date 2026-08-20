using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public unsafe class StructLayoutTests
{
    [Fact]
    public void MoonshinePacketDesc_HasExactExpectedSize()
    {
        // 4 (Seq) + 4 (Frame) + 2 (PacketIdx) + 2 (TotalPackets) + 2 (PayloadSize) + 1 (Type) + 1 (Flags) + 8 (Ptr) = 24 bytes
        int size = sizeof(MoonshinePacketDesc);
        size.Should().Be(24);
    }

    [Fact]
    public void MoonshineFrameDesc_HasExactExpectedSize()
    {
        // 4 (FrameIdx) + 4 (TotalBytes) + 4 (PacketCount) + 1 (Keyframe) + 3 (Reserved) + 8 (Ptr) = 24 bytes
        int size = sizeof(MoonshineFrameDesc);
        size.Should().Be(24);
    }

    [Fact]
    public void MoonshineDecoderCaps_HasExactExpectedSize()
    {
        // 4*3 (Width, Height, Fps) + 7 (bools) + 1 (reserved) = 20 bytes
        int size = sizeof(MoonshineDecoderCaps);
        size.Should().Be(20);
    }
}
