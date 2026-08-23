using System.Diagnostics;
using System.Runtime.CompilerServices;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Core.Audio;

/// <summary>
/// Production client microphone capture and encoding pipeline.
/// Captures client 48kHz mono microphone audio frames, applies software noise gating and gain normalisation,
/// compresses via low-delay Opus VoIP encoding, and packetises into backchannel RTP or MNBP datagrams with zero GC allocations.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged Opus encoder handles via C-ABI.")]
public sealed class ClientMicrophoneCapturePipeline : IDisposable
{
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly uint _bitrate;
    private readonly uint _frameDurationMs;
    private readonly uint _frameSamples;
    private readonly uint _streamId;
    private readonly ulong _sessionId;
    private readonly byte _payloadType;
    private readonly float[] _scratchPcmBuffer;
    private readonly byte[] _scratchOpusBuffer = new byte[1024];

    private IntPtr _encoderHandle;
    private ushort _sequenceNumber;
    private ulong _currentSampleIndex;
    private float _gainMultiplier = 1.0f;
    private float _noiseGateThresholdDb = -50.0f;
    private float _currentAttenuation = 1.0f;
    private bool _isMuted;
    private bool _wasMuted;
    private bool _disposed;

    public uint SampleRate => _sampleRate;
    public uint Channels => _channels;
    public uint Bitrate => _bitrate;
    public uint FrameDurationMs => _frameDurationMs;
    public uint StreamId => _streamId;
    public ulong SessionId => _sessionId;
    public byte PayloadType => _payloadType;
    public bool IsMuted => _isMuted;
    public float GainMultiplier => _gainMultiplier;
    public float NoiseGateThresholdDb => _noiseGateThresholdDb;
    public bool IsInitialized => _encoderHandle != IntPtr.Zero && !_disposed;
    public ushort CurrentSequenceNumber => _sequenceNumber;
    public ulong CurrentSampleIndex => _currentSampleIndex;

    public ClientMicrophoneCapturePipeline(
        uint sampleRate = 48000,
        uint channels = 1,
        uint bitrate = 32000,
        uint frameDurationMs = 10,
        uint streamId = 0x99887766,
        ulong sessionId = 0,
        byte payloadType = MicAudioPacket.DefaultPayloadType
    )
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bitrate = bitrate;
        _frameDurationMs = frameDurationMs;
        _frameSamples = (sampleRate * frameDurationMs) / 1000;
        _streamId = streamId;
        _sessionId = sessionId;
        _payloadType = payloadType;

        _scratchPcmBuffer = new float[_frameSamples * _channels];

        _encoderHandle = MoonshineNativeMethods.OpusEncoderCreate(
            sampleRate,
            channels,
            bitrate,
            frameDurationMs,
            complexity: 6,
            useVbr: 1
        );

        if (_encoderHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialise native Opus encoder for Client Microphone.");
        }
    }

    /// <summary>
    /// Sets client microphone input gain multiplier clamped between 0.0 and 10.0.
    /// </summary>
    public void SetGain(float gain)
    {
        _gainMultiplier = Math.Clamp(gain, 0.0f, 10.0f);
    }

    /// <summary>
    /// Sets client microphone mute state.
    /// </summary>
    public void SetMute(bool isMuted)
    {
        _isMuted = isMuted;
    }

    /// <summary>
    /// Sets client microphone noise gate threshold in decibels.
    /// </summary>
    public void SetNoiseGateThreshold(float thresholdDb)
    {
        _noiseGateThresholdDb = thresholdDb;
    }

    /// <summary>
    /// Processes a frame of client microphone audio and packetises it into an outgoing datagram.
    /// Supports RFC 3550 RTP or Moonshine Native Binary Protocol (MNBP) framing with zero heap allocations.
    /// </summary>
    public unsafe bool TryProcessRecordedFrame(
        ReadOnlySpan<float> pcmSamples,
        Span<byte> outDatagram,
        out int bytesWritten,
        bool preferMoonshineFraming = false
    )
    {
        bytesWritten = 0;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_encoderHandle == IntPtr.Zero)
        {
            return false;
        }

        uint frameSamples = _frameSamples;
        int requiredSamples = _scratchPcmBuffer.Length;
        if (pcmSamples.Length < requiredSamples)
        {
            return false;
        }

        // Apply noise gate with envelope smoothing, input gain normalisation, and smooth mute transition
        Span<float> processedPcm = _scratchPcmBuffer.AsSpan();

        if (_isMuted)
        {
            if (_wasMuted)
            {
                // Steady-state muted: zero entire frame
                processedPcm.Clear();
            }
            else
            {
                // Smooth mute transition: ramp gain to zero across first 64 samples
                _wasMuted = true;
                const int muteRampSamples = 64;
                int rampLimit = Math.Min(muteRampSamples, requiredSamples);

                for (int i = 0; i < rampLimit; ++i)
                {
                    float muteFactor = (muteRampSamples - i) / (float)muteRampSamples;
                    float val = pcmSamples[i] * _gainMultiplier * _currentAttenuation * muteFactor;
                    processedPcm[i] = Math.Clamp(val, -1.0f, 1.0f);
                }

                if (requiredSamples > rampLimit)
                {
                    processedPcm[rampLimit..].Clear();
                }
            }
        }
        else
        {
            _wasMuted = false;

            double sumSq = 0.0;
            for (int i = 0; i < requiredSamples; ++i)
            {
                sumSq += pcmSamples[i] * pcmSamples[i];
            }
            double rms = Math.Sqrt(sumSq / requiredSamples);
            double thresholdAmp = Math.Pow(10.0, _noiseGateThresholdDb / 20.0);
            bool gateClosed = rms < thresholdAmp;
            float targetAttenuation = gateClosed ? 0.05f : 1.0f;

            for (int i = 0; i < requiredSamples; ++i)
            {
                _currentAttenuation += (targetAttenuation - _currentAttenuation) * 0.05f;
                float val = pcmSamples[i] * _gainMultiplier * _currentAttenuation;
                processedPcm[i] = Math.Clamp(val, -1.0f, 1.0f);
            }
        }

        // Encode to Opus via native C-ABI using pre-allocated scratch buffer
        Span<byte> opusPayload = _scratchOpusBuffer.AsSpan();
        int encodeResult;
        uint opusBytes;

        fixed (float* pPcm = processedPcm)
        fixed (byte* pOut = opusPayload)
        {
            encodeResult = MoonshineNativeMethods.OpusEncoderEncodeFloat(
                _encoderHandle,
                pPcm,
                frameSamples,
                pOut,
                (uint)opusPayload.Length,
                out opusBytes
            );
        }

        if (encodeResult == 0 || opusBytes == 0)
        {
            return false;
        }

        ushort seq = _sequenceNumber++;
        ulong sampleIdx = _currentSampleIndex;
        _currentSampleIndex += frameSamples;
        uint timestamp = (uint)sampleIdx;

        if (preferMoonshineFraming)
        {
            int totalSize = MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize + (int)opusBytes;
            if (outDatagram.Length < totalSize)
            {
                return false;
            }

            ulong timestampUs = (ulong)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));

            var outerHeader = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.MicPacket,
                PayloadSize: (uint)(MoonshineMicPacketCodec.HeaderSize + opusBytes),
                SequenceNumber: seq,
                SessionId: _sessionId,
                TimestampUs: timestampUs
            );

            if (!MoonshineProtocolCodec.TryWriteHeader(in outerHeader, outDatagram[..MoonshineProtocolConstants.HeaderSize]))
            {
                return false;
            }

            var micHeader = new MoonshineMicPacketHeader
            {
                StreamId = _streamId,
                SampleIndex = sampleIdx,
                PayloadSize = (ushort)opusBytes,
                Channels = (byte)_channels,
                Codec = MoonshineAudioCodec.Opus,
                SampleRate = _sampleRate
            };

            if (!MoonshineMicPacketCodec.TryWriteHeader(in micHeader, outDatagram.Slice(MoonshineProtocolConstants.HeaderSize, MoonshineMicPacketCodec.HeaderSize)))
            {
                return false;
            }

            opusPayload.Slice(0, (int)opusBytes).CopyTo(outDatagram.Slice(MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize));
            bytesWritten = totalSize;
            return true;
        }

        return MicAudioPacket.TryWrite(
            opusPayload.Slice(0, (int)opusBytes),
            seq,
            timestamp,
            _streamId,
            marker: false,
            _payloadType,
            outDatagram,
            out bytesWritten
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_encoderHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusEncoderDestroy(_encoderHandle);
                _encoderHandle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
