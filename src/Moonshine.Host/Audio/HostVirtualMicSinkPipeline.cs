using System.Runtime.InteropServices;
using Moonshine.Interop;

namespace Moonshine.Host.Audio;

/// <summary>
/// Telemetry metrics for the Host Virtual Microphone Sink.
/// </summary>
public readonly record struct HostMicSinkMetrics(
    ulong TotalPacketsReceived,
    ulong TotalSamplesRendered,
    uint LossCount,
    uint DriftCorrections,
    double CurrentJitterMs
);

/// <summary>
/// High-performance, zero-allocation managed pipeline for Host Microphone Virtual Audio Injection.
/// Interfaces with native C++23 MicAudioSink for sub-15ms voice latency, adaptive jitter buffering,
/// clock drift compensation, and virtual audio device routing.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:DisposableTypesShouldDeclareFinalizer", Justification = "Finaliser deliberately omitted: managed disposal deterministically releases unmanaged mic sink resources via C-ABI.")]
public sealed class HostVirtualMicSinkPipeline : IDisposable
{
    private IntPtr _handle;
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly uint _targetLatencyMs;
    private bool _disposed;

    public uint SampleRate => _sampleRate;
    public uint Channels => _channels;
    public uint TargetLatencyMs => _targetLatencyMs;
    public bool IsInitialized => _handle != IntPtr.Zero && !_disposed;

    public HostVirtualMicSinkPipeline(
        uint sampleRate = 48000,
        uint channels = 1,
        uint targetLatencyMs = 10,
        float gainMultiplier = 1.0f,
        float noiseGateThresholdDb = -50.0f,
        bool isMuted = false
    )
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _targetLatencyMs = targetLatencyMs;

        _handle = MoonshineNativeMethods.MicSinkCreate(
            sampleRate,
            channels,
            targetLatencyMs,
            gainMultiplier,
            noiseGateThresholdDb,
            (byte)(isMuted ? 1 : 0)
        );

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize native Host Virtual Microphone Sink.");
        }
    }

    /// <summary>
    /// Pushes an incoming Opus microphone packet from the client backchannel into the jitter buffer.
    /// </summary>
    public unsafe bool TryPushOpusPacket(
        ReadOnlySpan<byte> opusPayload,
        uint timestamp,
        ushort sequenceNumber
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (opusPayload.IsEmpty)
        {
            return false;
        }

        fixed (byte* pPayload = opusPayload)
        {
            int result = MoonshineNativeMethods.MicSinkPushOpusPacket(
                _handle,
                pPayload,
                (uint)opusPayload.Length,
                timestamp,
                sequenceNumber
            );
            return result != 0;
        }
    }

    /// <summary>
    /// Pulls processed Float32 PCM samples for injection into the host virtual microphone device.
    /// </summary>
    public unsafe bool TryPullPcm(
        Span<float> outPcm,
        out int samplesRead
    )
    {
        samplesRead = 0;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (outPcm.IsEmpty)
        {
            return false;
        }

        fixed (float* pPcm = outPcm)
        {
            int result = MoonshineNativeMethods.MicSinkPullPcm(
                _handle,
                pPcm,
                (uint)outPcm.Length,
                out uint nativeRead
            );

            samplesRead = (int)nativeRead;
            return result != 0;
        }
    }

    /// <summary>
    /// Adjusts host microphone input gain multiplier.
    /// </summary>
    public void SetGain(float gain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MoonshineNativeMethods.MicSinkSetGain(_handle, gain);
    }

    /// <summary>
    /// Toggles microphone mute state on the host sink.
    /// </summary>
    public void SetMute(bool isMuted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MoonshineNativeMethods.MicSinkSetMute(_handle, (byte)(isMuted ? 1 : 0));
    }

    /// <summary>
    /// Retrieves active jitter buffer and rendering metrics.
    /// </summary>
    public HostMicSinkMetrics GetMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MoonshineNativeMethods.MicSinkGetMetrics(
            _handle,
            out ulong packetsReceived,
            out ulong samplesRendered,
            out uint lossCount,
            out uint driftCorrections,
            out double jitterMs
        );

        return new HostMicSinkMetrics(
            packetsReceived,
            samplesRendered,
            lossCount,
            driftCorrections,
            jitterMs
        );
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
            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.MicSinkDestroy(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
