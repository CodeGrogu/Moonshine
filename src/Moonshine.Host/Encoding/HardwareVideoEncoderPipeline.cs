using System.Diagnostics;
using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Managed wrapper for multi-vendor hardware video encoder pipelines (NVENC, AMF, QuickSync, D3D11).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged hardware encoder resources via C-ABI.")]
public sealed class HardwareVideoEncoderPipeline : IVideoEncoderPipeline
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
    private readonly EncoderVendor _vendor;
    private readonly RateControlMode _rcMode;
    private bool _disposed;
    private readonly Lock _lock = new();

    private ulong _framesEncoded;
    private ulong _submittedFrameCounter;
    private ulong _totalEncodingTimeQpc;
    private ulong _encodingErrorsCount;

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
    private bool _hasDecoderAcceptance;

    public uint Width => _width;
    public uint Height => _height;
    public uint Fps => Volatile.Read(ref _fps);
    public uint BitrateKbps => Volatile.Read(ref _bitrateKbps);
    public VideoCodec Codec => _codec;
    public EncoderVendor Vendor => _vendor;
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
            bool hasAccepted = Volatile.Read(ref _hasDecoderAcceptance);
            bool hasValid = Volatile.Read(ref _hasValidFrame);
            bool latestMatch = hasAccepted && hasValid && lastAccepted == lastValid;
            bool healthy = EncoderEvidencePolicy.IsDecoderAcceptanceHealthy(_disposed, _handle != IntPtr.Zero, hasValid, lastValid, hasAccepted, lastAccepted);

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
                DecoderAcceptanceHealthy: healthy,
                HasDecoderAcceptance: hasAccepted,
                HasValidFrame: hasValid
            );
        }
    }

    public ulong FramesEncoded => Volatile.Read(ref _framesEncoded);
    public ulong EncodingErrorsCount => Volatile.Read(ref _encodingErrorsCount);
    public double AverageEncodingLatencyMicroseconds
    {
        get
        {
            ulong frames = Volatile.Read(ref _framesEncoded);
            ulong totalQpc = Volatile.Read(ref _totalEncodingTimeQpc);
            return frames > 0 ? (double)MoonshineMediaClock.TicksToMicroseconds((long)totalQpc) / frames : 0.0;
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
        _rcMode = rcMode;
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
            GopLength = 0, // Infinite GOP for sub-frame streaming
            EnableIntraRefresh = 0,
            EnableFillerData = 1
        };

        _handle = MoonshineNativeMethods.EncoderCreate((uint)vendor, d3dDevice, in config);
        if (_handle != IntPtr.Zero)
        {
            if (_vendor == EncoderVendor.Auto)
            {
                _vendor = (EncoderVendor)MoonshineNativeMethods.EncoderGetVendor(_handle);
            }
            _implementationKind = EncoderImplementationKind.HardwareAccelerated;
            _isHardwareAccelerated = true;
            _runtimeState = EncoderRuntimeState.Ready;
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
        long startQpc = Stopwatch.GetTimestamp();

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
                        long elapsed = Stopwatch.GetTimestamp() - startQpc;
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

                            if (!auResult.IsValid || !auResult.ContainsFrameData)
                            {
                                bytesWritten = 0;
                                Interlocked.Increment(ref _encodingErrorsCount);
                                _runtimeState = EncoderRuntimeState.Ready;
                                return false;
                            }

                            Volatile.Write(ref _accessUnitValid, true);
                            Volatile.Write(ref _hasProducedValidOutput, true);

                            if (!_hasValidFrame)
                            {
                                Volatile.Write(ref _firstValidFrameId, frameId);
                                _hasValidFrame = true;
                            }
                            Volatile.Write(ref _lastValidFrameId, frameId);

                            bool isKeyframe = auResult.HasCodecHeaders || auResult.HasRandomAccessMarker || auResult.HasParameterSets || auResult.HasIdr || auResult.HasRandomAccessPoint;
                            desc.IsKeyframe = (byte)(isKeyframe ? 1 : desc.IsKeyframe);
                            Interlocked.Increment(ref _framesEncoded);
                            _runtimeState = EncoderRuntimeState.Ready;
                            return true;
                        }

                        Interlocked.Increment(ref _framesEncoded);
                        _runtimeState = EncoderRuntimeState.Ready;
                        return true;
                    }

                    Interlocked.Increment(ref _encodingErrorsCount);
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
        lock (_lock)
        {
            ulong frameId = Interlocked.Increment(ref _submittedFrameCounter);
            ulong timestampUs = MoonshineMediaClock.GetCurrentTimestampMicroseconds();
            return TryEncodeFrame(d3dTexture, frameId, timestampUs, forceIdr, out desc, outBitstream, out bytesWritten);
        }
    }

    public void RecordDecoderAcceptance(ulong frameId)
    {
        lock (_lock)
        {
            Volatile.Write(ref _hasDecoderAcceptance, true);
            Volatile.Write(ref _lastDecoderAcceptedFrameId, frameId);
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
        if (_disposed)
        {
            bytesWritten = 0;
            return new EncodeSubmissionResult(
                Submitted: false,
                OutputAvailable: false,
                KeyFrame: false,
                BytesWritten: 0,
                PacketDesc: default,
                Result: EncoderResult.DeviceLost
            );
        }

        if (_handle == IntPtr.Zero)
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
        if (!success)
        {
            return new EncodeSubmissionResult(
                Submitted: false,
                OutputAvailable: false,
                KeyFrame: false,
                BytesWritten: 0,
                PacketDesc: default,
                Result: _runtimeState == EncoderRuntimeState.Faulted ? EncoderResult.DeviceLost : EncoderResult.EncoderFailure
            );
        }

        bool isKey = desc.IsKeyframe != 0;
        return new EncodeSubmissionResult(
            Submitted: true,
            OutputAvailable: bytesWritten > 0,
            KeyFrame: isKey,
            BytesWritten: bytesWritten,
            PacketDesc: desc,
            Result: EncoderResult.Success
        );
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
            ulong frameId = Interlocked.Increment(ref _submittedFrameCounter);
            ulong timestampUs = MoonshineMediaClock.GetCurrentTimestampMicroseconds();
            return SubmitFrame(d3dTexture, frameId, timestampUs, forceIdr, outBitstream, out bytesWritten);
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
            _runtimeState = EncoderRuntimeState.Disposed;
            Volatile.Write(ref _hasDecoderAcceptance, false);
            Volatile.Write(ref _hasValidFrame, false);
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
