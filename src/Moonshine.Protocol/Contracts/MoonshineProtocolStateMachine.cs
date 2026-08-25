using System.Runtime.CompilerServices;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Contracts;

/// <summary>
/// States for the Moonshine Native Binary Protocol (MNBP v1) session state machine.
/// </summary>
public enum MoonshineProtocolState
{
    /// <summary>Session initialised but no handshake has been exchanged.</summary>
    Created = 0,

    /// <summary>Hello message transmitted; awaiting HelloResponse.</summary>
    HandshakeInitiated = 1,

    /// <summary>HelloResponse received; session ID and challenge salt established.</summary>
    HandshakeCompleted = 2,

    /// <summary>SessionSetup transmitted; negotiating media stream parameters and ports.</summary>
    SessionNegotiating = 3,

    /// <summary>Session negotiated; media streaming, audio playback, and input active.</summary>
    StreamingActive = 4,

    /// <summary>Streaming active but experiencing packet loss or network degradation.</summary>
    StreamingDegraded = 5,

    /// <summary>Gracefully stopping streaming and tearing down active channels.</summary>
    Draining = 6,

    /// <summary>Session terminated and all channel contexts closed.</summary>
    Closed = 7,

    /// <summary>Unrecoverable error, timeout, or protocol violation occurred (fail-closed state).</summary>
    Faulted = 8
}

/// <summary>
/// Hardened zero-allocation state machine enforcing deterministic MNBP v1 protocol sequencing,
/// out-of-order handshake rejection, connection timeout detection, and fail-closed state invariants.
/// </summary>
public sealed class MoonshineProtocolStateMachine
{
    public const ulong DefaultConnectionTimeoutUs = 5_000_000; // 5 seconds
    public const uint DefaultMaxMtu = 65507;
    public const uint DefaultMinMtu = 576;
    public const int MaxHandshakeExaminedPackets = 256;
    public const int MaxHandshakeMalformedPackets = 32;

    private readonly object _syncLock = new();
    private MoonshineProtocolState _state = MoonshineProtocolState.Created;
    private string? _faultReason;

    public MoonshineProtocolState State
    {
        get
        {
            lock (_syncLock) return _state;
        }
    }

    public string? FaultReason
    {
        get
        {
            lock (_syncLock) return _faultReason;
        }
    }

    public ulong SessionId { get; private set; }
    public uint VideoStreamId { get; private set; }
    public uint AudioStreamId { get; private set; }
    public uint FeedbackStreamId { get; private set; }
    public uint NegotiatedMtu { get; private set; } = 1188;
    public ulong ConnectionTimeoutUs { get; set; } = DefaultConnectionTimeoutUs;

    public ulong LastActivityTimestampUs { get; private set; }
    public uint LastReceivedSequenceNumber { get; private set; }
    public ulong LastReceivedFrameIndex { get; private set; }
    public bool IsStrictSequenceEnforced { get; set; }

    public bool IsOperational => State is MoonshineProtocolState.StreamingActive or MoonshineProtocolState.StreamingDegraded;
    public bool IsTerminated => State is MoonshineProtocolState.Closed or MoonshineProtocolState.Faulted;

    public MoonshineProtocolStateMachine(ulong initialSessionId = 0, uint initialMtu = 1188)
    {
        SessionId = initialSessionId;
        NegotiatedMtu = (initialMtu >= DefaultMinMtu && initialMtu <= DefaultMaxMtu) ? initialMtu : 1188;
    }

    /// <summary>
    /// Processes an incoming or outgoing envelope header, validating state sequencing, MTU limits,
    /// session identity, anti-replay sequence ordering, and connection timeouts.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MoonshineErrorCode IngestPacketHeader(in MoonshinePacketHeader header, ulong currentTimestampUs)
    {
        lock (_syncLock)
        {
            // 1. Fail-closed check: Terminated or faulted states reject all further traffic
            if (_state == MoonshineProtocolState.Faulted)
            {
                return MoonshineErrorCode.InvalidSession;
            }

            if (_state == MoonshineProtocolState.Closed)
            {
                return MoonshineErrorCode.InvalidSession;
            }

            // 2. Validate header magic and version
            if (header.Magic != MoonshineProtocolConstants.Magic)
            {
                TransitionToFaultedLocked("Invalid magic bytes in envelope header.");
                return MoonshineErrorCode.InvalidMagic;
            }

            if (header.Version != MoonshineProtocolConstants.Version10)
            {
                TransitionToFaultedLocked("Unsupported protocol version.");
                return MoonshineErrorCode.UnsupportedVersion;
            }

            // 3. Connection timeout check
            if (LastActivityTimestampUs > 0 && currentTimestampUs > LastActivityTimestampUs)
            {
                ulong elapsedUs = currentTimestampUs - LastActivityTimestampUs;
                if (elapsedUs > ConnectionTimeoutUs && _state is not MoonshineProtocolState.Created and not MoonshineProtocolState.Closed)
                {
                    TransitionToFaultedLocked($"Connection timed out after {elapsedUs} us without activity.");
                    return MoonshineErrorCode.StaleTimestamp;
                }
            }

            // 4. Session ID validation (explicit contract check)
            if (MoonshineProtocolCodec.RequiresSessionId(header.MessageType))
            {
                if (_state >= MoonshineProtocolState.HandshakeCompleted && SessionId != 0)
                {
                    if (header.SessionId != SessionId)
                    {
                        TransitionToFaultedLocked($"Session ID mismatch: expected {SessionId}, got {header.SessionId}.");
                        return MoonshineErrorCode.InvalidSession;
                    }
                }
            }

            // 5. Sequence number validation (anti-replay and RFC 1982 modular progression)
            if (IsStrictSequenceEnforced && LastReceivedSequenceNumber > 0)
            {
                if (!MoonshineProtocolCodec.IsNewerSequence(header.SequenceNumber, LastReceivedSequenceNumber))
                {
                    return MoonshineErrorCode.DuplicateSequence;
                }
            }

            // 6. MTU payload ceiling check
            if (header.PayloadSize > NegotiatedMtu && header.MessageType is MoonshineMessageType.VideoPacket or MoonshineMessageType.AudioPacket or MoonshineMessageType.MicPacket)
            {
                TransitionToFaultedLocked($"Datagram payload size {header.PayloadSize} exceeds negotiated MTU ceiling {NegotiatedMtu}.");
                return MoonshineErrorCode.MalformedHeader;
            }

            // 7. State-specific message sequencing validation
            MoonshineErrorCode stateError = ValidateMessageForStateLocked(header.MessageType);
            if (stateError != MoonshineErrorCode.Success)
            {
                TransitionToFaultedLocked($"Out-of-order message {header.MessageType} received during state {_state}.");
                return stateError;
            }

            // Update monotonic tracking metrics with modular rollover awareness
            LastActivityTimestampUs = currentTimestampUs;
            if (LastReceivedSequenceNumber == 0 || MoonshineProtocolCodec.IsNewerSequence(header.SequenceNumber, LastReceivedSequenceNumber))
            {
                LastReceivedSequenceNumber = header.SequenceNumber;
            }

            return MoonshineErrorCode.Success;
        }
    }

    /// <summary>
    /// Processes feedback loss statistics, verifying that frame index does not regress within an active stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MoonshineErrorCode IngestFeedbackLossStats(in MoonshineFeedbackLossStatsPayload feedback, ulong currentTimestampUs)
    {
        lock (_syncLock)
        {
            if (_state != MoonshineProtocolState.StreamingActive && _state != MoonshineProtocolState.StreamingDegraded)
            {
                return MoonshineErrorCode.InvalidSession;
            }

            if (feedback.StreamId == 0)
            {
                return MoonshineErrorCode.StreamNotFound;
            }

            if (VideoStreamId != 0 && feedback.StreamId != VideoStreamId)
            {
                return MoonshineErrorCode.StreamNotFound;
            }

            // Stale feedback filtering: monotonic stream horizon invariant with rollover safety
            if (LastReceivedFrameIndex > 0 && !MoonshineProtocolCodec.IsNewerFrameIndex(feedback.LastReceivedFrameIndex, LastReceivedFrameIndex))
            {
                return MoonshineErrorCode.StaleTimestamp;
            }

            LastReceivedFrameIndex = feedback.LastReceivedFrameIndex;
            LastActivityTimestampUs = currentTimestampUs;
            return MoonshineErrorCode.Success;
        }
    }

    /// <summary>
    /// Evaluates if a given message type is permitted in the current protocol state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MoonshineErrorCode ValidateMessageForStateLocked(MoonshineMessageType messageType)
    {
        // Host configuration and discovery messages are allowed across all operational states
        if (messageType is MoonshineMessageType.DiscoveryProbe or MoonshineMessageType.DiscoveryAnnouncement or MoonshineMessageType.DiscoveryResponse or
            MoonshineMessageType.GetHostCapabilities or MoonshineMessageType.HostCapabilitiesResponse or
            MoonshineMessageType.GetHostConfiguration or MoonshineMessageType.HostConfigurationResponse or
            MoonshineMessageType.SetHostConfiguration or MoonshineMessageType.SetHostConfigurationResponse or
            MoonshineMessageType.ConfigurationChanged)
        {
            if (_state is not MoonshineProtocolState.Closed and not MoonshineProtocolState.Faulted)
            {
                return MoonshineErrorCode.Success;
            }
        }

        switch (_state)
        {
            case MoonshineProtocolState.Created:
                if (messageType is MoonshineMessageType.Hello)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.InvalidSession;

            case MoonshineProtocolState.HandshakeInitiated:
                if (messageType is MoonshineMessageType.HelloResponse or MoonshineMessageType.Hello or MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.MalformedHeader;

            case MoonshineProtocolState.HandshakeCompleted:
                if (messageType is MoonshineMessageType.SessionSetup or MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck or MoonshineMessageType.Teardown)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.MalformedHeader;

            case MoonshineProtocolState.SessionNegotiating:
                if (messageType is MoonshineMessageType.SessionSetupResponse or MoonshineMessageType.SessionSetup or MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck or MoonshineMessageType.Teardown)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.MalformedHeader;

            case MoonshineProtocolState.StreamingActive:
            case MoonshineProtocolState.StreamingDegraded:
                if (messageType is MoonshineMessageType.VideoPacket or MoonshineMessageType.AudioPacket or
                    MoonshineMessageType.MicPacket or MoonshineMessageType.FeedbackLossStats or
                    MoonshineMessageType.IdrRequest or MoonshineMessageType.InputKeyboard or
                    MoonshineMessageType.InputMouse or MoonshineMessageType.InputGamepad or
                    MoonshineMessageType.TelemetryReport or MoonshineMessageType.KeepAlive or
                    MoonshineMessageType.KeepAliveAck or MoonshineMessageType.GetHostCapabilities or
                    MoonshineMessageType.HostCapabilitiesResponse or MoonshineMessageType.GetHostConfiguration or
                    MoonshineMessageType.HostConfigurationResponse or MoonshineMessageType.SetHostConfiguration or
                    MoonshineMessageType.SetHostConfigurationResponse or MoonshineMessageType.ConfigurationChanged or
                    MoonshineMessageType.Teardown)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.MalformedHeader;

            case MoonshineProtocolState.Draining:
                if (messageType is MoonshineMessageType.Teardown or MoonshineMessageType.KeepAlive or MoonshineMessageType.KeepAliveAck)
                {
                    return MoonshineErrorCode.Success;
                }
                return MoonshineErrorCode.InvalidSession;

            case MoonshineProtocolState.Closed:
            case MoonshineProtocolState.Faulted:
            default:
                return MoonshineErrorCode.InvalidSession;
        }
    }

    /// <summary>
    /// Advances state to HandshakeInitiated upon sending Hello.
    /// </summary>
    public bool RecordHelloSent()
    {
        lock (_syncLock)
        {
            if (_state != MoonshineProtocolState.Created) return false;
            _state = MoonshineProtocolState.HandshakeInitiated;
            return true;
        }
    }

    /// <summary>
    /// Advances state to HandshakeCompleted upon receiving a valid HelloResponse.
    /// </summary>
    public bool RecordHelloResponseReceived(ulong assignedSessionId)
    {
        lock (_syncLock)
        {
            if (_state != MoonshineProtocolState.HandshakeInitiated && _state != MoonshineProtocolState.Created)
            {
                TransitionToFaultedLocked("HelloResponse received out of order.");
                return false;
            }

            if (assignedSessionId == 0)
            {
                TransitionToFaultedLocked("HelloResponse contained invalid zero session ID.");
                return false;
            }

            SessionId = assignedSessionId;
            _state = MoonshineProtocolState.HandshakeCompleted;
            return true;
        }
    }

    /// <summary>
    /// Advances state to SessionNegotiating upon transmitting SessionSetup.
    /// </summary>
    public bool RecordSessionSetupSent()
    {
        lock (_syncLock)
        {
            if (_state != MoonshineProtocolState.HandshakeCompleted)
            {
                TransitionToFaultedLocked("SessionSetup initiated before completing handshake.");
                return false;
            }

            _state = MoonshineProtocolState.SessionNegotiating;
            return true;
        }
    }

    /// <summary>
    /// Advances state to StreamingActive upon receiving a successful SessionSetupResponse.
    /// </summary>
    public bool RecordSessionSetupResponseReceived(uint videoStreamId, uint audioStreamId, uint feedbackStreamId, uint negotiatedMtu)
    {
        lock (_syncLock)
        {
            if (_state != MoonshineProtocolState.SessionNegotiating)
            {
                TransitionToFaultedLocked("SessionSetupResponse received without active negotiation.");
                return false;
            }

            if (videoStreamId == 0 || audioStreamId == 0)
            {
                TransitionToFaultedLocked("SessionSetupResponse contained invalid zero stream IDs.");
                return false;
            }

            if (negotiatedMtu < DefaultMinMtu || negotiatedMtu > DefaultMaxMtu)
            {
                TransitionToFaultedLocked($"Negotiated MTU {negotiatedMtu} violates MTU boundaries.");
                return false;
            }

            VideoStreamId = videoStreamId;
            AudioStreamId = audioStreamId;
            FeedbackStreamId = feedbackStreamId;
            NegotiatedMtu = negotiatedMtu;
            _state = MoonshineProtocolState.StreamingActive;
            return true;
        }
    }

    /// <summary>
    /// Transitions state to StreamingDegraded upon sustained packet loss or jitter spikes.
    /// </summary>
    public bool SetDegraded(bool isDegraded)
    {
        lock (_syncLock)
        {
            if (_state == MoonshineProtocolState.StreamingActive && isDegraded)
            {
                _state = MoonshineProtocolState.StreamingDegraded;
                return true;
            }
            if (_state == MoonshineProtocolState.StreamingDegraded && !isDegraded)
            {
                _state = MoonshineProtocolState.StreamingActive;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Initiates session teardown, transitioning to Draining.
    /// </summary>
    public bool RecordTeardown()
    {
        lock (_syncLock)
        {
            if (_state is MoonshineProtocolState.Closed or MoonshineProtocolState.Faulted) return false;
            _state = MoonshineProtocolState.Draining;
            return true;
        }
    }

    /// <summary>
    /// Closes the session state machine cleanly.
    /// </summary>
    public void Close()
    {
        lock (_syncLock)
        {
            _state = MoonshineProtocolState.Closed;
        }
    }

    /// <summary>
    /// Forces the state machine into the fail-closed Faulted state.
    /// </summary>
    public void Fault(string reason)
    {
        lock (_syncLock)
        {
            TransitionToFaultedLocked(reason);
        }
    }

    /// <summary>
    /// Resets the state machine back to Created for a clean session reconnection.
    /// </summary>
    public void Reset(ulong newSessionId = 0)
    {
        lock (_syncLock)
        {
            _state = MoonshineProtocolState.Created;
            _faultReason = null;
            SessionId = newSessionId;
            VideoStreamId = 0;
            AudioStreamId = 0;
            FeedbackStreamId = 0;
            LastActivityTimestampUs = 0;
            LastReceivedSequenceNumber = 0;
            LastReceivedFrameIndex = 0;
        }
    }

    private void TransitionToFaultedLocked(string reason)
    {
        _state = MoonshineProtocolState.Faulted;
        _faultReason = reason;
    }
}
