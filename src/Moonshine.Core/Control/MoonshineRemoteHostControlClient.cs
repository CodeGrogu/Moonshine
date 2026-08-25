using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Moonshine.Core.Security;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Core.Control;

/// <summary>
/// Delegate for sending raw control datagrams over custom transport bindings.
/// </summary>
/// <param name="datagram">The datagram memory buffer to transmit.</param>
/// <param name="cancellationToken">Cancellation token for the transmission.</param>
/// <returns>A value task representing the asynchronous send operation.</returns>
public delegate ValueTask ControlPacketSender(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);

/// <summary>
/// Production client for the Moonshine 0x0800 Host Management and Remote Configuration protocol.
/// Provides asynchronous query and mutation primitives for host capabilities and runtime configurations.
/// Handles correlated request-response matching, cancellation, and configuration change notifications.
/// </summary>
public sealed class MoonshineRemoteHostControlClient : IDisposable
{
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<MoonshineHostCapabilitiesResponsePayload>> _pendingCapabilities = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<MoonshineHostConfigurationPayload>> _pendingConfiguration = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<(MoonshineErrorCode StatusCode, uint AppliedVersion)>> _pendingSetConfiguration = new();

    private readonly ControlPacketSender? _customSender;
    private readonly MoonshineSessionAuthenticator? _authenticator;
    private uint _sequenceNumber;
    private bool _disposed;

    /// <summary>
    /// Event raised when the remote host emits a proactive configuration changed notification (0x0807).
    /// </summary>
    [SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Explicit protocol-aligned delegate signature requested for high-performance zero-allocation dispatch.")]
    public event Action<MoonshineConfigurationChangedPayload>? ConfigurationChanged;

    /// <summary>
    /// Gets or sets the underlying control UDP socket.
    /// </summary>
    public Socket? Socket { get; set; }

    /// <summary>
    /// Gets or sets the remote host control endpoint.
    /// </summary>
    public IPEndPoint? RemoteEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the active streaming session identifier.
    /// </summary>
    public ulong SessionId { get; set; }

    /// <summary>
    /// Gets the optional session authenticator for packet signing and replay validation.
    /// </summary>
    public MoonshineSessionAuthenticator? Authenticator => _authenticator;

    /// <summary>
    /// Initialises a new instance of the <see cref="MoonshineRemoteHostControlClient"/> class.
    /// </summary>
    /// <param name="socket">Optional UDP socket used for transmission.</param>
    /// <param name="remoteEndpoint">Optional remote host control endpoint destination.</param>
    /// <param name="sessionId">Optional session identifier.</param>
    /// <param name="customSender">Optional custom sender delegate for mock or pipeline routing.</param>
    /// <param name="authenticator">Optional session authenticator for message signing.</param>
    public MoonshineRemoteHostControlClient(
        Socket? socket = null,
        IPEndPoint? remoteEndpoint = null,
        ulong sessionId = 0,
        ControlPacketSender? customSender = null,
        MoonshineSessionAuthenticator? authenticator = null)
    {
        Socket = socket;
        RemoteEndpoint = remoteEndpoint;
        SessionId = sessionId;
        _customSender = customSender;
        _authenticator = authenticator;
    }

    /// <summary>
    /// Requests the remote host hardware capabilities asynchronously via 0x0801 GetHostCapabilities.
    /// </summary>
    /// <param name="queryMask">Bitmask specifying requested capability categories (0 for full set).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task resolving to the advertised host capabilities response payload.</returns>
    public async Task<MoonshineHostCapabilitiesResponsePayload> GetCapabilitiesAsync(uint queryMask = 0, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint seq = unchecked((uint)Interlocked.Increment(ref _sequenceNumber));
        var tcs = new TaskCompletionSource<MoonshineHostCapabilitiesResponsePayload>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingCapabilities[seq] = tcs;

        CancellationTokenRegistration registration = default;
        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (_pendingCapabilities.TryRemove(seq, out var pending))
                {
                    pending.TrySetCanceled(ct);
                }
            });
        }

        try
        {
            uint payloadSize = _authenticator != null ? 36u : 4u;
            byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + (int)payloadSize];
            var header = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.GetHostCapabilities,
                PayloadSize: payloadSize,
                SequenceNumber: seq,
                SessionId: SessionId,
                TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

            MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
            MoonshineProtocolCodec.TryWriteGetHostCapabilities(queryMask, buffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));

            if (_authenticator != null)
            {
                _authenticator.ComputeMessageAuthTag(
                    buffer.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4),
                    buffer.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));
            }

            await SendPacketAsync(buffer, ct).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pendingCapabilities.TryRemove(seq, out _);
            throw;
        }
        finally
        {
            registration.Dispose();
        }
    }

    /// <summary>
    /// Queries the current active host configuration asynchronously via 0x0803 GetHostConfiguration.
    /// </summary>
    /// <param name="queryScope">Bitmask specifying requested configuration subsystems (0 for all).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task resolving to the active host configuration payload.</returns>
    public async Task<MoonshineHostConfigurationPayload> GetConfigurationAsync(uint queryScope = 0, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint seq = unchecked((uint)Interlocked.Increment(ref _sequenceNumber));
        var tcs = new TaskCompletionSource<MoonshineHostConfigurationPayload>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingConfiguration[seq] = tcs;

        CancellationTokenRegistration registration = default;
        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (_pendingConfiguration.TryRemove(seq, out var pending))
                {
                    pending.TrySetCanceled(ct);
                }
            });
        }

        try
        {
            uint payloadSize = _authenticator != null ? 36u : 4u;
            byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + (int)payloadSize];
            var header = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.GetHostConfiguration,
                PayloadSize: payloadSize,
                SequenceNumber: seq,
                SessionId: SessionId,
                TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

            MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
            MoonshineProtocolCodec.TryWriteGetHostConfiguration(queryScope, buffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 4));

            if (_authenticator != null)
            {
                _authenticator.ComputeMessageAuthTag(
                    buffer.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 4),
                    buffer.AsSpan(MoonshineProtocolConstants.HeaderSize + 4, 32));
            }

            await SendPacketAsync(buffer, ct).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pendingConfiguration.TryRemove(seq, out _);
            throw;
        }
        finally
        {
            registration.Dispose();
        }
    }

    /// <summary>
    /// Proposes a new host configuration asynchronously via 0x0805 SetHostConfiguration.
    /// </summary>
    /// <param name="proposed">The proposed configuration settings payload.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task resolving to the status code and applied configuration version.</returns>
    public async Task<(MoonshineErrorCode StatusCode, uint AppliedVersion)> SetConfigurationAsync(
        MoonshineHostConfigurationPayload proposed,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint seq = unchecked((uint)Interlocked.Increment(ref _sequenceNumber));
        var tcs = new TaskCompletionSource<(MoonshineErrorCode StatusCode, uint AppliedVersion)>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingSetConfiguration[seq] = tcs;

        CancellationTokenRegistration registration = default;
        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                if (_pendingSetConfiguration.TryRemove(seq, out var pending))
                {
                    pending.TrySetCanceled(ct);
                }
            });
        }

        try
        {
            uint payloadSize = _authenticator != null ? 80u : 48u;
            byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + (int)payloadSize];
            var header = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.SetHostConfiguration,
                PayloadSize: payloadSize,
                SequenceNumber: seq,
                SessionId: SessionId,
                TimestampUs: (ulong)((Stopwatch.GetTimestamp() * 1_000_000L) / Stopwatch.Frequency));

            MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
            MoonshineProtocolCodec.TryWriteHostConfiguration(in proposed, buffer.AsSpan(MoonshineProtocolConstants.HeaderSize, 48));

            if (_authenticator != null)
            {
                _authenticator.ComputeMessageAuthTag(
                    buffer.AsSpan(0, MoonshineProtocolConstants.HeaderSize + 48),
                    buffer.AsSpan(MoonshineProtocolConstants.HeaderSize + 48, 32));
            }

            await SendPacketAsync(buffer, ct).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pendingSetConfiguration.TryRemove(seq, out _);
            throw;
        }
        finally
        {
            registration.Dispose();
        }
    }

    /// <summary>
    /// Processes an incoming control message datagram, dispatching responses to pending requests or firing configuration change events.
    /// </summary>
    /// <param name="datagram">The received raw UDP datagram buffer.</param>
    public void ProcessIncomingControlMessage(ReadOnlySpan<byte> datagram)
    {
        if (_disposed || datagram.Length < MoonshineProtocolConstants.HeaderSize)
        {
            return;
        }

        MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(datagram, out var header);
        if (err != MoonshineErrorCode.Success || header.Magic != MoonshineProtocolConstants.Magic)
        {
            return;
        }

        if (SessionId != 0 && header.SessionId != 0 && header.SessionId != SessionId)
        {
            return;
        }

        if (datagram.Length < MoonshineProtocolConstants.HeaderSize + header.PayloadSize)
        {
            return;
        }

        ReadOnlySpan<byte> payload = datagram.Slice(MoonshineProtocolConstants.HeaderSize, (int)header.PayloadSize);

        if (_authenticator != null && header.PayloadSize > 32)
        {
            int authTagOffset = (int)header.PayloadSize - 32;
            ReadOnlySpan<byte> signedPortion = datagram[..(MoonshineProtocolConstants.HeaderSize + authTagOffset)];
            ReadOnlySpan<byte> expectedTag = payload[authTagOffset..];
            Span<byte> computedTag = stackalloc byte[32];
            _authenticator.ComputeMessageAuthTag(signedPortion, computedTag);
            if (!computedTag.SequenceEqual(expectedTag))
            {
                return;
            }
            payload = payload[..authTagOffset];
        }

        switch (header.MessageType)

        {
            case MoonshineMessageType.HostCapabilitiesResponse:
                if (MoonshineProtocolCodec.TryReadHostCapabilitiesResponse(payload, out var capabilities) == MoonshineErrorCode.Success)
                {
                    if (_pendingCapabilities.TryRemove(header.SequenceNumber, out var tcs))
                    {
                        tcs.TrySetResult(capabilities);
                    }
                }
                break;

            case MoonshineMessageType.HostConfigurationResponse:
                if (MoonshineProtocolCodec.TryReadHostConfiguration(payload, out var config) == MoonshineErrorCode.Success)
                {
                    if (_pendingConfiguration.TryRemove(header.SequenceNumber, out var tcs))
                    {
                        tcs.TrySetResult(config);
                    }
                }
                break;

            case MoonshineMessageType.SetHostConfigurationResponse:
                if (MoonshineProtocolCodec.TryReadSetHostConfigurationResponse(payload, out var setResp) == MoonshineErrorCode.Success)
                {
                    var result = (setResp.StatusCode, setResp.AppliedConfigVersion);
                    if (_pendingSetConfiguration.TryRemove(header.SequenceNumber, out var tcs))
                    {
                        tcs.TrySetResult(result);
                    }
                }
                break;

            case MoonshineMessageType.ConfigurationChanged:
                if (MoonshineProtocolCodec.TryReadConfigurationChanged(payload, out var changedPayload) == MoonshineErrorCode.Success)
                {
                    ConfigurationChanged?.Invoke(changedPayload);
                }
                break;
        }
    }

    private async ValueTask SendPacketAsync(ReadOnlyMemory<byte> datagram, CancellationToken ct)
    {
        if (_customSender != null)
        {
            await _customSender(datagram, ct).ConfigureAwait(false);
            return;
        }

        if (Socket != null && RemoteEndpoint != null)
        {
            await Socket.SendToAsync(datagram, SocketFlags.None, RemoteEndpoint, ct).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("No socket or transmission callback configured for host control client.");
    }

    /// <summary>
    /// Performs client-side pre-flight validation of a proposed host configuration against advertised host capabilities.
    /// </summary>
    /// <param name="proposed">The proposed configuration settings payload to validate.</param>
    /// <param name="capabilities">The advertised host capabilities payload to validate against.</param>
    /// <param name="errorCode">When validation fails, receives the specific error code; otherwise <see cref="MoonshineErrorCode.Success"/>.</param>
    /// <param name="failureReason">When validation fails, receives a descriptive explanation; otherwise null.</param>
    /// <returns>True if the configuration is valid and compatible with the host capabilities; otherwise false.</returns>
    public static bool ValidateProposedConfiguration(
        in MoonshineHostConfigurationPayload proposed,
        in MoonshineHostCapabilitiesResponsePayload capabilities,
        out MoonshineErrorCode errorCode,
        out string? failureReason)
    {
        // 1. Dimensions validation
        if (proposed.DisplayWidth == 0 || proposed.DisplayHeight == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "Display dimensions must be greater than zero.";
            return false;
        }

        if (capabilities.MaxEncodeWidth > 0 && proposed.DisplayWidth > capabilities.MaxEncodeWidth)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Requested display width ({proposed.DisplayWidth}) exceeds maximum supported encode width ({capabilities.MaxEncodeWidth}).";
            return false;
        }

        if (capabilities.MaxEncodeHeight > 0 && proposed.DisplayHeight > capabilities.MaxEncodeHeight)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Requested display height ({proposed.DisplayHeight}) exceeds maximum supported encode height ({capabilities.MaxEncodeHeight}).";
            return false;
        }

        // 2. Refresh rate validation
        if (proposed.RefreshRateHz == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "Refresh rate must be greater than zero.";
            return false;
        }

        if (capabilities.MaxEncodeFps > 0 && proposed.RefreshRateHz > capabilities.MaxEncodeFps)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Requested refresh rate ({proposed.RefreshRateHz} Hz) exceeds maximum supported encode frame rate ({capabilities.MaxEncodeFps} fps).";
            return false;
        }

        // 3. Bitrate validation
        if (proposed.TargetBitrateKbps == 0 || proposed.MaxBitrateKbps == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "Target and maximum bitrates must be greater than zero.";
            return false;
        }

        if (proposed.TargetBitrateKbps > proposed.MaxBitrateKbps)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Target bitrate ({proposed.TargetBitrateKbps} kbps) cannot exceed maximum bitrate ({proposed.MaxBitrateKbps} kbps).";
            return false;
        }

        if (capabilities.MaxBitrateKbps > 0)
        {
            if (proposed.TargetBitrateKbps > capabilities.MaxBitrateKbps)
            {
                errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
                failureReason = $"Target bitrate ({proposed.TargetBitrateKbps} kbps) exceeds maximum host bitrate capability ({capabilities.MaxBitrateKbps} kbps).";
                return false;
            }

            if (proposed.MaxBitrateKbps > capabilities.MaxBitrateKbps)
            {
                errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
                failureReason = $"Maximum bitrate ({proposed.MaxBitrateKbps} kbps) exceeds maximum host bitrate capability ({capabilities.MaxBitrateKbps} kbps).";
                return false;
            }
        }

        // 4. Video codec support validation
        if (!IsCodecSupported(capabilities.SupportedVideoCodecs, proposed.PreferredCodec))
        {
            errorCode = MoonshineErrorCode.UnsupportedCodec;
            failureReason = $"Requested video codec ({proposed.PreferredCodec}) is not supported by host capabilities.";
            return false;
        }

        // 5. HDR10 support validation
        if (proposed.Hdr10Enabled != 0 && capabilities.SupportsHdr10 == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "HDR10 mode is requested but host hardware does not support HDR10 encoding.";
            return false;
        }

        // 6. Audio channels validation (strictly 2, 6, or 8 channels)
        if (proposed.AudioChannels is not (2 or 6 or 8))
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Audio channel count ({proposed.AudioChannels}) is invalid. Only 2, 6, or 8 channels are supported.";
            return false;
        }

        // 7. Audio bitrate validation (32 kbps to 1024 kbps)
        if (proposed.AudioBitrateKbps is < 32 or > 1024)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = $"Audio bitrate ({proposed.AudioBitrateKbps} kbps) is out of range. Allowed range is 32 to 1024 kbps.";
            return false;
        }

        // 8. Microphone backchannel validation
        if (proposed.MicPassthroughEnabled != 0 && capabilities.SupportsMicBackchannel == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "Microphone passthrough backchannel is requested but host does not support microphone backchannel.";
            return false;
        }

        // 9. Virtual audio driver validation
        if (proposed.VirtualAudioDriverEnabled != 0 && capabilities.SupportsVirtualAudio == 0)
        {
            errorCode = MoonshineErrorCode.InvalidConfigurationParameter;
            failureReason = "Virtual audio driver is requested but host does not support virtual audio driver.";
            return false;
        }

        errorCode = MoonshineErrorCode.Success;
        failureReason = null;
        return true;
    }

    private static bool IsCodecSupported(uint supportedVideoCodecsMask, MoonshineVideoCodec codec)
    {
        if (codec is MoonshineVideoCodec.Unknown or > MoonshineVideoCodec.H264)
        {
            return false;
        }

        uint capBit = codec switch
        {
            MoonshineVideoCodec.Av1 => (uint)MoonshineCapabilities.Av1,
            MoonshineVideoCodec.Hevc => (uint)MoonshineCapabilities.Hevc,
            MoonshineVideoCodec.H264 => (uint)MoonshineCapabilities.H264,
            _ => 0
        };

        uint directBit = 1u << (int)codec;
        return (supportedVideoCodecsMask & capBit) != 0 || (supportedVideoCodecsMask & directBit) != 0;
    }

    /// <summary>
    /// Disposes the control client and cancels any pending asynchronous requests.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _pendingCapabilities)
        {
            if (_pendingCapabilities.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }

        foreach (var kvp in _pendingConfiguration)
        {
            if (_pendingConfiguration.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }

        foreach (var kvp in _pendingSetConfiguration)
        {
            if (_pendingSetConfiguration.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }
    }
}

