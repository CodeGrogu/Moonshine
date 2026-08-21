using System.Buffers.Binary;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;

namespace Moonshine.Client.Audio;

/// <summary>
/// Client Microphone Audio Pipeline.
/// Captures client 48kHz mono microphone audio, applies software noise gating and gain normalisation,
/// compresses via low-delay Opus encoding, and packetises into backchannel RTP datagrams with zero GC allocations.
/// </summary>
public sealed class ClientMicCapturePipeline : IDisposable
{
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly uint _frameDurationMs;
    private readonly uint _ssrc;
    private readonly byte _payloadType;
    private IntPtr _encoderHandle;
    private ushort _sequenceNumber;
    private uint _currentTimestamp;
    private float _gainMultiplier = 1.0f;
    private float _noiseGateThresholdDb = -50.0f;
    private bool _isMuted;
    private bool _disposed;

    public uint SampleRate => _sampleRate;
    public uint Channels => _channels;
    public uint FrameDurationMs => _frameDurationMs;
    public bool IsMuted => _isMuted;
    public float GainMultiplier => _gainMultiplier;
    public bool IsInitialized => _encoderHandle != IntPtr.Zero && !_disposed;

    public ClientMicCapturePipeline(
        uint sampleRate = 48000,
        uint channels = 1,
        uint bitrate = 32000,
        uint frameDurationMs = 10,
        uint ssrc = 0x99887766,
        byte payloadType = MicAudioPacket.DefaultPayloadType
    )
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _frameDurationMs = frameDurationMs;
        _ssrc = ssrc;
        _payloadType = payloadType;

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
            throw new InvalidOperationException("Failed to initialize native Opus encoder for Client Microphone.");
        }
    }

    /// <summary>
    /// Sets client microphone input gain.
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
    /// Processes a frame of client microphone audio and packetises it into an outgoing RTP datagram.
    /// </summary>
    public unsafe bool TryProcessRecordedFrame(
        ReadOnlySpan<float> pcmSamples,
        Span<byte> outRtpDatagram,
        out int bytesWritten
    )
    {
        bytesWritten = 0;
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint frameSamples = (_sampleRate * _frameDurationMs) / 1000;
        if (pcmSamples.Length < frameSamples)
        {
            return false;
        }

        // Apply noise gate and mute in scratch buffer
        Span<float> processedPcm = stackalloc float[pcmSamples.Length];
        if (_isMuted)
        {
            processedPcm.Clear();
        }
        else
        {
            double sumSq = 0.0;
            for (int i = 0; i < pcmSamples.Length; ++i)
            {
                sumSq += pcmSamples[i] * pcmSamples[i];
            }
            double rms = Math.Sqrt(sumSq / pcmSamples.Length);
            double thresholdAmp = Math.Pow(10.0, _noiseGateThresholdDb / 20.0);
            bool gateClosed = rms < thresholdAmp;

            for (int i = 0; i < pcmSamples.Length; ++i)
            {
                float val = pcmSamples[i] * _gainMultiplier;
                if (gateClosed)
                {
                    val *= 0.05f;
                }
                processedPcm[i] = Math.Clamp(val, -1.0f, 1.0f);
            }
        }

        // Encode to Opus
        Span<byte> opusPayload = stackalloc byte[512];
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
        uint timestamp = _currentTimestamp;
        _currentTimestamp += frameSamples;

        return MicAudioPacket.TryWrite(
            opusPayload.Slice(0, (int)opusBytes),
            seq,
            timestamp,
            _ssrc,
            marker: false,
            _payloadType,
            outRtpDatagram,
            out bytesWritten
        );
    }

    ~ClientMicCapturePipeline()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
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
