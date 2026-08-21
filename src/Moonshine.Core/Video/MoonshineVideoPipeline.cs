using System.Diagnostics;
using Moonshine.Interop;

namespace Moonshine.Core.Video;

public enum HardwareDecoderApi
{
    Direct3D11 = 0,
    Direct3D12 = 1
}

public sealed record VideoPipelineMetrics(
    ulong FramesSubmitted,
    ulong DecodeErrors,
    double AverageDecodeLatencyMicroseconds
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
    private ulong _totalDecodeTimeQpc;
    private uint _width;
    private uint _height;

    public IntPtr Handle => _decoderHandle;
    public HardwareDecoderApi DecoderApi { get; }
    public uint Width => Volatile.Read(ref _width);
    public uint Height => Volatile.Read(ref _height);
    public uint CodecId { get; }
    public bool IsActive => _decoderHandle != IntPtr.Zero && !_disposed;

    public VideoPipelineMetrics Metrics
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesSubmitted);
            ulong totalQpc = Volatile.Read(ref _totalDecodeTimeQpc);
            double avgLatencyUs = frames > 0 ? (double)totalQpc / frames * (1_000_000.0 / Stopwatch.Frequency) : 0.0;
            return new(frames, Volatile.Read(ref _decodeErrors), avgLatencyUs);
        }
    }

    public double AverageDecodeLatencyMicroseconds
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesSubmitted);
            ulong totalQpc = Volatile.Read(ref _totalDecodeTimeQpc);
            return frames > 0 ? (double)totalQpc / frames * (1_000_000.0 / Stopwatch.Frequency) : 0.0;
        }
    }

    public MoonshineVideoPipeline(
        IntPtr hwnd,
        uint width,
        uint height,
        uint codecId = 1, // Default HEVC
        HardwareDecoderApi api = HardwareDecoderApi.Direct3D11)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _width = width;
        _height = height;
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
    /// Submits a reconstructed frame directly into the hardware decoder pipeline with zero GC allocations.
    /// </summary>
    public unsafe bool SubmitFrame(in MoonshineFrameDesc frame)
    {
        long startQpc = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.VideoSubmitFrame(_decoderHandle, in frame);
            if (res != 0)
            {
                Interlocked.Increment(ref _decodeErrors);
                return false;
            }

            long elapsed = Stopwatch.GetTimestamp() - startQpc;
            Interlocked.Increment(ref _framesSubmitted);
            Interlocked.Add(ref _totalDecodeTimeQpc, (ulong)elapsed);
            return true;
        }
    }

    /// <summary>
    /// Gets the GPU-resident decoded texture handle for zero-copy presentation.
    /// </summary>
    public IntPtr GetDecodedTexture()
    {
        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return IntPtr.Zero;
            return MoonshineNativeMethods.VideoGetTexture(_decoderHandle);
        }
    }

    /// <summary>
    /// Dynamically reconfigures the decoder resolution without tearing down the pipeline.
    /// </summary>
    public bool Reset(uint newWidth, uint newHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);

        lock (_lock)
        {
            if (_disposed || _decoderHandle == IntPtr.Zero) return false;
            int res = MoonshineNativeMethods.VideoReset(_decoderHandle, newWidth, newHeight);
            if (res == 0)
            {
                Volatile.Write(ref _width, newWidth);
                Volatile.Write(ref _height, newHeight);
                return true;
            }
            return false;
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
