using System.Runtime.CompilerServices;
using System.Text;
using Moonshine.Interop;

namespace Moonshine.Host.Color;

/// <summary>
/// Display HDR10 Metadata Extractor and SMPTE ST 2084 / BT.2020 Parameter Serialiser.
/// </summary>
public static class Hdr10MetadataExtractor
{
    /// <summary>
    /// Extracts HDR10 metadata for the specified display monitor.
    /// </summary>
    public static bool TryExtractMetadata(IntPtr hmonitor, out MoonshineHdr10Metadata metadata)
    {
        int res = MoonshineNativeMethods.HdrExtractMetadata(hmonitor, out metadata);
        return res > 0;
    }

    /// <summary>
    /// Parses HDR capabilities from a DXGI color space identifier.
    /// </summary>
    public static MoonshineHdr10Metadata ParseCapabilities(uint colorSpaceDxgi)
    {
        MoonshineNativeMethods.HdrParseCapabilities(colorSpaceDxgi, out var metadata);
        return metadata;
    }

    /// <summary>
    /// Formats ITU-T H.265 / H.264 Mastering Display Colour Volume & CLL SEI message payload.
    /// </summary>
    public static unsafe byte[] GenerateMasteringDisplaySeiPayload(in MoonshineHdr10Metadata metadata)
    {
        // 24 bytes for Mastering Display SEI + 4 bytes for CLL SEI
        byte[] payload = new byte[28];
        fixed (MoonshineHdr10Metadata* pMeta = &metadata)
        fixed (byte* pDst = payload)
        {
            // Display Primaries (G, B, R order according to ITU-T H.265 D.2.27)
            WriteBigEndian16(pDst + 0, pMeta->GreenPrimary[0]);
            WriteBigEndian16(pDst + 2, pMeta->GreenPrimary[1]);
            WriteBigEndian16(pDst + 4, pMeta->BluePrimary[0]);
            WriteBigEndian16(pDst + 6, pMeta->BluePrimary[1]);
            WriteBigEndian16(pDst + 8, pMeta->RedPrimary[0]);
            WriteBigEndian16(pDst + 10, pMeta->RedPrimary[1]);

            // White Point
            WriteBigEndian16(pDst + 12, pMeta->WhitePoint[0]);
            WriteBigEndian16(pDst + 14, pMeta->WhitePoint[1]);

            // Max / Min Luminance
            WriteBigEndian32(pDst + 16, pMeta->MaxMasteringLuminance);
            WriteBigEndian32(pDst + 20, pMeta->MinMasteringLuminance);

            // Content Light Level (MaxCLL & MaxFALL)
            WriteBigEndian16(pDst + 24, pMeta->MaxContentLightLevel);
            WriteBigEndian16(pDst + 26, pMeta->MaxFrameAverageLightLevel);
        }
        return payload;
    }

    /// <summary>
    /// Formats RTSP SDP HDR10 format parameters for connecting GameStream / Sunshine clients.
    /// </summary>
    public static unsafe string FormatSdpHdrAttributes(in MoonshineHdr10Metadata metadata, int payloadType = 96)
    {
        if (metadata.HdrEnabled == 0)
        {
            return $"a=fmtp:{payloadType} color-primaries=1;transfer-characteristics=1;matrix-coefficients=1\r\n";
        }

        fixed (MoonshineHdr10Metadata* p = &metadata)
        {
            var sb = new StringBuilder();
            sb.Append($"a=fmtp:{payloadType} color-primaries=9;transfer-characteristics=16;matrix-coefficients=9;");
            sb.Append($"mastering-display-color-volume={p->GreenPrimary[0]},{p->GreenPrimary[1]},");
            sb.Append($"{p->BluePrimary[0]},{p->BluePrimary[1]},{p->RedPrimary[0]},{p->RedPrimary[1]},");
            sb.Append($"{p->WhitePoint[0]},{p->WhitePoint[1]},{p->MaxMasteringLuminance},{p->MinMasteringLuminance};");
            sb.Append($"content-light-level={p->MaxContentLightLevel},{p->MaxFrameAverageLightLevel}\r\n");
            return sb.ToString();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteBigEndian16(byte* ptr, ushort val)
    {
        ptr[0] = (byte)(val >> 8);
        ptr[1] = (byte)val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteBigEndian32(byte* ptr, uint val)
    {
        ptr[0] = (byte)(val >> 24);
        ptr[1] = (byte)(val >> 16);
        ptr[2] = (byte)(val >> 8);
        ptr[3] = (byte)val;
    }
}
