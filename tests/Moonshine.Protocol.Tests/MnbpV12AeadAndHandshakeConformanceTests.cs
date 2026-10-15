using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentAssertions;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Security;
using Xunit;

namespace Moonshine.Protocol.Tests;

public sealed class MnbpV12AeadAndHandshakeConformanceTests
{
    [Fact]
    public void DeriveSessionKeys_ProducesDeterministicDirectionalKeysAndSalts()
    {
        byte[] psk = RandomNumberGenerator.GetBytes(32);
        byte[] ecdh = RandomNumberGenerator.GetBytes(32);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(16);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(16);

        using var keys1 = MnbpV12CryptoEngine.DeriveSessionKeys(psk, ecdh, clientNonce, serverNonce);
        using var keys2 = MnbpV12CryptoEngine.DeriveSessionKeys(psk, ecdh, clientNonce, serverNonce);

        keys1.SessionId.Should().NotBe(0UL);
        keys1.SessionId.Should().Be(keys2.SessionId);
        keys1.ClientWriteKey.ToArray().Should().Equal(keys2.ClientWriteKey.ToArray());
        keys1.ServerWriteKey.ToArray().Should().Equal(keys2.ServerWriteKey.ToArray());
        keys1.ControlKey.ToArray().Should().Equal(keys2.ControlKey.ToArray());
        keys1.ClientSalt.ToArray().Should().Equal(keys2.ClientSalt.ToArray());
        keys1.ServerSalt.ToArray().Should().Equal(keys2.ServerSalt.ToArray());

        // Directional keys and salts must be cryptographically distinct
        keys1.ClientWriteKey.ToArray().Should().NotEqual(keys1.ServerWriteKey.ToArray());
        keys1.ClientWriteKey.ToArray().Should().NotEqual(keys1.ControlKey.ToArray());
        keys1.ClientSalt.ToArray().Should().NotEqual(keys1.ServerSalt.ToArray());
    }

    [Fact]
    public void ChaCha20Poly1305_SealAndOpen_RoundTripsAndAuthenticatesHeaderAad()
    {
        byte[] psk = RandomNumberGenerator.GetBytes(32);
        byte[] ecdh = RandomNumberGenerator.GetBytes(32);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(16);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(16);

        using var keys = MnbpV12CryptoEngine.DeriveSessionKeys(psk, ecdh, clientNonce, serverNonce);
        var replayWindow = new MnbpReplayWindow();

        byte[] plaintext = "Real-time HEVC IDR slice NAL payload"u8.ToArray();
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MnbpV12CryptoEngine.ProtocolVersion12,
            MessageType: MoonshineMessageType.VideoSliceData,
            PayloadSize: (uint)plaintext.Length,
            SequenceNumber: 42,
            SessionId: keys.SessionId,
            TimestampUs: 123456789UL);

        byte[] wirePacket = new byte[MnbpV12CryptoEngine.HeaderLength + plaintext.Length + MnbpV12CryptoEngine.AeadTagLength];
        bool sealedOk = MnbpV12CryptoEngine.TrySealPacket(
            keys.ServerWriteKey,
            keys.ServerSalt,
            pathId: 1,
            in header,
            plaintext,
            wirePacket,
            out int bytesWritten);

        sealedOk.Should().BeTrue();
        bytesWritten.Should().Be(wirePacket.Length);

        byte[] decrypted = new byte[plaintext.Length];
        bool openedOk = MnbpV12CryptoEngine.TryOpenPacket(
            keys.ServerWriteKey,
            keys.ServerSalt,
            pathId: 1,
            wirePacket,
            replayWindow,
            decrypted,
            out var parsedHeader,
            out int plaintextLen);

        openedOk.Should().BeTrue();
        plaintextLen.Should().Be(plaintext.Length);
        decrypted.Should().Equal(plaintext);
        parsedHeader.SequenceNumber.Should().Be(42UL);
        replayWindow.HighestAcceptedSequence.Should().Be(42UL);

        // Replay of the exact same packet must be rejected
        bool replayOk = MnbpV12CryptoEngine.TryOpenPacket(
            keys.ServerWriteKey,
            keys.ServerSalt,
            pathId: 1,
            wirePacket,
            replayWindow,
            decrypted,
            out _,
            out _);
        replayOk.Should().BeFalse();
    }

    [Fact]
    public void ChaCha20Poly1305_HeaderTampering_FailsAuthenticationAndDoesNotAdvanceReplayWindow()
    {
        byte[] psk = RandomNumberGenerator.GetBytes(32);
        byte[] ecdh = RandomNumberGenerator.GetBytes(32);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(16);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(16);

        using var keys = MnbpV12CryptoEngine.DeriveSessionKeys(psk, ecdh, clientNonce, serverNonce);
        var replayWindow = new MnbpReplayWindow();

        byte[] plaintext = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MnbpV12CryptoEngine.ProtocolVersion12,
            MessageType: MoonshineMessageType.InputMouseState,
            PayloadSize: (uint)plaintext.Length,
            SequenceNumber: 100,
            SessionId: keys.SessionId,
            TimestampUs: 99999UL);

        byte[] wirePacket = new byte[MnbpV12CryptoEngine.HeaderLength + plaintext.Length + MnbpV12CryptoEngine.AeadTagLength];
        MnbpV12CryptoEngine.TrySealPacket(
            keys.ClientWriteKey,
            keys.ClientSalt,
            pathId: 0,
            in header,
            plaintext,
            wirePacket,
            out _).Should().BeTrue();

        // Tamper with the TimestampUs field in the unencrypted 32-byte header (AAD)
        byte[] tamperedPacket = (byte[])wirePacket.Clone();
        tamperedPacket[31] ^= 0xFF;

        byte[] output = new byte[plaintext.Length];
        bool tamperedResult = MnbpV12CryptoEngine.TryOpenPacket(
            keys.ClientWriteKey,
            keys.ClientSalt,
            pathId: 0,
            tamperedPacket,
            replayWindow,
            output,
            out _,
            out _);

        tamperedResult.Should().BeFalse();
        replayWindow.HighestAcceptedSequence.Should().Be(0UL, "failed AEAD verification must never advance the replay window");

        // Authentic packet with sequence 100 must still succeed afterwards
        bool authenticResult = MnbpV12CryptoEngine.TryOpenPacket(
            keys.ClientWriteKey,
            keys.ClientSalt,
            pathId: 0,
            wirePacket,
            replayWindow,
            output,
            out _,
            out _);

        authenticResult.Should().BeTrue();
        replayWindow.HighestAcceptedSequence.Should().Be(100UL);
    }

    [Fact]
    public void ReplayWindow_64PacketSlidingWindow_HandlesOutOfOrderAndRejectsStaleAndDuplicatePackets()
    {
        var window = new MnbpReplayWindow();

        window.CommitAccepted(100).Should().BeTrue();
        window.HighestAcceptedSequence.Should().Be(100);

        // Duplicate of 100 is rejected
        window.CanAccept(100).Should().BeFalse();

        // Lower boundary of 64-packet window: 100 - 63 = 37 is valid; 36 is outside window
        window.CanAccept(37).Should().BeTrue();
        window.CanAccept(36).Should().BeFalse();

        window.CommitAccepted(37).Should().BeTrue();
        window.CanAccept(37).Should().BeFalse();

        // Advance window to 200 (shift > 64)
        window.CommitAccepted(200).Should().BeTrue();
        window.HighestAcceptedSequence.Should().Be(200);
        window.CanAccept(100).Should().BeFalse();
        window.CanAccept(137).Should().BeTrue();
    }

    [Fact]
    public void AntiAmplificationLimiter_Enforces3xBudgetBeforeAddressValidation()
    {
        var limiter = new MnbpAntiAmplificationLimiter();
        limiter.RecordBytesReceived(120); // e.g., 120-byte HelloRequest

        // Allowed up to 360 bytes before address ownership is verified
        limiter.TryConsumeSendBudget(112).Should().BeTrue();
        limiter.TryConsumeSendBudget(248).Should().BeTrue();
        limiter.TryConsumeSendBudget(1).Should().BeFalse("exceeds 3x pre-authentication amplification budget");

        limiter.MarkAddressValidated();
        limiter.TryConsumeSendBudget(65536).Should().BeTrue("unlimited after cryptographic address validation");
    }

    [Fact]
    public void TcpFrameCodec_FramesAndRejectsOversizeControlMessages()
    {
        byte[] payload = RandomNumberGenerator.GetBytes(128);
        byte[] framed = new byte[4 + payload.Length];

        MnbpTcpFrameCodec.TryWriteFrame(payload, framed, out int written).Should().BeTrue();
        written.Should().Be(132);

        MnbpTcpFrameCodec.TryReadFrame(framed, out var decodedPayload, out int consumed, out bool oversize).Should().BeTrue();
        oversize.Should().BeFalse();
        consumed.Should().Be(132);
        decodedPayload.ToArray().Should().Equal(payload);

        // Craft an invalid 70,000-byte length prefix (> 65,536 max)
        Span<byte> badPrefix = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(badPrefix[..4], 70000U);
        MnbpTcpFrameCodec.TryReadFrame(badPrefix, out _, out _, out bool isOversize).Should().BeFalse();
        isOversize.Should().BeTrue();
    }

    [Fact]
    public void HandshakeHmacProofs_MutuallyAuthenticateTranscriptAndDetectTampering()
    {
        byte[] psk = RandomNumberGenerator.GetBytes(32);
        byte[] ecdh = RandomNumberGenerator.GetBytes(32);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(16);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(16);

        using var keys = MnbpV12CryptoEngine.DeriveSessionKeys(psk, ecdh, clientNonce, serverNonce);

        byte[] helloReqBytes = RandomNumberGenerator.GetBytes(88);
        byte[] helloRespPrefix48 = RandomNumberGenerator.GetBytes(48);
        byte[] serverHmac = new byte[32];
        MnbpV12CryptoEngine.ComputeServerHelloHmac(keys.HandshakeSecret, helloReqBytes, helloRespPrefix48, serverHmac);

        byte[] fullHelloResp = new byte[80];
        helloRespPrefix48.CopyTo(fullHelloResp.AsSpan(0, 48));
        serverHmac.CopyTo(fullHelloResp.AsSpan(48, 32));

        byte[] clientAuthPrefix52 = RandomNumberGenerator.GetBytes(52);
        byte[] clientHmac = new byte[32];
        MnbpV12CryptoEngine.ComputeClientAuthHmac(keys.HandshakeSecret, helloReqBytes, fullHelloResp, clientAuthPrefix52, clientHmac);

        byte[] fullClientAuth = new byte[84];
        clientAuthPrefix52.CopyTo(fullClientAuth.AsSpan(0, 52));
        clientHmac.CopyTo(fullClientAuth.AsSpan(52, 32));

        byte[] serverConfirmPrefix48 = RandomNumberGenerator.GetBytes(48);
        byte[] serverConfirmHmac = new byte[32];
        MnbpV12CryptoEngine.ComputeServerConfirmHmac(
            keys.HandshakeSecret,
            helloReqBytes,
            fullHelloResp,
            fullClientAuth,
            serverConfirmPrefix48,
            serverConfirmHmac);

        byte[] expectedServerConfirmHmac = new byte[32];
        MnbpV12CryptoEngine.ComputeServerConfirmHmac(
            keys.HandshakeSecret,
            helloReqBytes,
            fullHelloResp,
            fullClientAuth,
            serverConfirmPrefix48,
            expectedServerConfirmHmac);

        CryptographicOperations.FixedTimeEquals(serverConfirmHmac, expectedServerConfirmHmac).Should().BeTrue();
    }
}
