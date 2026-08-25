using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Unified Multi-Vendor Hardware Video Encoder Engine.
/// Orchestrates GPU vendor auto-detection, zero-copy VRAM surface encoding,
/// dynamic congestion bitrate scaling, and instant IDR keyframe recovery.
/// </summary>
public sealed class UnifiedHardwareEncoderEngine : IDisposable
{
    /// <summary>
    /// Maximum acceptable frame lag between the latest encoded frame and the latest decoder-accepted frame (4 frames).
    /// Accommodates the 4-stage pipelined streaming architecture: Capture -> Encoder In-Flight Queue -> Network Ingestion -> Decoder Display Queue.
    /// </summary>
    public const ulong DecoderAcceptanceLagWindow = EncoderEvidencePolicy.DecoderAcceptanceLagWindow;

    private readonly IVideoEncoderPipeline _pipeline;
    private long _framesEncoded;
    private long _keyframesEmitted;
    private long _bytesEmitted;
    private long _encodingErrors;
    private bool _hasProducedValidOutput;
    private bool _disposed;
    private readonly Lock _lock = new();

    public uint Width => _pipeline.Width;
    public uint Height => _pipeline.Height;
    public uint Fps => _pipeline.Fps;
    public uint BitrateKbps => _pipeline.BitrateKbps;
    public VideoCodec Codec => _pipeline.Codec;
    public EncoderVendor Vendor => _pipeline.Vendor;
    public bool IsActive => _pipeline.IsActive && !_disposed;
    public EncoderImplementationKind ImplementationKind => _pipeline.ImplementationKind;
    public bool IsHardwareAccelerated => _pipeline.IsHardwareAccelerated;
    public bool HasProducedValidOutput => Volatile.Read(ref _hasProducedValidOutput) || _pipeline.HasProducedValidOutput;
    public Type ImplementationType => _pipeline.ImplementationType;
    public EncoderRuntimeState RuntimeState => _disposed ? EncoderRuntimeState.Disposed : _pipeline.RuntimeState;
    public EncoderEvidence Evidence => _pipeline.Evidence;

    public long FramesEncoded => Interlocked.Read(ref _framesEncoded);
    public long KeyframesEmitted => Interlocked.Read(ref _keyframesEmitted);
    public long BytesEmitted => Interlocked.Read(ref _bytesEmitted);
    public long EncodingErrors => Interlocked.Read(ref _encodingErrors);
    public double AverageEncodingLatencyMicroseconds => _pipeline.AverageEncodingLatencyMicroseconds;

    public UnifiedHardwareEncoderEngine(IVideoEncoderPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public UnifiedHardwareEncoderEngine(
        uint width,
        uint height,
        uint fps = 60,
        uint bitrateKbps = 20000,
        VideoCodec codec = VideoCodec.HevcMain10,
        RateControlMode rcMode = RateControlMode.ConstantBitrate,
        EncoderVendor preferredVendor = EncoderVendor.Auto,
        IntPtr d3dDevice = 0
    )
    {
        _pipeline = new HardwareVideoEncoderPipeline(
            width,
            height,
            fps,
            bitrateKbps,
            (uint)(bitrateKbps * 1.5),
            codec,
            rcMode,
            preferredVendor,
            d3dDevice
        );
    }

    public bool TryEncodeFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            bool success = _pipeline.TryEncodeFrame(d3dTexture, forceIdr, out desc, outBitstream, out bytesWritten);
            if (success)
            {
                Interlocked.Increment(ref _framesEncoded);
                if (desc.IsKeyframe != 0)
                {
                    Interlocked.Increment(ref _keyframesEmitted);
                }
                Interlocked.Add(ref _bytesEmitted, bytesWritten);
                if (bytesWritten > 0)
                {
                    var auResult = BitstreamValidator.ValidateAccessUnit(Codec, outBitstream[..bytesWritten]);
                    if (auResult.IsValid && auResult.ContainsFrameData)
                    {
                        Volatile.Write(ref _hasProducedValidOutput, true);
                    }
                }
                return true;
            }

            Interlocked.Increment(ref _encodingErrors);
            return false;
        }
    }

    public bool TryEncodeFrame(
        IntPtr d3dTexture,
        ulong frameId,
        ulong timestampUs,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            bool success = _pipeline.TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out desc, outBitstream, out bytesWritten);
            if (success)
            {
                Interlocked.Increment(ref _framesEncoded);
                if (desc.IsKeyframe != 0)
                {
                    Interlocked.Increment(ref _keyframesEmitted);
                }
                Interlocked.Add(ref _bytesEmitted, bytesWritten);
                if (bytesWritten > 0)
                {
                    var auResult = BitstreamValidator.ValidateAccessUnit(Codec, outBitstream[..bytesWritten]);
                    if (auResult.IsValid && auResult.ContainsFrameData)
                    {
                        Volatile.Write(ref _hasProducedValidOutput, true);
                    }
                }
                return true;
            }

            Interlocked.Increment(ref _encodingErrors);
            return false;
        }
    }

    public void RecordDecoderAcceptance(ulong frameId)
    {
        lock (_lock)
        {
            if (!_disposed && _pipeline.IsActive)
            {
                _pipeline.RecordDecoderAcceptance(frameId);
            }
        }
    }

    public EncodeSubmissionResult SubmitFrame(
        IntPtr d3dTexture,
        ulong frameId,
        ulong timestampUs,
        bool forceIdr,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive)
            {
                bytesWritten = 0;
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: _disposed ? EncoderResult.DeviceLost : EncoderResult.NotAvailable
                );
            }

            var submission = _pipeline.SubmitFrame(d3dTexture, frameId, timestampUs, forceIdr, outBitstream, out bytesWritten);
            if (submission.Submitted && submission.OutputAvailable)
            {
                Interlocked.Increment(ref _framesEncoded);
                if (submission.KeyFrame)
                {
                    Interlocked.Increment(ref _keyframesEmitted);
                }
                Interlocked.Add(ref _bytesEmitted, bytesWritten);
                if (bytesWritten > 0)
                {
                    var auResult = BitstreamValidator.ValidateAccessUnit(Codec, outBitstream[..bytesWritten]);
                    if (auResult.IsValid && auResult.ContainsFrameData)
                    {
                        Volatile.Write(ref _hasProducedValidOutput, true);
                    }
                }
            }
            else if (!submission.Submitted || submission.Result != EncoderResult.Success)
            {
                Interlocked.Increment(ref _encodingErrors);
            }

            return submission;
        }
    }

    public EncodeSubmissionResult SubmitFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive)
            {
                bytesWritten = 0;
                return new EncodeSubmissionResult(
                    Submitted: false,
                    OutputAvailable: false,
                    KeyFrame: false,
                    BytesWritten: 0,
                    PacketDesc: default,
                    Result: _disposed ? EncoderResult.DeviceLost : EncoderResult.NotAvailable
                );
            }

            var submission = _pipeline.SubmitFrame(d3dTexture, forceIdr, outBitstream, out bytesWritten);
            if (submission.Submitted && submission.OutputAvailable)
            {
                Interlocked.Increment(ref _framesEncoded);
                if (submission.KeyFrame)
                {
                    Interlocked.Increment(ref _keyframesEmitted);
                }
                Interlocked.Add(ref _bytesEmitted, bytesWritten);
                if (bytesWritten > 0)
                {
                    var auResult = BitstreamValidator.ValidateAccessUnit(Codec, outBitstream[..bytesWritten]);
                    if (auResult.IsValid && auResult.ContainsFrameData)
                    {
                        Volatile.Write(ref _hasProducedValidOutput, true);
                    }
                }
            }
            else if (!submission.Submitted || submission.Result != EncoderResult.Success)
            {
                Interlocked.Increment(ref _encodingErrors);
            }

            return submission;
        }
    }

    public bool TryPollPacket(
        Span<byte> outBitstream,
        out MoonshineEncodedPacketDesc desc,
        out int bytesWritten
    )
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive)
            {
                desc = default;
                bytesWritten = 0;
                return false;
            }

            bool polled = _pipeline.TryPollPacket(outBitstream, out desc, out bytesWritten);
            if (polled && bytesWritten > 0)
            {
                Interlocked.Increment(ref _framesEncoded);
                if (desc.IsKeyframe != 0)
                {
                    Interlocked.Increment(ref _keyframesEmitted);
                }
                Interlocked.Add(ref _bytesEmitted, bytesWritten);
                var auResult = BitstreamValidator.ValidateAccessUnit(Codec, outBitstream[..bytesWritten]);
                if (auResult.IsValid && auResult.ContainsFrameData)
                {
                    Volatile.Write(ref _hasProducedValidOutput, true);
                }
            }
            return polled;
        }
    }

    public bool ReconfigureBitrate(uint newBitrateKbps, uint newFps = 0)
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive) return false;
            uint fps = (newFps > 0) ? newFps : _pipeline.Fps;
            return _pipeline.Reconfigure(newBitrateKbps, fps);
        }
    }

    public bool ReconfigureResolution(uint width, uint height, uint fps = 60, uint bitrateKbps = 0)
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive) return false;
            return _pipeline.ReconfigureResolution(width, height, fps, bitrateKbps);
        }
    }

    public bool Drain()
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive) return false;
            return _pipeline.Drain();
        }
    }

    public bool Flush()
    {
        lock (_lock)
        {
            if (_disposed || !_pipeline.IsActive) return false;
            return _pipeline.Flush();
        }
    }

    public void RequestKeyframe()
    {
        lock (_lock)
        {
            if (!_disposed && _pipeline.IsActive)
            {
                _pipeline.RequestKeyframe();
            }
        }
    }

    public bool TryRecoverDevice(IntPtr newD3dDevice)
    {
        lock (_lock)
        {
            if (_disposed) return false;
            bool recovered = _pipeline.TryRecoverDevice(newD3dDevice);
            if (recovered)
            {
                Volatile.Write(ref _hasProducedValidOutput, false);
            }
            return recovered;
        }
    }

    public static bool TryQueryCapabilities(
        EncoderVendor vendor,
        out MoonshineEncoderCaps caps,
        IntPtr d3dDevice = 0
    )
    {
        int res = MoonshineNativeMethods.EncoderQueryCaps((uint)vendor, d3dDevice, out caps);
        return res > 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _pipeline.Dispose();
        }
    }
}
