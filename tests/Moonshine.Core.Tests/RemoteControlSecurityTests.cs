using System.Security.Cryptography;
using FluentAssertions;
using Moonshine.Core.Security;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

public class RemoteControlSecurityTests
{
    [Fact]
    public void RemoteControl_ReplayAndFreshness_EnforcedBySessionAuthenticator()
    {
        var authenticator = new MoonshineSessionAuthenticator();
        ulong currentEpochUs = 20_000_000UL;
        ulong freshnessWindowUs = 5_000_000UL;

        // 1. Fresh message with sequence 1 accepted
        SessionValidationResult result1 = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 1,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        result1.Status.Should().Be(SessionValidationStatus.Valid);
        result1.Message.Should().Contain("Message successfully validated");

        // 2. Fresh message within 5-second freshness window (age = 4s) accepted
        SessionValidationResult result2 = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 2,
            timestampUs: currentEpochUs - 4_000_000UL,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        result2.Status.Should().Be(SessionValidationStatus.Valid);

        // 3. Stale timestamp (age = 5.000001s, just outside the 5-second window) rejected
        SessionValidationResult staleEdge = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 3,
            timestampUs: currentEpochUs - 5_000_001UL,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        staleEdge.Status.Should().Be(SessionValidationStatus.StaleTimestamp);
        staleEdge.Message.Should().Contain("timestamp is stale");

        // 4. Stale timestamp (age = 15s, significantly outside the 5-second window) rejected
        SessionValidationResult staleDistant = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 4,
            timestampUs: currentEpochUs - 15_000_000UL,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        staleDistant.Status.Should().Be(SessionValidationStatus.StaleTimestamp);
        staleDistant.Message.Should().Contain("timestamp is stale");

        // 5. Replay attack: retransmitting sequence number 1 rejected
        SessionValidationResult replayResult1 = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 1,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        replayResult1.Status.Should().Be(SessionValidationStatus.DuplicateSequence);
        replayResult1.Message.Should().Contain("Duplicate sequence number 1 detected");

        // 6. Replay attack: retransmitting sequence number 2 rejected
        SessionValidationResult replayResult2 = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 2,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        replayResult2.Status.Should().Be(SessionValidationStatus.DuplicateSequence);
        replayResult2.Message.Should().Contain("Duplicate sequence number 2 detected");

        // 7. Legitimate sequence progression continues to succeed
        for (uint seq = 10; seq <= 25; seq++)
        {
            SessionValidationResult validSeq = authenticator.ValidateMessage(
                protocolVersion: MoonshineProtocolConstants.Version10,
                sequenceNumber: seq,
                timestampUs: currentEpochUs,
                currentEpochUs: currentEpochUs,
                freshnessWindowUs: freshnessWindowUs);

            validSeq.Status.Should().Be(SessionValidationStatus.Valid);
        }

        // 8. Replaying any of the newly seen sequences rejected
        SessionValidationResult replayMid = authenticator.ValidateMessage(
            protocolVersion: MoonshineProtocolConstants.Version10,
            sequenceNumber: 15,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: freshnessWindowUs);

        replayMid.Status.Should().Be(SessionValidationStatus.DuplicateSequence);
    }

    [Fact]
    public void RemoteControl_HMACAuthentication_DetectsTamperedPayload()
    {
        byte[] masterSecret = new byte[32];
        RandomNumberGenerator.Fill(masterSecret);

        ulong clientNonce = 0xA1B2C3D4E5F60718UL;
        ulong hostNonce = 0x8172635445362718UL;
        ulong sessionId = 0xCAFEBABE12345678UL;

        MoonshineSessionKeys keys = MoonshineSessionAuthenticator.DeriveSessionKeys(
            masterSecret,
            clientNonce,
            hostNonce,
            sessionId);

        keys.HeaderHmacKey.Should().HaveCount(32);
        keys.ControlChannelKey.Should().HaveCount(32);

        var configPayload = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 5,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 144,
            TargetBitrateKbps = 35000,
            MaxBitrateKbps = 50000,
            PreferredCodec = MoonshineVideoCodec.Av1,
            Hdr10Enabled = 1,
            AudioChannels = 6,
            AudioQualityMode = 1,
            AudioBitrateKbps = 320,
            InputPollingRateHz = 1000,
            MicPassthroughEnabled = 1,
            VirtualAudioDriverEnabled = 1,
            Reserved1 = 0,
            Reserved2 = 0,
            Reserved3 = 0
        };

        byte[] serializedDatagram = new byte[MoonshineProtocolConstants.HeaderSize + 48];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.SetHostConfiguration,
            PayloadSize: 48,
            SequenceNumber: 42,
            SessionId: sessionId,
            TimestampUs: 1234567890UL);

        MoonshineProtocolCodec.TryWriteHeader(in header, serializedDatagram);
        MoonshineProtocolCodec.TryWriteHostConfiguration(in configPayload, serializedDatagram.AsSpan(MoonshineProtocolConstants.HeaderSize));

        // Compute valid HMAC-SHA256 tag over the header and payload
        byte[] authenticTag = new byte[32];
        MoonshineSessionAuthenticator.ComputeMessageAuthTag(keys.HeaderHmacKey, serializedDatagram, authenticTag);

        // Verification of untampered datagram must succeed
        bool isOriginalValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(keys.HeaderHmacKey, serializedDatagram, authenticTag);
        isOriginalValid.Should().BeTrue();

        // Tamper with each byte across the serialised MoonshineHostConfigurationPayload
        int payloadStart = MoonshineProtocolConstants.HeaderSize;
        int payloadEnd = payloadStart + 48;

        for (int byteOffset = payloadStart; byteOffset < payloadEnd; byteOffset++)
        {
            byte[] tamperedDatagram = (byte[])serializedDatagram.Clone();
            tamperedDatagram[byteOffset] ^= 0x01;

            bool isTamperedValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(keys.HeaderHmacKey, tamperedDatagram, authenticTag);
            isTamperedValid.Should().BeFalse($"tampering byte at payload offset {byteOffset - payloadStart} must fail HMAC verification");
        }

        // Tamper with header fields (sequence number, message type, session ID)
        for (int headerOffset = 0; headerOffset < payloadStart; headerOffset++)
        {
            byte[] tamperedHeaderDatagram = (byte[])serializedDatagram.Clone();
            tamperedHeaderDatagram[headerOffset] ^= 0x80;

            bool isHeaderTamperValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(keys.HeaderHmacKey, tamperedHeaderDatagram, authenticTag);
            isHeaderTamperValid.Should().BeFalse($"tampering header byte at offset {headerOffset} must fail HMAC verification");
        }

        // Tampering with the HMAC authentication tag itself must also fail
        for (int tagOffset = 0; tagOffset < 32; tagOffset++)
        {
            byte[] tamperedTag = (byte[])authenticTag.Clone();
            tamperedTag[tagOffset] ^= 0xFF;

            bool isTagTamperValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(keys.HeaderHmacKey, serializedDatagram, tamperedTag);
            isTagTamperValid.Should().BeFalse($"tampering auth tag byte at offset {tagOffset} must fail verification");
        }

        // Verification with wrong derived session key must fail
        bool isWrongKeyValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(keys.ControlChannelKey, serializedDatagram, authenticTag);
        isWrongKeyValid.Should().BeFalse();
    }
}
