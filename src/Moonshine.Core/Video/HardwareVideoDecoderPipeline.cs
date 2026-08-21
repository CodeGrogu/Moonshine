using Moonshine.Interop;

namespace Moonshine.Core.Video;

/// <summary>
/// High-level client hardware video decoder pipeline.
/// Orchestrates GPU capability probing, codec negotiation, frame submission, and presentation surface extraction.
/// </summary>
public sealed class HardwareVideoDecoderPipeline : IDisposable
{
    private readonly MoonshineVideoPipeline? _pipeline;
    private readonly HardwareDecoderApi _api;
    private readonly uint _codecId;
    private bool _disposed;
    private readonly Lock _lock = new();

    public bool IsActive => _pipeline?.IsActive ?? false;
    public HardwareDecoderApi DecoderApi => _api;
    public uint CodecId => _codecId;
    public uint Width => _pipeline?.Width ?? 0;
    public uint Height => _pipeline?.Height ?? 0;
    public VideoPipelineMetrics? Metrics => _pipeline?.Metrics;

    public HardwareVideoDecoderPipeline(
        IntPtr hwnd,
        uint width,
        uint height,
        uint preferredCodec = 1, // HEVC default
        HardwareDecoderApi preferredApi = HardwareDecoderApi.Direct3D11)
    {
        _codecId = preferredCodec;
        _api = preferredApi;

        try
        {
            _pipeline = new MoonshineVideoPipeline(hwnd, width, height, preferredCodec, preferredApi);
        }
        catch
        {
            _pipeline = null;
        }
    }

    /// <summary>
    /// Submits a reconstructed video frame to the hardware decoder.
    /// </summary>
    public bool TrySubmitFrame(in MoonshineFrameDesc frame)
    {
        lock (_lock)
        {
            if (_disposed || _pipeline == null || !_pipeline.IsActive) return false;
            return _pipeline.SubmitFrame(in frame);
        }
    }

    /// <summary>
    /// Gets the GPU-resident decoded texture handle for swapchain presentation.
    /// </summary>
    public IntPtr GetDecodedSurface()
    {
        lock (_lock)
        {
            if (_disposed || _pipeline == null || !_pipeline.IsActive) return IntPtr.Zero;
            return _pipeline.GetDecodedTexture();
        }
    }

    /// <summary>
    /// Reconfigures stream dimensions on resolution change.
    /// </summary>
    public bool Reconfigure(uint newWidth, uint newHeight)
    {
        lock (_lock)
        {
            if (_disposed || _pipeline == null || !_pipeline.IsActive) return false;
            return _pipeline.Reset(newWidth, newHeight);
        }
    }

    /// <summary>
    /// Queries the live hardware video decoding capabilities from the host GPU adapter.
    /// </summary>
    public static MoonshineDecoderCaps QueryCapabilities()
    {
        return MoonshineVideoPipeline.QueryCaps();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _pipeline?.Dispose();
        }
    }
}
