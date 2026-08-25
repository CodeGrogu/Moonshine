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
    EncoderImplementationKind ImplementationKind { get; }
    bool IsHardwareAccelerated { get; }
    bool HasProducedValidOutput { get; }
    Type ImplementationType { get; }
    EncoderRuntimeState RuntimeState { get; }
    EncoderEvidence Evidence { get; }

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

    bool TryEncodeFrame(
        IntPtr d3dTexture,
        ulong frameId,
        ulong timestampUs,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    );

    void RecordDecoderAcceptance(ulong frameId);

    EncodeSubmissionResult SubmitFrame(
        IntPtr d3dTexture,
        ulong frameId,
        ulong timestampUs,
        bool forceIdr,
        Span<byte> outBitstream,
        out int bytesWritten
    );

    EncodeSubmissionResult SubmitFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        Span<byte> outBitstream,
        out int bytesWritten
    );

    bool TryPollPacket(
        Span<byte> outBitstream,
        out MoonshineEncodedPacketDesc desc,
        out int bytesWritten
    );

    bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0);
    /// <summary>
    /// Reconfigure encoder resolution. Semantically equivalent to:
    /// Drain() -> release registered surfaces -> reconfigure parameters ->
    /// re-register surfaces at new dimensions -> force IDR on next submission.
    /// </summary>
    bool ReconfigureResolution(uint width, uint height, uint fps = 60, uint bitrateKbps = 0) => false;

    /// <summary>
    /// Stop accepting new frames and wait until all previously submitted frames
    /// have produced their encoded output. Returns true when all pending output
    /// has been collected. Used for session shutdown and pre-reconfiguration flush.
    /// </summary>
    bool Drain() => false;

    /// <summary>
    /// Discard or reset pending encoder state and establish a clean random-access
    /// boundary. The next submitted frame will be an IDR/CRA/key frame.
    /// Returns true when encoder is ready to accept new input.
    /// Used for error recovery and stream discontinuity.
    /// </summary>
    bool Flush() => false;
    void RequestKeyframe();
    bool TryRecoverDevice(IntPtr newD3dDevice);
}
