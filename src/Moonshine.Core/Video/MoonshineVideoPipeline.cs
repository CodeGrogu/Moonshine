using Moonshine.Interop;

namespace Moonshine.Core.Video;

public enum HardwareDecoderApi
{
    Direct3D11 = 0,
    Direct3D12 = 1
}

public sealed record VideoPipelineMetrics(
    ulong FramesSubmitted,
    ulong DecodeErrors
);

/// <summary>
/// Hardware Video Decoder Pipeline orchestrating Direct3D 11 and Direct3D 12 zero-copy decoding.
/// </summary>
public sealed class MoonshineVideoPipeline : IDisposable
{
    private IntPtr _decoderHandle;
    private readonly Lock _lock = new();
    private bool _disposed;
    private ulong _framesSubmitted;
    private ulong _decodeErrors;

    public IntPtr Handle => _decoderHandle;
    public HardwareDecoderApi DecoderApi { get; }
    public uint Width { get; }
    public uint Height { get; }
    public uint CodecId { get; }

    public VideoPipelineMetrics Metrics => new(
        Volatile.Read(ref _framesSubmitted),
        Volatile.Read(ref _decodeErrors)
    );

    public MoonshineVideoPipeline(
        IntPtr hwnd,
        uint width,
        uint height,
        uint codecId = 1, // Default HEVC
        HardwareDecoderApi api = HardwareDecoderApi.Direct3D11)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        CodecId = codecId;
        DecoderApi = api;

        _decoderHandle = api == HardwareDecoderApi.Direct3D12
            ? MoonshineNativeMethods.VideoCreateD3D12(hwnd, width, height, codecId)
            : MoonshineNativeMethods.VideoCreateD3D11(hwnd, width, height, codecId);

        if (_decoderHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to initialize hardware video decoder using {api}.");
        }
    }

    /// <summary>
    /// Queries the video decoding capabilities of the host system.
    /// </summary>
    public static MoonshineDecoderCaps QueryCaps()
    {
        MoonshineNativeMethods.VideoQueryCaps(out var caps);
        return caps;
    }

    /// <summary>
    /// Submits a reconstructed frame directly into the hardware decoder pipeline.
    /// </summary>
    public unsafe bool SubmitFrame(in MoonshineFrameDesc frame)
    {
        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return false;

            Interlocked.Increment(ref _framesSubmitted);
            int res = MoonshineNativeMethods.VideoSubmitFrame(_decoderHandle, in frame);
            if (res != 0)
            {
                Interlocked.Increment(ref _decodeErrors);
                return false;
            }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_decoderHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.VideoDestroy(_decoderHandle);
                _decoderHandle = IntPtr.Zero;
            }
        }
    }
}
