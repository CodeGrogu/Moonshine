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
            byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + 4];
            var header = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.GetHostCapabilities,
                PayloadSize: 4,
                SequenceNumber: seq,
                SessionId: SessionId,
                TimestampUs: (ulong)Stopwatch.GetTimestamp());

            MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
            MoonshineProtocolCodec.TryWriteGetHostCapabilities(queryMask, buffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

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
            byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + 4];
            var header = new MoonshinePacketHeader(
                Magic: MoonshineProtocolConstants.Magic,
                Version: MoonshineProtocolConstants.Version10,
                MessageType: MoonshineMessageType.GetHostConfiguration,
                PayloadSize: 4,
                SequenceNumber: seq,
                SessionId: SessionId,
                TimestampUs: (ulong)Stopwatch.GetTimestamp());

            MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
            MoonshineProtocolCodec.TryWriteGetHostConfiguration(queryScope, buffer.AsSpan(MoonshineProtocolConstants.HeaderSize));

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
                TimestampUs: (ulong)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency));

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

        ReadOnlySpan<byte> payload = datagram[MoonshineProtocolConstants.HeaderSize..];

        switch (header.MessageType)
        {
            case MoonshineMessageType.HostCapabilitiesResponse:
                if (MoonshineProtocolCodec.TryReadHostCapabilitiesResponse(payload, out var capabilities) == MoonshineErrorCode.Success)
                {
                    if (_pendingCapabilities.TryRemove(header.SequenceNumber, out var tcs))
                    {
                        tcs.TrySetResult(capabilities);
                    }
                    else
                    {
                        foreach (var kvp in _pendingCapabilities)
                        {
                            if (_pendingCapabilities.TryRemove(kvp.Key, out var fallbackTcs))
                            {
                                fallbackTcs.TrySetResult(capabilities);
                                break;
                            }
                        }
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
                    else
                    {
                        foreach (var kvp in _pendingConfiguration)
                        {
                            if (_pendingConfiguration.TryRemove(kvp.Key, out var fallbackTcs))
                            {
                                fallbackTcs.TrySetResult(config);
                                break;
                            }
                        }
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
                    else
                    {
                        foreach (var kvp in _pendingSetConfiguration)
                        {
                            if (_pendingSetConfiguration.TryRemove(kvp.Key, out var fallbackTcs))
                            {
                                fallbackTcs.TrySetResult(result);
                                break;
                            }
                        }
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
