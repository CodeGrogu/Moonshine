using System.Globalization;
using System.Text;

namespace Moonshine.Protocol.RTSP;

public enum VideoCodec
{
    H264 = 0,
    Hevc = 1,
    Av1 = 2
}

public sealed record Hdr10MasteringMetadata(
    ushort RedX = 34000,
    ushort RedY = 16000,
    ushort GreenX = 13250,
    ushort GreenY = 34500,
    ushort BlueX = 7500,
    ushort BlueY = 3000,
    ushort WhitePointX = 15635,
    ushort WhitePointY = 16450,
    uint MaxMasteringLuminance = 1000,
    uint MinMasteringLuminance = 1,
    ushort MaxCll = 1000,
    ushort MaxFall = 400
);

public sealed record MoonshineStreamConfiguration(
    int Width = 1920,
    int Height = 1080,
    int FrameRate = 60,
    int BitrateKbps = 20000,
    VideoCodec Codec = VideoCodec.Hevc,
    bool EnableHdr = false,
    Hdr10MasteringMetadata? HdrMetadata = null,
    int AudioChannels = 2,
    int AudioSampleRate = 48000,
    int AudioBitrateKbps = 128,
    int ClientRtpVideoPort = 47998,
    int ClientRtpAudioPort = 48000,
    int ClientControlPort = 47999,
    int FecShardsK = 20,
    int FecShardsN = 25
);

public sealed record SdpNegotiationResult(
    bool Success,
    int VideoPayloadType,
    int AudioPayloadType,
    int VideoPort,
    int AudioPort,
    int ControlPort,
    string? SessionId,
    string? ErrorMessage = null
);

/// <summary>
/// High-performance SDP (Session Description Protocol) builder and parser for GameStream and Sunshine.
/// Compliant with RFC 4566 and NVIDIA GameStream stream negotiation extensions.
/// </summary>
public static class SdpNegotiator
{
    public const int PayloadTypeH264 = 96;
    public const int PayloadTypeHevc = 98;
    public const int PayloadTypeAv1 = 100;
    public const int PayloadTypeOpus = 97;

    /// <summary>
    /// Generates client SDP offer payload for RTSP DESCRIBE and SETUP requests.
    /// </summary>
    public static string BuildClientSdp(MoonshineStreamConfiguration config)
    {
        var sb = new StringBuilder(1024);

        int videoPt = config.Codec switch
        {
            VideoCodec.H264 => PayloadTypeH264,
            VideoCodec.Hevc => PayloadTypeHevc,
            VideoCodec.Av1 => PayloadTypeAv1,
            _ => PayloadTypeHevc
        };

        string codecName = config.Codec switch
        {
            VideoCodec.H264 => "H264",
            VideoCodec.Hevc => "H265",
            VideoCodec.Av1 => "AV1",
            _ => "H265"
        };

        // Session level descriptions (RFC 4566)
        sb.Append("v=0\r\n");
        sb.Append("o=Moonshine 0 0 IN IP4 0.0.0.0\r\n");
        sb.Append("s=NVIDIA Streaming Session\r\n");
        sb.Append("t=0 0\r\n");
        sb.Append("a=control:rtsp://0.0.0.0/\r\n");

        // Video Media Description
        sb.Append(CultureInfo.InvariantCulture, $"m=video {config.ClientRtpVideoPort} RTP/AVP {videoPt}\r\n");
        sb.Append("c=IN IP4 0.0.0.0\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=rtpmap:{videoPt} {codecName}/90000\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=fmtp:{videoPt} packetization-mode=1\r\n");
        sb.Append("a=control:streamid=video\r\n");

        // GameStream QoS and Video Attributes
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].clientViewportWd:{config.Width}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].clientViewportHt:{config.Height}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].fps:{config.FrameRate}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].initialBitrateKbps:{config.BitrateKbps}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].dynamicRangeMode:{(config.EnableHdr ? 1 : 0)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-vqos[0].fec.k:{config.FecShardsK}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-vqos[0].fec.n:{config.FecShardsN}\r\n");

        // HDR10 Metadata Attributes (SMPTE ST 2086 / CTA-861-G)
        if (config.EnableHdr && config.HdrMetadata != null)
        {
            var hdr = config.HdrMetadata;
            sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].hdr.displayPrimaries:{hdr.RedX},{hdr.RedY},{hdr.GreenX},{hdr.GreenY},{hdr.BlueX},{hdr.BlueY}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].hdr.whitePoint:{hdr.WhitePointX},{hdr.WhitePointY}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].hdr.masteringLuminance:{hdr.MaxMasteringLuminance},{hdr.MinMasteringLuminance}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].hdr.maxCll:{hdr.MaxCll}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"a=x-nv-video[0].hdr.maxFall:{hdr.MaxFall}\r\n");
        }

        // Audio Media Description
        sb.Append(CultureInfo.InvariantCulture, $"m=audio {config.ClientRtpAudioPort} RTP/AVP {PayloadTypeOpus}\r\n");
        sb.Append("c=IN IP4 0.0.0.0\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=rtpmap:{PayloadTypeOpus} opus/{config.AudioSampleRate}/{config.AudioChannels}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"a=fmtp:{PayloadTypeOpus} maxaveragebitrate={config.AudioBitrateKbps * 1000};stereo={(config.AudioChannels > 1 ? 1 : 0)}\r\n");
        sb.Append("a=control:streamid=audio\r\n");

        // Control Stream Description
        sb.Append(CultureInfo.InvariantCulture, $"m=application {config.ClientControlPort} RTP/AVP 101\r\n");
        sb.Append("c=IN IP4 0.0.0.0\r\n");
        sb.Append("a=control:streamid=control\r\n");

        return sb.ToString();
    }

    /// <summary>
    /// Parses server SDP answer to extract stream properties and port mappings.
    /// </summary>
    public static SdpNegotiationResult ParseServerSdp(string sdpText)
    {
        if (string.IsNullOrWhiteSpace(sdpText))
        {
            return new SdpNegotiationResult(false, 0, 0, 0, 0, 0, null, "Empty SDP content");
        }

        int videoPt = 0;
        int audioPt = 0;
        int videoPort = 0;
        int audioPort = 0;
        int controlPort = 0;
        string? sessionId = null;

        var lines = sdpText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string currentMediaType = string.Empty;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("m=video", StringComparison.OrdinalIgnoreCase))
            {
                currentMediaType = "video";
                var parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[1], CultureInfo.InvariantCulture, out int port)) videoPort = port;
                    if (int.TryParse(parts[3], CultureInfo.InvariantCulture, out int pt)) videoPt = pt;
                }
            }
            else if (line.StartsWith("m=audio", StringComparison.OrdinalIgnoreCase))
            {
                currentMediaType = "audio";
                var parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[1], CultureInfo.InvariantCulture, out int port)) audioPort = port;
                    if (int.TryParse(parts[3], CultureInfo.InvariantCulture, out int pt)) audioPt = pt;
                }
            }
            else if (line.StartsWith("m=application", StringComparison.OrdinalIgnoreCase))
            {
                currentMediaType = "control";
                var parts = line.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int port))
                {
                    controlPort = port;
                }
            }
            else if (line.StartsWith("a=x-nv-session-id:", StringComparison.OrdinalIgnoreCase))
            {
                sessionId = line["a=x-nv-session-id:".Length..].Trim();
            }
        }

        return new SdpNegotiationResult(
            Success: true,
            VideoPayloadType: videoPt != 0 ? videoPt : PayloadTypeHevc,
            AudioPayloadType: audioPt != 0 ? audioPt : PayloadTypeOpus,
            VideoPort: videoPort,
            AudioPort: audioPort,
            ControlPort: controlPort,
            SessionId: sessionId
        );
    }
}
