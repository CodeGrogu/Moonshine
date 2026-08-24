using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Dedicated Intel QuickSync / oneVPL Hardware Video Encoder Pipeline.
/// Provides direct Direct3D 11 texture registration, low-power VDENC mode,
/// CBR rate control, zero B-frames, and progressive intra-refresh slice encoding.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged Intel QuickSync hardware encoder resources via C-ABI.")]
public sealed class QsvHardwareEncoderPipeline : IVideoEncoderPipeline
{
    /// <summary>
    /// Maximum acceptable frame lag between the latest encoded frame and the latest decoder-accepted frame (4 frames).
    /// Accommodates the 4-stage pipelined streaming architecture: Capture -> Encoder In-Flight Queue -> Network Ingestion -> Decoder Display Queue.
    /// </summary>
    public const ulong DecoderAcceptanceLagWindow = EncoderEvidencePolicy.DecoderAcceptanceLagWindow;

    private IntPtr _handle;
    private readonly uint _width;
    private readonly uint _height;
    private uint _fps;
    private uint _bitrateKbps;
    private uint _peakBitrateKbps;
    private readonly VideoCodec _codec;
    private QsvTargetUsage _targetUsage;
    private bool _lowPowerVdenc;
    private bool _intraRefreshEnabled;
    private uint _intraRefreshCycleSize;
    private int _intraRefreshQpDelta;
    private ulong _framesEncoded;
    private ulong _submittedFrameCounter;
    private ulong _totalEncodingTimeQpc;
    private bool _disposed;
    private readonly Lock _lock = new();

    private readonly EncoderImplementationKind _implementationKind;
    private readonly bool _isHardwareAccelerated;
    private EncoderRuntimeState _runtimeState;
    private bool _hasProducedValidOutput;

    private bool _frameSubmitted;
    private bool _outputReceived;
    private bool _bitstreamStructurallyValid;
    private bool _accessUnitValid;
    private ulong _lastDecoderAcceptedFrameId;
    private ulong _firstValidFrameId;
    private ulong _lastValidFrameId;
    private bool _hasValidFrame;

    public uint Width => _width;
    public uint Height => _height;
    public uint Fps => Volatile.Read(ref _fps);
    public uint BitrateKbps => Volatile.Read(ref _bitrateKbps);
    public VideoCodec Codec => _codec;
    public EncoderVendor Vendor => EncoderVendor.IntelQuickSync;
    public QsvTargetUsage TargetUsage => _targetUsage;
    public bool LowPowerVdenc => _lowPowerVdenc;
    public bool IsActive => _handle != IntPtr.Zero && !_disposed;
    public EncoderImplementationKind ImplementationKind => _implementationKind;
    public bool IsHardwareAccelerated => _isHardwareAccelerated;
    public bool HasProducedValidOutput => Volatile.Read(ref _hasProducedValidOutput);
    public Type ImplementationType => GetType();
    public EncoderRuntimeState RuntimeState
    {
        get
        {
            if (_disposed) return EncoderRuntimeState.Disposed;
            if (_handle == IntPtr.Zero) return EncoderRuntimeState.Faulted;
            int nativeState = MoonshineNativeMethods.EncoderGetState(_handle);
            return nativeState switch
            {
                8 => EncoderRuntimeState.Faulted,
                9 => EncoderRuntimeState.Disposed,
                _ => _runtimeState
            };
        }
    }

    public EncoderEvidence Evidence
    {
        get
        {
            ulong lastValid = Volatile.Read(ref _lastValidFrameId);
            ulong lastAccepted = Volatile.Read(ref _lastDecoderAcceptedFrameId);
            bool latestMatch = lastAccepted != 0 && lastAccepted == lastValid;
            bool healthy = EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(_disposed, _handle != IntPtr.Zero, lastValid, lastAccepted);

            return new EncoderEvidence(
                ApiAvailable: _handle != IntPtr.Zero,
                HardwareSupported: _isHardwareAccelerated,
                SessionInitialised: !_disposed && _handle != IntPtr.Zero,
                FrameSubmitted: Volatile.Read(ref _frameSubmitted),
                OutputReceived: Volatile.Read(ref _outputReceived),
                BitstreamStructurallyValid: Volatile.Read(ref _bitstreamStructurallyValid),
                AccessUnitValid: Volatile.Read(ref _accessUnitValid),
                DecoderAccepted: healthy,
                FirstValidFrameId: Volatile.Read(ref _firstValidFrameId),
                LastValidFrameId: lastValid,
                LastDecoderAcceptedFrameId: lastAccepted,
                DecoderAcceptedLatestFrame: latestMatch,
                DecoderAcceptanceHealthy: healthy
            );
        }
    }

    public double AverageEncodingLatencyMicroseconds
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesEncoded);
            ulong totalQpc = Volatile.Read(ref _totalEncodingTimeQpc);
            return frames > 0 ? (double)MoonshineMediaClock.TicksToMicroseconds((long)totalQpc) / frames : 0.0;
        }
    }

    public QsvHardwareEncoderPipeline(
        uint width,
        uint height,
        uint fps = 60,
        uint bitrateKbps = 20000,
        uint peakBitrateKbps = 30000,
        VideoCodec codec = VideoCodec.HevcMain10,
        QsvTargetUsage targetUsage = QsvTargetUsage.BestSpeed,
        bool lowPowerVdenc = true,
        IntPtr d3dDevice = 0
    )
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _peakBitrateKbps = peakBitrateKbps;
        _codec = codec;
        _targetUsage = targetUsage;
        _lowPowerVdenc = lowPowerVdenc;

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

        _handle = MoonshineNativeMethods.EncoderCreate((uint)EncoderVendor.IntelQuickSync, d3dDevice, in config);
        if (_handle != IntPtr.Zero)
        {
            _implementationKind = EncoderImplementationKind.HardwareAccelerated;
            _isHardwareAccelerated = true;
            _runtimeState = EncoderRuntimeState.Ready;
            _ = MoonshineNativeMethods.QsvSetTuning(_handle, (uint)targetUsage, lowPowerVdenc ? 1 : 0);
        }
        else
        {
            _implementationKind = EncoderImplementationKind.Unimplemented;
            _isHardwareAccelerated = false;
            _runtimeState = EncoderRuntimeState.Faulted;
        }
    }

    public unsafe bool TryEncodeFrame(
        IntPtr d3dTexture,
        ulong frameId,
        ulong timestampUs,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        desc = default;
        bytesWritten = 0;
        long startQpc = System.Diagnostics.Stopwatch.GetTimestamp();

        lock (_lock)
        {
            Volatile.Write(ref _frameSubmitted, true);
            if (_disposed || _handle == IntPtr.Zero || d3dTexture == IntPtr.Zero) return false;

            _runtimeState = EncoderRuntimeState.Encoding;
            try
            {
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
                        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - startQpc;
                        Interlocked.Increment(ref _framesEncoded);
                        Interlocked.Add(ref _totalEncodingTimeQpc, (ulong)elapsed);

                        desc.FrameIndex = frameId;
                        if (timestampUs > 0)
                        {
                            desc.TimestampQpc = (long)timestampUs;
                        }

                        if (bytesWritten > 0)
                        {
                            Volatile.Write(ref _outputReceived, true);
                            var auResult = BitstreamValidator.ValidateAccessUnit(_codec, outBitstream[..bytesWritten]);
                            if (auResult.HasStructurallyValidPayload)
                            {
                                Volatile.Write(ref _bitstreamStructurallyValid, true);
                            }

                            if (!auResult.IsCompleteAccessUnit || !auResult.ContainsFrameData)
                            {
                                bytesWritten = 0;
                                _runtimeState = EncoderRuntimeState.Ready;
                                return false;
                            }

                            Volatile.Write(ref _accessUnitValid, true);
                            Volatile.Write(ref _hasProducedValidOutput, true);

                            if (frameId != 0)
                            {
                                if (!_hasValidFrame)
                                {
                                    Volatile.Write(ref _firstValidFrameId, frameId);
                                    _hasValidFrame = true;
                                }
                                Volatile.Write(ref _lastValidFrameId, frameId);
                            }

                            bool isKeyframe = auResult.HasCodecHeaders || auResult.HasRandomAccessMarker || auResult.HasParameterSets || auResult.HasIdr || auResult.HasRandomAccessPoint;
                            desc.IsKeyframe = (byte)(isKeyframe ? 1 : desc.IsKeyframe);
                        }

                        _runtimeState = EncoderRuntimeState.Ready;
                        return true;
                    }

                    _runtimeState = EncoderRuntimeState.Ready;
                    return false;
                }
            }
            catch
            {
                _runtimeState = EncoderRuntimeState.Faulted;
                throw;
            }
        }
    }

    public bool TryEncodeFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        out MoonshineEncodedPacketDesc desc,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        ulong frameId = Interlocked.Increment(ref _submittedFrameCounter);
        ulong timestampUs = MoonshineMediaClock.GetCurrentTimestampMicroseconds();
        return TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out desc, outBitstream, out bytesWritten);
    }

    public void NotifyDecoderAcceptedFrame(ulong frameId)
    {
        lock (_lock)
        {
            if (_disposed) return;
            ulong currentLast = Volatile.Read(ref _lastDecoderAcceptedFrameId);
            if (frameId > currentLast)
            {
                Volatile.Write(ref _lastDecoderAcceptedFrameId, frameId);
            }
        }
    }

    public void RecordDecoderAcceptance(ulong frameId)
    {
        NotifyDecoderAcceptedFrame(frameId);
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
        if (_disposed)
        {
            bytesWritten = 0;
            return new EncodeSubmissionResult(
                Submitted: false,
                OutputAvailable: false,
                KeyFrame: false,
                BytesWritten: 0,
                PacketDesc: default,
                Result: EncoderResult.NotAvailable
            );
        }

        if (_handle == IntPtr.Zero || d3dTexture == IntPtr.Zero)
        {
            bytesWritten = 0;
            return new EncodeSubmissionResult(
                Submitted: false,
                OutputAvailable: false,
                KeyFrame: false,
                BytesWritten: 0,
                PacketDesc: default,
                Result: EncoderResult.NotAvailable
            );
        }

        bool success = TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out var desc, outBitstream, out bytesWritten);
        return new EncodeSubmissionResult(
            Submitted: true,
            OutputAvailable: success && bytesWritten > 0,
            KeyFrame: desc.IsKeyframe != 0,
            BytesWritten: bytesWritten,
            PacketDesc: desc,
            Result: success ? EncoderResult.Success : EncoderResult.EncoderFailure
        );
    }

    public EncodeSubmissionResult SubmitFrame(
        IntPtr d3dTexture,
        bool forceIdr,
        Span<byte> outBitstream,
        out int bytesWritten
    )
    {
        ulong frameId = Interlocked.Increment(ref _submittedFrameCounter);
        ulong timestampUs = MoonshineMediaClock.GetCurrentTimestampMicroseconds();
        return SubmitFrame(d3dTexture, frameId, timestampUs, forceIdr, outBitstream, out bytesWritten);
    }

    public bool ReconfigureBitrate(uint bitrateKbps, uint peakBitrateKbps)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            var newConfig = new MoonshineEncoderConfig
            {
                Width = _width,
                Height = _height,
                Fps = _fps,
                BitrateKbps = bitrateKbps,
                PeakBitrateKbps = peakBitrateKbps,
                Codec = (uint)_codec,
                RcMode = 0,
                GopLength = 0,
                EnableIntraRefresh = (byte)(_intraRefreshEnabled ? 1 : 0),
                EnableFillerData = 1
            };

            int res = MoonshineNativeMethods.EncoderReconfigure(_handle, in newConfig);
            if (res > 0)
            {
                Volatile.Write(ref _bitrateKbps, bitrateKbps);
                Volatile.Write(ref _peakBitrateKbps, peakBitrateKbps);
                return true;
            }
            return false;
        }
    }

    public bool TryPollPacket(
        Span<byte> outBitstream,
        out MoonshineEncodedPacketDesc desc,
        out int bytesWritten
    )
    {
        desc = default;
        bytesWritten = 0;
        return false;
    }

    public bool Reconfigure(uint bitrateKbps, uint fps, uint peakBitrateKbps = 0)
    {
        if (peakBitrateKbps == 0)
        {
            peakBitrateKbps = (uint)(bitrateKbps * 1.5);
        }
        Volatile.Write(ref _fps, fps);
        return ReconfigureBitrate(bitrateKbps, peakBitrateKbps);
    }

    public bool ConfigureTuning(QsvTargetUsage targetUsage, bool lowPowerVdenc)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.QsvSetTuning(_handle, (uint)targetUsage, lowPowerVdenc ? 1 : 0);
            if (res > 0)
            {
                _targetUsage = targetUsage;
                _lowPowerVdenc = lowPowerVdenc;
                return true;
            }
            return false;
        }
    }

    public bool ConfigureIntraRefresh(bool enable, uint cycleSize, int qpDelta)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;

            int res = MoonshineNativeMethods.QsvSetIntraRefresh(_handle, enable ? 1 : 0, cycleSize, qpDelta);
            if (res > 0)
            {
                _intraRefreshEnabled = enable;
                _intraRefreshCycleSize = cycleSize;
                _intraRefreshQpDelta = qpDelta;
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
        int res = MoonshineNativeMethods.QsvQueryCodecSupport((uint)codec, out uint supported);
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
            _runtimeState = EncoderRuntimeState.Disposed;
            Volatile.Write(ref _lastDecoderAcceptedFrameId, 0);
            Volatile.Write(ref _lastValidFrameId, 0);

            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.EncoderDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
