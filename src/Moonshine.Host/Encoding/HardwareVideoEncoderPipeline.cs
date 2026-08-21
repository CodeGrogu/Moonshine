using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Managed wrapper for multi-vendor hardware video encoder pipelines (NVENC, AMF, QuickSync, D3D11).
/// </summary>
public sealed class HardwareVideoEncoderPipeline : IVideoEncoderPipeline
{
    private IntPtr _handle;
    private readonly uint _width;
    private readonly uint _height;
    private uint _fps;
    private uint _bitrateKbps;
    private uint _peakBitrateKbps;
    private readonly VideoCodec _codec;
    private readonly EncoderVendor _vendor;
    private bool _disposed;
    private readonly Lock _lock = new();

    private ulong _framesEncoded;
    private ulong _totalEncodingTimeQpc;
    private ulong _encodingErrorsCount;

    public uint Width => _width;
    public uint Height => _height;
    public uint Fps => Volatile.Read(ref _fps);
    public uint BitrateKbps => Volatile.Read(ref _bitrateKbps);
    public VideoCodec Codec => _codec;
    public EncoderVendor Vendor => _vendor;
    public bool IsActive => _handle != IntPtr.Zero && !_disposed;

    public ulong FramesEncoded => Volatile.Read(ref _framesEncoded);
    public ulong EncodingErrorsCount => Volatile.Read(ref _encodingErrorsCount);
    public double AverageEncodingLatencyMicroseconds
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesEncoded);
            ulong totalQpc = Volatile.Read(ref _totalEncodingTimeQpc);
            return frames > 0 ? (double)totalQpc / frames * (1_000_000.0 / Stopwatch.Frequency) : 0.0;
        }
    }

    public HardwareVideoEncoderPipeline(
        uint width,
        uint height,
        uint fps = 60,
        uint bitrateKbps = 20000,
        uint peakBitrateKbps = 30000,
        VideoCodec codec = VideoCodec.HevcMain10,
        RateControlMode rcMode = RateControlMode.ConstantBitrate,
        EncoderVendor vendor = EncoderVendor.Auto,
        IntPtr d3dDevice = 0
    )
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _peakBitrateKbps = peakBitrateKbps;
        _codec = codec;
        _vendor = vendor;

        var config = new MoonshineEncoderConfig
        {
            Width = width,
            Height = height,
            Fps = fps,
            BitrateKbps = bitrateKbps,
            PeakBitrateKbps = peakBitrateKbps,
            Codec = (uint)codec,
            RcMode = (uint)rcMode,
            GopLength = 0, // Infinite GOP for GameStream / Sunshine
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        _handle = MoonshineNativeMethods.EncoderCreate((uint)vendor, d3dDevice, in config);
    }

    ~HardwareVideoEncoderPipeline()
    {
        Dispose(false);
    }

    public unsafe bool TryEncodeFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        desc = default;
        bytesWritten = 0;
        long startQpc = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            fixed (byte* bufferPtr = outBitstream)
            {
                int res = MoonshineNativeMethods.EncoderEncodeFrame(
                    _handle,
                    d3dTexture,
                    forceIdr ? 1 : 0,
                    out desc,
                    bufferPtr,
                    (uint)outBitstream.Length,
                    out uint written
                );

                if (res > 0)
                {
                    bytesWritten = (int)written;
                    long elapsed = Stopwatch.GetTimestamp() - startQpc;
                    Interlocked.Increment(ref _framesEncoded);
                    Interlocked.Add(ref _totalEncodingTimeQpc, (ulong)elapsed);
                    return true;
                }

                Interlocked.Increment(ref _encodingErrorsCount);
                return false;
            }
        }
    }

    public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            if (peakBitrateKbps == 0)
            {
                peakBitrateKbps = (uint)(bitrateKbps * 1.5);
            }

            var config = new MoonshineEncoderConfig
            {
                Width = _width,
                Height = _height,
                Fps = fps,
                BitrateKbps = bitrateKbps,
                PeakBitrateKbps = peakBitrateKbps,
                Codec = (uint)_codec,
                RcMode = 0,
                GopLength = 0,
                EnableIntraRefresh = 0,
                EnableFillerData = 1
            };

            int res = MoonshineNativeMethods.EncoderReconfigure(_handle, in config);
            if (res > 0)
            {
                Volatile.Write(ref _bitrateKbps, bitrateKbps);
                Volatile.Write(ref _peakBitrateKbps, peakBitrateKbps);
                Volatile.Write(ref _fps, fps);
                return true;
            }

            return false;
        }
    }

    public void RequestKeyframe()
    {
        lock (_lock)
        {
            if (!_disposed && _handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.EncoderRequestKeyframe(_handle);
            }
        }
    }

    public static bool TryQueryCapabilities(EncoderVendor vendor, IntPtr d3dDevice, out MoonshineEncoderCaps caps)
    {
        return MoonshineNativeMethods.EncoderQueryCaps((uint)vendor, d3dDevice, out caps) > 0;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.EncoderDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
