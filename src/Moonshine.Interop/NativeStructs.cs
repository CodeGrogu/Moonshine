using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// Exact binary match for MoonshinePacketDesc (C-ABI).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshinePacketDesc
{
    public uint SequenceNumber;
    public uint FrameIndex;
    public ushort PacketIndex;
    public ushort TotalPackets;
    public ushort PayloadSize;
    public byte PacketType; // 0: Data, 1: Parity (FEC)
    public byte Flags;      // Bit 0: Start, Bit 1: End, Bit 2: Keyframe
    public byte* PayloadPtr;
}

/// <summary>
/// Exact binary match for MoonshineFrameDesc (C-ABI).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineFrameDesc
{
    public uint FrameIndex;
    public uint TotalBytes;
    public uint PacketCount;
    public byte IsKeyframe;
    public fixed byte Reserved[3];
    public byte* FrameBuffer;
}

/// <summary>
/// Exact binary match for MoonshineDecoderCaps (C-ABI).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineDecoderCaps
{
    public uint MaxWidth;
    public uint MaxHeight;
    public uint MaxFps;
    public byte SupportsAv1;
    public byte SupportsHevc;
    public byte SupportsH264;
    public byte SupportsHdr10;
    public byte Supports10Bit;
    public byte SupportsD3D12;
    public byte SupportsVulkan;
    public fixed byte Reserved[1];
}

/// <summary>
/// Exact binary match for MoonshineCaptureFrameDesc (C-ABI).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineCaptureFrameDesc
{
    public void* TextureHandle;
    public uint Width;
    public uint Height;
    public uint Format;
    public ulong TimestampQpc;
    public uint AccumulatedFrames;
    public byte CursorVisible;
    public fixed byte Reserved[3];
}
