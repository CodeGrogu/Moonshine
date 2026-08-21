using System;
using System.Diagnostics.CodeAnalysis;
using Moonshine.Interop;

namespace Moonshine.Host.Audio;

/// <summary>
/// Managed coordinator for the real-time shared memory IPC audio bridge.
/// Bridges host streaming and client microphone injection directly to the dedicated Windows virtual audio driver.
/// </summary>
public sealed class VirtualAudioIpcBridgePipeline : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;
    private readonly uint _sampleRate;
    private readonly uint _channels;
    private readonly bool _isHostServer;

    /// <summary>
    /// Gets the negotiated sample rate in Hz.
    /// </summary>
    public uint SampleRate => _sampleRate;

    /// <summary>
    /// Gets the active channel count.
    /// </summary>
    public uint Channels => _channels;

    /// <summary>
    /// Gets whether the shared memory IPC bridge is actively connected.
    /// </summary>
    public bool IsConnected => !_disposed && _handle != IntPtr.Zero && MoonshineNativeMethods.AudioIpcBridgeIsConnected(_handle) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualAudioIpcBridgePipeline"/> class.
    /// </summary>
    public VirtualAudioIpcBridgePipeline(
        bool isHostServer = true,
        uint sampleRate = 48000,
        uint channels = 2
    )
    {
        _isHostServer = isHostServer;
        _sampleRate = sampleRate;
        _channels = channels;
        _handle = MoonshineNativeMethods.AudioIpcBridgeCreate(isHostServer ? 1 : 0, sampleRate, channels);

        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize native audio IPC bridge.");
        }
    }

    /// <summary>
    /// Writes decoded client microphone PCM samples into the virtual microphone capture ring buffer.
    /// </summary>
    public unsafe int WriteCapturePcm(ReadOnlySpan<float> pcmSamples)
    {
        ThrowIfDisposed();
        if (pcmSamples.IsEmpty)
        {
            return 0;
        }

        fixed (float* ptr = pcmSamples)
        {
            long written = MoonshineNativeMethods.AudioIpcBridgeWriteCapturePcm(
                _handle,
                ptr,
                (uint)pcmSamples.Length
            );
            return (int)written;
        }
    }

    /// <summary>
    /// Reads rendered game audio PCM samples from the virtual speaker ring buffer.
    /// </summary>
    public unsafe int ReadRenderPcm(Span<float> outPcmSamples, bool waitEvent = false, uint timeoutMs = 15)
    {
        ThrowIfDisposed();
        if (outPcmSamples.IsEmpty)
        {
            return 0;
        }

        fixed (float* ptr = outPcmSamples)
        {
            long read = MoonshineNativeMethods.AudioIpcBridgeReadRenderPcm(
                _handle,
                ptr,
                (uint)outPcmSamples.Length,
                waitEvent ? 1 : 0,
                timeoutMs
            );
            return (int)read;
        }
    }

    /// <summary>
    /// Waits for the render synchronization event to be signaled by the virtual audio driver.
    /// </summary>
    public bool WaitRenderEvent(uint timeoutMs = 15)
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.AudioIpcBridgeWaitRenderEvent(_handle, timeoutMs) != 0;
    }

    /// <summary>
    /// Retrieves real-time IPC bridge metrics including underruns and overruns.
    /// </summary>
    public bool TryGetMetrics(out MoonshineNativeMethods.AudioIpcMetricsInterop metrics)
    {
        ThrowIfDisposed();
        int res = MoonshineNativeMethods.AudioIpcBridgeGetMetrics(_handle, out metrics);
        return res != 0;
    }

    /// <summary>
    /// Enables Windows Multimedia Class Scheduler Service (MMCSS) "Pro Audio" high-priority scheduling.
    /// </summary>
    public bool TryEnableMmcss()
    {
        ThrowIfDisposed();
        return MoonshineNativeMethods.AudioIpcBridgeEnableMmcss(_handle) != 0;
    }

    /// <summary>
    /// Reverts MMCSS scheduling priority back to normal.
    /// </summary>
    public void RevertMmcss()
    {
        if (!_disposed && _handle != IntPtr.Zero)
        {
            MoonshineNativeMethods.AudioIpcBridgeRevertMmcss(_handle);
        }
    }

    /// <summary>
    /// Performs a smooth micro crossfade exponential gain ramp on audio buffers when switching backends.
    /// </summary>
    public static void ApplyCrossfade(Span<float> outgoingBuffer, Span<float> incomingBuffer, Span<float> destination)
    {
        int length = Math.Min(destination.Length, Math.Min(outgoingBuffer.Length, incomingBuffer.Length));
        for (int i = 0; i < length; i++)
        {
            float progress = (float)i / (float)length;
            float gainIncoming = progress * progress; // Quadratic smooth ramp up
            float gainOutgoing = 1.0f - gainIncoming; // Complementary ramp down
            destination[i] = (outgoingBuffer[i] * gainOutgoing) + (incomingBuffer[i] * gainIncoming);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _handle == IntPtr.Zero, this);
    }

    /// <summary>
    /// Finalizer for unmanaged resource cleanup.
    /// </summary>
    ~VirtualAudioIpcBridgePipeline()
    {
        Dispose(false);
    }

    /// <summary>
    /// Releases all native unmanaged resources.
    /// </summary>
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
                MoonshineNativeMethods.AudioIpcBridgeDestroy(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
