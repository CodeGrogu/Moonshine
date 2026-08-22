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

    /// <summary>
    /// Gets the average synchronous execution time in microseconds spent inside the native encoder call.
    /// Note: This measures the host dispatch and CPU-to-hardware submission duration; asynchronous GPU silicon pipeline flight time is instrumented separately via frame completion timestamps.
    /// </summary>
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
