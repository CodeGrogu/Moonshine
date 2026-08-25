using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class MoonshineProtocolStateMachineTests
{
    [Fact]
    public void StateMachine_InitialState_IsCreated()
    {
        var fsm = new MoonshineProtocolStateMachine();
        fsm.State.Should().Be(MoonshineProtocolState.Created);
        fsm.IsOperational.Should().BeFalse();
        fsm.IsTerminated.Should().BeFalse();
        fsm.FaultReason.Should().BeNull();
    }

    [Fact]
    public void StateMachine_ValidHandshakeAndNegotiationLifecycle_TransitionsToStreamingActive()
    {
        var fsm = new MoonshineProtocolStateMachine();

        // 1. Send Hello -> HandshakeInitiated
        bool helloOk = fsm.RecordHelloSent();
        helloOk.Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.HandshakeInitiated);

        // 2. Receive HelloResponse -> HandshakeCompleted
        bool helloRespOk = fsm.RecordHelloResponseReceived(0x123456789ABCDEF0UL);
        helloRespOk.Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.HandshakeCompleted);
        fsm.SessionId.Should().Be(0x123456789ABCDEF0UL);

        // 3. Send SessionSetup -> SessionNegotiating
        bool setupOk = fsm.RecordSessionSetupSent();
        setupOk.Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.SessionNegotiating);

        // 4. Receive SessionSetupResponse -> StreamingActive
        bool setupRespOk = fsm.RecordSessionSetupResponseReceived(
            videoStreamId: 101,
            audioStreamId: 102,
            feedbackStreamId: 103,
            negotiatedMtu: 1188);

        setupRespOk.Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.StreamingActive);
        fsm.IsOperational.Should().BeTrue();
        fsm.VideoStreamId.Should().Be(101);
        fsm.AudioStreamId.Should().Be(102);
        fsm.FeedbackStreamId.Should().Be(103);
        fsm.NegotiatedMtu.Should().Be(1188);

        // 5. Ingest valid media packet in StreamingActive
        var videoHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 500,
            SequenceNumber: 1,
            SessionId: 0x123456789ABCDEF0UL,
            TimestampUs: 1000);

        MoonshineErrorCode ingestErr = fsm.IngestPacketHeader(in videoHeader, 1000);
        ingestErr.Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedSequenceNumber.Should().Be(1);

        // 6. Transition to degraded and recover
        fsm.SetDegraded(true).Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.StreamingDegraded);
        fsm.IsOperational.Should().BeTrue();

        fsm.SetDegraded(false).Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.StreamingActive);

        // 7. Teardown -> Draining -> Closed
        fsm.RecordTeardown().Should().BeTrue();
        fsm.State.Should().Be(MoonshineProtocolState.Draining);

        fsm.Close();
        fsm.State.Should().Be(MoonshineProtocolState.Closed);
        fsm.IsTerminated.Should().BeTrue();
    }

    [Fact]
    public void StateMachine_OutOfOrderSessionSetup_FailsClosed()
    {
        var fsm = new MoonshineProtocolStateMachine();

        // Attempt to start SessionSetup without Hello
        bool setupOk = fsm.RecordSessionSetupSent();
        setupOk.Should().BeFalse();
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
        fsm.FaultReason.Should().Contain("SessionSetup initiated before completing handshake");
        fsm.IsTerminated.Should().BeTrue();

        // Subsequent packets should fail closed
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.Hello,
            PayloadSize: 32,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 100);

        fsm.IngestPacketHeader(in header, 100).Should().Be(MoonshineErrorCode.InvalidSession);
    }

    [Fact]
    public void StateMachine_InvalidMagicAndVersion_TransitionsToFaulted()
    {
        var fsm = new MoonshineProtocolStateMachine();

        var badMagicHeader = new MoonshinePacketHeader(
            Magic: 0xDEADBEEF,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.Hello,
            PayloadSize: 32,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 100);

        fsm.IngestPacketHeader(in badMagicHeader, 100).Should().Be(MoonshineErrorCode.InvalidMagic);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);

        // Reset and test invalid version
        fsm.Reset();
        fsm.State.Should().Be(MoonshineProtocolState.Created);

        var badVersionHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: 0x0099,
            MessageType: MoonshineMessageType.Hello,
            PayloadSize: 32,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 100);

        fsm.IngestPacketHeader(in badVersionHeader, 100).Should().Be(MoonshineErrorCode.UnsupportedVersion);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
    }

    [Fact]
    public void StateMachine_SessionIdMismatch_TransitionsToFaulted()
    {
        var fsm = new MoonshineProtocolStateMachine();
        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x1111222233334444UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        // Message with mismatched session ID
        var wrongSessionHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 200,
            SequenceNumber: 1,
            SessionId: 0x9999999999999999UL,
            TimestampUs: 500);

        fsm.IngestPacketHeader(in wrongSessionHeader, 500).Should().Be(MoonshineErrorCode.InvalidSession);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
        fsm.FaultReason.Should().Contain("Session ID mismatch");
    }

    [Fact]
    public void StateMachine_OversizedMtuViolation_TransitionsToFaulted()
    {
        var fsm = new MoonshineProtocolStateMachine(initialMtu: 1188);
        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x5555UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        // Media packet exceeding negotiated MTU
        var oversizedHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 1400, // Exceeds 1188
            SequenceNumber: 1,
            SessionId: 0x5555UL,
            TimestampUs: 200);

        fsm.IngestPacketHeader(in oversizedHeader, 200).Should().Be(MoonshineErrorCode.MalformedHeader);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
        fsm.FaultReason.Should().Contain("exceeds negotiated MTU ceiling");
    }

    [Fact]
    public void StateMachine_ConnectionTimeout_FailsClosed()
    {
        var fsm = new MoonshineProtocolStateMachine
        {
            ConnectionTimeoutUs = 2_000_000 // 2 seconds
        };

        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x7777UL);

        var firstHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 0,
            SequenceNumber: 1,
            SessionId: 0x7777UL,
            TimestampUs: 1_000_000);

        fsm.IngestPacketHeader(in firstHeader, 1_000_000).Should().Be(MoonshineErrorCode.Success);

        // Advance timestamp by 3 seconds (exceeding 2s timeout)
        var timedOutHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 0,
            SequenceNumber: 2,
            SessionId: 0x7777UL,
            TimestampUs: 4_500_000);

        fsm.IngestPacketHeader(in timedOutHeader, 4_500_000).Should().Be(MoonshineErrorCode.StaleTimestamp);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
        fsm.FaultReason.Should().Contain("Connection timed out");
    }

    [Fact]
    public void StateMachine_StrictSequenceAntiReplay_RejectsDuplicates()
    {
        var fsm = new MoonshineProtocolStateMachine
        {
            IsStrictSequenceEnforced = true
        };

        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x8888UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        var pkt10 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 10,
            SessionId: 0x8888UL,
            TimestampUs: 100);

        fsm.IngestPacketHeader(in pkt10, 100).Should().Be(MoonshineErrorCode.Success);

        // Duplicate or older sequence number
        var pkt9 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 9,
            SessionId: 0x8888UL,
            TimestampUs: 110);

        fsm.IngestPacketHeader(in pkt9, 110).Should().Be(MoonshineErrorCode.DuplicateSequence);
    }

    [Fact]
    public void StateMachine_FeedbackMonotonicStreamHorizon_RejectsStaleFrameIndices()
    {
        var fsm = new MoonshineProtocolStateMachine();
        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x9999UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(videoStreamId: 1, audioStreamId: 2, feedbackStreamId: 3, negotiatedMtu: 1188);

        var fb1 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 500,
            PacketsReceived = 1000,
            RoundTripTimeUs = 5000
        };

        fsm.IngestFeedbackLossStats(in fb1, 1000).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedFrameIndex.Should().Be(500);

        // Progress forward
        var fb2 = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 505,
            PacketsReceived = 1050
        };
        fsm.IngestFeedbackLossStats(in fb2, 1050).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedFrameIndex.Should().Be(505);

        // Stale regressing feedback (delayed UDP packet from frame 490)
        var staleFb = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 490,
            PacketsReceived = 980
        };
        fsm.IngestFeedbackLossStats(in staleFb, 1060).Should().Be(MoonshineErrorCode.StaleTimestamp);
        fsm.LastReceivedFrameIndex.Should().Be(505); // Preserves monotonic horizon
    }

    [Fact]
    public void StateMachine_SequenceRollover_AcceptsWrappedSequenceAndRejectsStale()
    {
        var fsm = new MoonshineProtocolStateMachine
        {
            IsStrictSequenceEnforced = true
        };

        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x1234UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        // Sequence 0xFFFFFFFE
        var pkt1 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 0xFFFFFFFEU,
            SessionId: 0x1234UL,
            TimestampUs: 100);
        fsm.IngestPacketHeader(in pkt1, 100).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedSequenceNumber.Should().Be(0xFFFFFFFEU);

        // Sequence 0xFFFFFFFF
        var pkt2 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 0xFFFFFFFFU,
            SessionId: 0x1234UL,
            TimestampUs: 200);
        fsm.IngestPacketHeader(in pkt2, 200).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedSequenceNumber.Should().Be(0xFFFFFFFFU);

        // Sequence 0x00000000 (Modular 32-bit Rollover)
        var pkt3 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 0x00000000U,
            SessionId: 0x1234UL,
            TimestampUs: 300);
        fsm.IngestPacketHeader(in pkt3, 300).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedSequenceNumber.Should().Be(0x00000000U);

        // Sequence 0x00000001
        var pkt4 = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 0x00000001U,
            SessionId: 0x1234UL,
            TimestampUs: 400);
        fsm.IngestPacketHeader(in pkt4, 400).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedSequenceNumber.Should().Be(0x00000001U);

        // Stale sequence 0xFFFFFFFF (should be rejected as older than 0x00000001)
        var stalePkt = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 0xFFFFFFFFU,
            SessionId: 0x1234UL,
            TimestampUs: 500);
        fsm.IngestPacketHeader(in stalePkt, 500).Should().Be(MoonshineErrorCode.DuplicateSequence);
    }

    [Fact]
    public void StateMachine_FrameIndexRollover_AcceptsWrappedFrameIndexAndRejectsStale()
    {
        var fsm = new MoonshineProtocolStateMachine();
        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0x5678UL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        var fbMax = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 0xFFFFFFFFFFFFFFFFUL,
            PacketsReceived = 1000
        };
        fsm.IngestFeedbackLossStats(in fbMax, 1000).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedFrameIndex.Should().Be(0xFFFFFFFFFFFFFFFFUL);

        // Wrapped index 0 (Modular 64-bit Rollover)
        var fbZero = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 0x0000000000000000UL,
            PacketsReceived = 1010
        };
        fsm.IngestFeedbackLossStats(in fbZero, 1010).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedFrameIndex.Should().Be(0x0000000000000000UL);

        // Advance to 1
        var fbOne = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 0x0000000000000001UL,
            PacketsReceived = 1020
        };
        fsm.IngestFeedbackLossStats(in fbOne, 1020).Should().Be(MoonshineErrorCode.Success);
        fsm.LastReceivedFrameIndex.Should().Be(0x0000000000000001UL);

        // Stale frame index 0xFFFFFFFFFFFFFFFF
        var staleFb = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 0xFFFFFFFFFFFFFFFFUL,
            PacketsReceived = 1030
        };
        fsm.IngestFeedbackLossStats(in staleFb, 1030).Should().Be(MoonshineErrorCode.StaleTimestamp);
        fsm.LastReceivedFrameIndex.Should().Be(0x0000000000000001UL);
    }

    [Fact]
    public void StateMachine_ExplicitSessionIdPolicy_RejectsZeroSessionOnActiveMediaTraffic()
    {
        var fsm = new MoonshineProtocolStateMachine();
        fsm.RecordHelloSent();
        fsm.RecordHelloResponseReceived(0xABCDUL);
        fsm.RecordSessionSetupSent();
        fsm.RecordSessionSetupResponseReceived(1, 2, 3, 1188);

        // Media packet with SessionId == 0 on an active session MUST be rejected
        var zeroSessionHeader = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.VideoPacket,
            PayloadSize: 100,
            SequenceNumber: 1,
            SessionId: 0,
            TimestampUs: 100);

        MoonshineErrorCode err = fsm.IngestPacketHeader(in zeroSessionHeader, 100);
        err.Should().Be(MoonshineErrorCode.InvalidSession);
        fsm.State.Should().Be(MoonshineProtocolState.Faulted);
        fsm.FaultReason.Should().Contain("Session ID mismatch");
    }

    [Fact]
    public void ProtocolContracts_MessageRequirementsPolicy_VerifiesContracts()
    {
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.Hello).Should().BeFalse();
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.HelloResponse).Should().BeFalse();
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.DiscoveryProbe).Should().BeFalse();
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.VideoPacket).Should().BeTrue();
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.AudioPacket).Should().BeTrue();
        MoonshineProtocolCodec.RequiresSessionId(MoonshineMessageType.SessionSetup).Should().BeTrue();

        MoonshineProtocolCodec.RequiresAuthentication(MoonshineMessageType.GetHostConfiguration).Should().BeTrue();
        MoonshineProtocolCodec.RequiresAuthentication(MoonshineMessageType.SetHostConfiguration).Should().BeTrue();
        MoonshineProtocolCodec.RequiresAuthentication(MoonshineMessageType.VideoPacket).Should().BeFalse();
        MoonshineProtocolCodec.RequiresAuthentication(MoonshineMessageType.KeepAlive).Should().BeFalse();

        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.Hello).Should().Be(32);
        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.HelloResponse).Should().Be(48);
        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.SessionSetup).Should().Be(40);
        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.VideoPacket).Should().Be(32);
        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.AudioPacket).Should().Be(24);
        MoonshineProtocolCodec.GetMinimumPayloadSize(MoonshineMessageType.MicPacket).Should().Be(20);
    }
}
