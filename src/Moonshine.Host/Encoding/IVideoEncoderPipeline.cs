using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Polymorphic interface for Hardware Video Encoder Pipelines.
/// </summary>
public interface IVideoEncoderPipeline : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    uint Fps { get; }
    uint BitrateKbps { get; }
    VideoCodec Codec { get; }
    EncoderVendor Vendor { get; }
    bool IsActive { get; }
    double AverageEncodingLatencyMicroseconds { get; }

    bool TryEncodeFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    );

    bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0);
    void RequestKeyframe();
}
