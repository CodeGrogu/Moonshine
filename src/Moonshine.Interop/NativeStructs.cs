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
    /// <summary>Full 24-bit GameStream stream packet index, or zero for non-GameStream packets.</summary>
    public uint StreamPacketIndex;
    public byte* PayloadPtr;
}

/// <summary>
/// Exact binary match for MoonshineFrameDesc (C-ABI, 24 bytes).
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
/// Exact binary match for MoonshineDecoderCaps (C-ABI, 20 bytes).
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
/// Exact binary match for MoonshineCaptureFrameDesc (C-ABI, 36 bytes).
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
/// Exact binary match for MoonshineAdapterInfo (C-ABI, 160 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineAdapterInfo
{
    public uint AdapterIndex;
    public long AdapterLuid;
    public fixed byte Description[128];
    public ulong DedicatedVideoMemory;
    public byte IsHardware;
    public fixed byte Reserved[11];
}

/// <summary>
/// Exact binary match for MoonshineGpuAdapter (C-ABI, 184 bytes, 8-byte aligned).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public unsafe struct MoonshineGpuAdapter
{
    public uint Index;
    public uint VendorId;
    public uint DeviceId;
    public uint SubsystemId;
    public uint Revision;
    public uint IsSoftware;
    public uint HasOutput;
    public uint Reserved;
    public ulong AdapterLuid;
    public ulong DedicatedVideoMemory;
    public ulong SharedSystemMemory;
    public fixed byte Description[128];
}

/// <summary>
/// Exact binary match for MoonshineQsvDiagnosticReport (C-ABI, 384 bytes, 8-byte aligned).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public unsafe struct MoonshineQsvDiagnosticReport
{
    public uint AdapterFound;
    public uint AdapterDeviceId;
    public uint D3D11DeviceCreated;
    public uint D3D11VendorVerified;
    public uint VplDllLoaded;
    public uint VplConfigCreated;
    public uint VplImplFilterApplied;
    public uint VplAccelFilterApplied;
    public uint VplSessionCreated;
    public uint D3D11HandleBound;
    public uint H264Queried;
    public uint HevcQueried;
    public uint Av1Queried;
    public uint H264Supported;
    public uint HevcSupported;
    public uint Av1Supported;
    public uint EncoderConfigured;
    public uint FrameEncoded;
    public uint BitstreamValid;
    public uint DecoderCreated;
    public uint DecoderAccepted;
    public uint DecodedTextureAvailable;
    public uint DecoderLoopbackPassed;
    public uint LegacyMfxFallbackUsed;
    public int LastMfxStatus;
    public int ImplFilterStatus;
    public int AccelFilterStatus;
    public int LastHResult;
    public fixed byte AdapterDescription[128];
    public fixed byte VplDllName[64];
    public fixed byte FirstFailedStage[64];
    public fixed uint Reserved[4];
}

/// <summary>
/// Exact binary match for MoonshineDisplayInfo (C-ABI, 36 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineDisplayInfo
{
    public uint DisplayIndex;
    public uint AdapterIndex;
    public uint Width;
    public uint Height;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint Rotation;
    public byte IsAttachedToDesktop;
    public byte IsHdr;
    public byte BitsPerColor;
    public fixed byte Reserved[5];
}

/// <summary>
/// Exact binary match for MoonshineDisplayModeDesc (C-ABI, 32 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineDisplayModeDesc
{
    public uint Width;
    public uint Height;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint Format;
    public uint Scaling;
    public uint ScanlineOrdering;
    public byte IsHdr;
    public fixed byte Reserved[3];
}

/// <summary>
/// Exact binary match for MoonshineDisplayExtendedInfo (C-ABI, 152 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineDisplayExtendedInfo
{
    public uint DisplayIndex;
    public uint AdapterIndex;
    public long MonitorHandle;
    public int DesktopLeft;
    public int DesktopTop;
    public int DesktopRight;
    public int DesktopBottom;
    public uint DpiScale;
    public byte IsPrimary;
    public byte IsAttachedToDesktop;
    public byte IsHdr;
    public byte BitsPerColor;
    public fixed byte DeviceName[32];
    public fixed byte FriendlyName[64];
    public fixed byte Reserved[16];
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

/// <summary>
/// Exact binary match for MoonshineVirtualAudioDriverStatusC (C-ABI, 44 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MoonshineVirtualAudioDriverStatus
{
    public byte IsInstalled;
    public byte IsRenderEndpointPresent;
    public byte IsCaptureEndpointPresent;
    public byte Reserved;
    public uint SupportedSampleRatesCount;
    public uint SupportedChannelsCount;
    public fixed byte DriverVersion[32];

    public readonly string GetDriverVersion()
    {
        fixed (byte* ptr = DriverVersion)
        {
            return Marshal.PtrToStringAnsi((IntPtr)ptr) ?? string.Empty;
        }
    }
}

/// <summary>
/// Exact binary match for MoonshineAudioIpcMetricsC (C-ABI, 36 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MoonshineAudioIpcMetrics
{
    public uint RenderPacketsRead;
    public uint RenderUnderruns;
    public uint RenderOverruns;
    public uint CapturePacketsWritten;
    public uint CaptureUnderruns;
    public uint CaptureOverruns;
    public uint SampleRate;
    public uint Channels;
    public uint IsConnected;
}

/// <summary>
/// Exact binary match for MoonshineSwapchainMetrics (C-ABI, 24 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct MoonshineSwapchainMetrics
{
    public ulong FramesPresented;
    public ulong PresentationErrors;
    public ulong DroppedFrames;
}
