using System.Security.Cryptography;
using FluentAssertions;
using Moonshine.Core.Security;
using Xunit;

namespace Moonshine.Core.Tests;

public class MoonshineSessionAuthenticatorTests
{
    [Fact]
    public void DeriveSessionKeys_ProducesDistinctCryptographicKeys()
    {
        byte[] masterSecret = new byte[32];
        RandomNumberGenerator.Fill(masterSecret);

        ulong clientNonce = 0x1122334455667788UL;
        ulong hostNonce = 0x99AABBCCDDEEFF00UL;
        ulong sessionId = 42;

        MoonshineSessionKeys keys1 = MoonshineSessionAuthenticator.DeriveSessionKeys(
            masterSecret,
            clientNonce,
            hostNonce,
            sessionId);

        keys1.ClientToHostMediaKey.Should().HaveCount(32);
        keys1.HostToClientMediaKey.Should().HaveCount(32);
        keys1.ControlChannelKey.Should().HaveCount(32);
        keys1.HeaderHmacKey.Should().HaveCount(32);

        // All 4 keys must be mutually distinct
        keys1.ClientToHostMediaKey.Should().NotEqual(keys1.HostToClientMediaKey);
        keys1.ClientToHostMediaKey.Should().NotEqual(keys1.ControlChannelKey);
        keys1.ClientToHostMediaKey.Should().NotEqual(keys1.HeaderHmacKey);
        keys1.HostToClientMediaKey.Should().NotEqual(keys1.ControlChannelKey);

        // Different session ID produces completely different keys
        MoonshineSessionKeys keys2 = MoonshineSessionAuthenticator.DeriveSessionKeys(
            masterSecret,
            clientNonce,
            hostNonce,
            sessionId: 43);

        keys2.ClientToHostMediaKey.Should().NotEqual(keys1.ClientToHostMediaKey);
        keys2.HostToClientMediaKey.Should().NotEqual(keys1.HostToClientMediaKey);
    }

    [Fact]
    public void ValidateMessage_RejectsStaleTimestamp()
    {
        var authenticator = new MoonshineSessionAuthenticator();
        ulong currentEpochUs = 10_000_000UL;
        ulong staleTimestampUs = 1_000_000UL; // 9 seconds old (window is 5s)

        SessionValidationResult result = authenticator.ValidateMessage(
            protocolVersion: 0x0001,
            sequenceNumber: 1,
            timestampUs: staleTimestampUs,
            currentEpochUs: currentEpochUs,
            freshnessWindowUs: 5_000_000UL);

        result.Status.Should().Be(SessionValidationStatus.StaleTimestamp);
    }

    [Fact]
    public void ValidateMessage_RejectsDuplicateSequenceNumber()
    {
        var authenticator = new MoonshineSessionAuthenticator();
        ulong currentEpochUs = 10_000_000UL;

        // First message with sequence 100
        SessionValidationResult firstResult = authenticator.ValidateMessage(
            protocolVersion: 0x0001,
            sequenceNumber: 100,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs);

        firstResult.Status.Should().Be(SessionValidationStatus.Valid);

        // Replay of sequence 100
        SessionValidationResult replayResult = authenticator.ValidateMessage(
            protocolVersion: 0x0001,
            sequenceNumber: 100,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs);

        replayResult.Status.Should().Be(SessionValidationStatus.DuplicateSequence);
    }

    [Fact]
    public void ValidateMessage_RejectsDowngradeAttempt()
    {
        var authenticator = new MoonshineSessionAuthenticator();
        ulong currentEpochUs = 10_000_000UL;

        SessionValidationResult downgradeResult = authenticator.ValidateMessage(
            protocolVersion: 0x0000, // Version 0 (downgrade)
            sequenceNumber: 1,
            timestampUs: currentEpochUs,
            currentEpochUs: currentEpochUs);

        downgradeResult.Status.Should().Be(SessionValidationStatus.DowngradeDetected);
    }

    [Fact]
    public void MessageAuthTag_ComputesAndVerifiesAccurately()
    {
        byte[] hmacKey = new byte[32];
        RandomNumberGenerator.Fill(hmacKey);

        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        byte[] tag = new byte[32];

        MoonshineSessionAuthenticator.ComputeMessageAuthTag(hmacKey, payload, tag);

        bool isValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(hmacKey, payload, tag);
        isValid.Should().BeTrue();

        // Corrupted payload
        byte[] corruptedPayload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 99];
        bool isCorruptedValid = MoonshineSessionAuthenticator.VerifyMessageAuthTag(hmacKey, corruptedPayload, tag);
        isCorruptedValid.Should().BeFalse();
    }
}
