using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// Exact binary match for MoonshinePacketDesc (C-ABI, 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshinePacketDesc
{
    public const int NoBufferSlot = -1;

    public uint SequenceNumber;
    public uint FrameIndex;
    public ushort PacketIndex;
    public ushort TotalPackets;
    public ushort PayloadSize;
    public byte PacketType; // 0: Data, 1: Parity (FEC)
    public byte Flags;      // Bit 0: Start, Bit 1: End, Bit 2: Keyframe
    public int BufferSlotIndex;
    public fixed byte Reserved[4];
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

/// <summary>
/// Exact binary match for MoonshineHdr10Metadata (C-ABI, 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineHdr10Metadata
{
    public fixed ushort RedPrimary[2];              // BT.2020 Red coordinates (scaled by 50000)
    public fixed ushort GreenPrimary[2];            // BT.2020 Green coordinates (scaled by 50000)
    public fixed ushort BluePrimary[2];             // BT.2020 Blue coordinates (scaled by 50000)
    public fixed ushort WhitePoint[2];              // D65 White Point coordinates (scaled by 50000)
    public uint MaxMasteringLuminance;              // Max mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    public uint MinMasteringLuminance;              // Min mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    public ushort MaxContentLightLevel;             // MaxCLL in nits
    public ushort MaxFrameAverageLightLevel;        // MaxFALL in nits
    public byte HdrEnabled;                         // 1 if HDR10 active, 0 for SDR
    public byte ColorSpace;                         // 0 for BT.709, 1 for BT.2020
    public fixed byte Reserved[2];                  // Padding for strict 32-byte alignment
}

/// <summary>
/// Exact binary match for MoonshineEncoderCaps (C-ABI, 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineEncoderCaps
{
    public uint SupportedCodecsMask;
    public uint MaxWidth;
    public uint MaxHeight;
    public uint MaxFps;
    public byte Supports10Bit;
    public byte SupportsLossless;
    public byte SupportsSmartIdr;
    public byte VendorId;
    public uint MinBitrateKbps;
    public uint MaxBitrateKbps;
    public uint Reserved;
}

/// <summary>
/// Exact binary match for MoonshineEncoderConfig (C-ABI, 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineEncoderConfig
{
    public uint Width;
    public uint Height;
    public uint Fps;
    public uint BitrateKbps;
    public uint PeakBitrateKbps;
    public uint Codec;
    public uint RcMode;
    public ushort GopLength;
    public byte EnableIntraRefresh;
    public byte EnableFillerData;
}

/// <summary>
/// Exact binary match for MoonshineEncodedPacketDesc (C-ABI, 24 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineEncodedPacketDesc
{
    public ulong FrameIndex;
    public long TimestampQpc;
    public uint PayloadSize;
    public byte IsKeyframe;
    public byte IsHeaderPacket;
    public byte TemporalId;
    public byte Reserved;
}

