using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Dedicated AMD AMF Hardware Video Encoder Pipeline.
/// Provides direct Direct3D 11/12 texture registration, VCN low-latency profiles,
/// CBR rate control, zero B-frames, and progressive intra-refresh slice encoding.
/// </summary>
public sealed class AmfHardwareEncoderPipeline : IVideoEncoderPipeline
{
    private IntPtr _handle;
    private readonly uint _width;
    private readonly uint _height;
    private uint _fps;
    private uint _bitrateKbps;
    private uint _peakBitrateKbps;
    private readonly VideoCodec _codec;
    private AmfQualityPreset _preset;
    private AmfUsage _usage;
    private bool _intraRefreshEnabled;
    private uint _intraRefreshMbsPerSlot;
    private bool _disposed;
    private readonly Lock _lock = new();

    public uint Width => _width;
    public uint Height => _height;
    public uint Fps => Volatile.Read(ref _fps);
    public uint BitrateKbps => Volatile.Read(ref _bitrateKbps);
    public VideoCodec Codec => _codec;
    public EncoderVendor Vendor => EncoderVendor.AmdAmf;
    public AmfQualityPreset Preset => _preset;
    public AmfUsage Usage => _usage;
    public bool IsActive => _handle != IntPtr.Zero && !_disposed;
    public double AverageEncodingLatencyMicroseconds => 0.0;

    public AmfHardwareEncoderPipeline(
        uint width,
        uint height,
        uint fps = 60,
        uint bitrateKbps = 20000,
        uint peakBitrateKbps = 30000,
        VideoCodec codec = VideoCodec.HevcMain10,
        AmfQualityPreset preset = AmfQualityPreset.Speed,
        AmfUsage usage = AmfUsage.UltraLowLatency,
        IntPtr d3dDevice = 0
    )
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _peakBitrateKbps = peakBitrateKbps;
        _codec = codec;
        _preset = preset;
        _usage = usage;

        var config = new MoonshineEncoderConfig
        {
            Width = width,
            Height = height,
            Fps = fps,
            BitrateKbps = bitrateKbps,
            PeakBitrateKbps = peakBitrateKbps,
            Codec = (uint)codec,
            RcMode = 0, // CBR
            GopLength = 0, // Infinite GOP for sub-frame streaming
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        _handle = MoonshineNativeMethods.EncoderCreate((uint)EncoderVendor.AmdAmf, d3dDevice, in config);
        if (_handle != IntPtr.Zero)
        {
            _ = MoonshineNativeMethods.AmfSetTuning(_handle, (uint)preset, (uint)usage);
        }
    }

    ~AmfHardwareEncoderPipeline()
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
                    return true;
                }

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
                EnableIntraRefresh = (byte)(_intraRefreshEnabled ? 1 : 0),
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

    public bool ConfigureTuning(AmfQualityPreset preset, AmfUsage usage)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            int res = MoonshineNativeMethods.AmfSetTuning(_handle, (uint)preset, (uint)usage);
            if (res > 0)
            {
                _preset = preset;
                _usage = usage;
                return true;
            }
            return false;
        }
    }

    public bool ConfigureIntraRefresh(bool enable, uint mbsPerSlot = 16)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            int res = MoonshineNativeMethods.AmfSetIntraRefresh(_handle, enable ? 1 : 0, mbsPerSlot);
            if (res > 0)
            {
                _intraRefreshEnabled = enable;
                _intraRefreshMbsPerSlot = mbsPerSlot;
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

    public static bool IsCodecSupported(VideoCodec codec)
    {
        int res = MoonshineNativeMethods.AmfQueryCodecSupport((uint)codec, out uint supported);
        return res > 0 && supported > 0;
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
