using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public unsafe class StructLayoutTests
{
    [Fact]
    public void MoonshinePacketDesc_HasExactExpectedSizeAndLayout()
    {
        // 4 (Seq) + 4 (Frame) + 2 (PacketIdx) + 2 (TotalPackets) + 2 (PayloadSize) + 1 (Type) + 1 (Flags) + 4 (SlotIdx) + 4 (StreamPacketIdx) + 8 (Ptr) = 32 bytes
        int size = sizeof(MoonshinePacketDesc);
        size.Should().Be(32);
        Marshal.SizeOf<MoonshinePacketDesc>().Should().Be(32);

        // Assert exact field offsets matching C++ ABI
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.SequenceNumber)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.FrameIndex)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PacketIndex)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.TotalPackets)).ToInt32().Should().Be(10);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PayloadSize)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PacketType)).ToInt32().Should().Be(14);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.Flags)).ToInt32().Should().Be(15);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.BufferSlotIndex)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.StreamPacketIndex)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshinePacketDesc>(nameof(MoonshinePacketDesc.PayloadPtr)).ToInt32().Should().Be(24);
    }

    [Fact]
    public void MoonshinePacketDesc_BinarySerialization_MatchesExactBytePattern()
    {
        byte samplePayload = 0xAB;
        byte* samplePayloadPtr = &samplePayload;

        var desc = new MoonshinePacketDesc
        {
            SequenceNumber = 0x11223344,
            FrameIndex = 0x55667788,
            PacketIndex = 0x0102,
            TotalPackets = 0x0304,
            PayloadSize = 0x0506,
            PacketType = 0x0A,
            Flags = 0x0B,
            BufferSlotIndex = 0x0000002A, // 42
            StreamPacketIndex = 0x00A1B2C3,
            PayloadPtr = samplePayloadPtr
        };

        byte[] rawBytes = new byte[sizeof(MoonshinePacketDesc)];
        fixed (byte* destPtr = rawBytes)
        {
            *(MoonshinePacketDesc*)destPtr = desc;
        }

        // Verify uint32 SequenceNumber (little-endian)
        rawBytes[0].Should().Be(0x44);
        rawBytes[1].Should().Be(0x33);
        rawBytes[2].Should().Be(0x22);
        rawBytes[3].Should().Be(0x11);

        // Verify uint32 FrameIndex
        rawBytes[4].Should().Be(0x88);
        rawBytes[5].Should().Be(0x77);
        rawBytes[6].Should().Be(0x66);
        rawBytes[7].Should().Be(0x55);

        // Verify uint16 PacketIndex
        rawBytes[8].Should().Be(0x02);
        rawBytes[9].Should().Be(0x01);

        // Verify uint16 TotalPackets
        rawBytes[10].Should().Be(0x04);
        rawBytes[11].Should().Be(0x03);

        // Verify uint16 PayloadSize
        rawBytes[12].Should().Be(0x06);
        rawBytes[13].Should().Be(0x05);

        // Verify uint8 PacketType & Flags
        rawBytes[14].Should().Be(0x0A);
        rawBytes[15].Should().Be(0x0B);

        // Verify int32 BufferSlotIndex (42 = 0x2A)
        rawBytes[16].Should().Be(0x2A);
        rawBytes[17].Should().Be(0x00);
        rawBytes[18].Should().Be(0x00);
        rawBytes[19].Should().Be(0x00);

        // Verify uint32 StreamPacketIndex
        rawBytes[20].Should().Be(0xC3);
        rawBytes[21].Should().Be(0xB2);
        rawBytes[22].Should().Be(0xA1);
        rawBytes[23].Should().Be(0x00);

        // Verify uint64 PayloadPtr at offset 24
        ulong expectedPtrVal = (ulong)samplePayloadPtr;
        ulong actualPtrVal;
        fixed (byte* destPtr = rawBytes)
        {
            actualPtrVal = *(ulong*)(destPtr + 24);
        }
        actualPtrVal.Should().Be(expectedPtrVal);
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

    [Fact]
    public void MoonshineGpuAdapter_HasExactExpectedSizeAndLayout()
    {
        sizeof(MoonshineGpuAdapter).Should().Be(184);
        Marshal.SizeOf<MoonshineGpuAdapter>().Should().Be(184);

        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.Index)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.VendorId)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.DeviceId)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.SubsystemId)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.Revision)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.IsSoftware)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.HasOutput)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.Reserved)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.AdapterLuid)).ToInt32().Should().Be(32);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.DedicatedVideoMemory)).ToInt32().Should().Be(40);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.SharedSystemMemory)).ToInt32().Should().Be(48);
        Marshal.OffsetOf<MoonshineGpuAdapter>(nameof(MoonshineGpuAdapter.Description)).ToInt32().Should().Be(56);
    }

    [Fact]
    public void MoonshineQsvDiagnosticReport_HasExactExpectedSizeAndLayout()
    {
        sizeof(MoonshineQsvDiagnosticReport).Should().Be(384);
        Marshal.SizeOf<MoonshineQsvDiagnosticReport>().Should().Be(384);

        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.AdapterFound)).ToInt32().Should().Be(0);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.AdapterDeviceId)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.D3D11DeviceCreated)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.D3D11VendorVerified)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplDllLoaded)).ToInt32().Should().Be(16);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplConfigCreated)).ToInt32().Should().Be(20);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplImplFilterApplied)).ToInt32().Should().Be(24);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplAccelFilterApplied)).ToInt32().Should().Be(28);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplSessionCreated)).ToInt32().Should().Be(32);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.D3D11HandleBound)).ToInt32().Should().Be(36);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.H264Queried)).ToInt32().Should().Be(40);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.HevcQueried)).ToInt32().Should().Be(44);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.Av1Queried)).ToInt32().Should().Be(48);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.H264Supported)).ToInt32().Should().Be(52);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.HevcSupported)).ToInt32().Should().Be(56);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.Av1Supported)).ToInt32().Should().Be(60);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.EncoderConfigured)).ToInt32().Should().Be(64);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.FrameEncoded)).ToInt32().Should().Be(68);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.BitstreamValid)).ToInt32().Should().Be(72);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.DecoderCreated)).ToInt32().Should().Be(76);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.DecoderAccepted)).ToInt32().Should().Be(80);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.DecodedTextureAvailable)).ToInt32().Should().Be(84);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.DecoderLoopbackPassed)).ToInt32().Should().Be(88);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.LegacyMfxFallbackUsed)).ToInt32().Should().Be(92);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.LastMfxStatus)).ToInt32().Should().Be(96);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.ImplFilterStatus)).ToInt32().Should().Be(100);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.AccelFilterStatus)).ToInt32().Should().Be(104);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.LastHResult)).ToInt32().Should().Be(108);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.AdapterDescription)).ToInt32().Should().Be(112);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.VplDllName)).ToInt32().Should().Be(240);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.FirstFailedStage)).ToInt32().Should().Be(304);
        Marshal.OffsetOf<MoonshineQsvDiagnosticReport>(nameof(MoonshineQsvDiagnosticReport.Reserved)).ToInt32().Should().Be(368);
    }
}
