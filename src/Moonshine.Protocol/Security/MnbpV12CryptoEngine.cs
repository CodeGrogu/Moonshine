using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Protocol.Security;

/// <summary>
/// Normative MNBP v1.2 (0x0102) packet type identifiers as defined in docs/PROTOCOL_SPEC_V1.md Section 4.1.
/// Resolves the v1.0/v1.2 opcode collision where 0x0107 was overloaded between ClientAuth and Teardown.
/// </summary>
public enum MnbpV12PacketType : ushort
{
    HelloRequest = 0x0101,
    HelloResponse = 0x0102,
    SessionSetupRequest = 0x0103,
    SessionSetupResponse = 0x0104,
    Ping = 0x0105,
    Pong = 0x0106,
    ClientAuth = 0x0107,
    ServerConfirm = 0x0108,
    MediaEndpointConfirm = 0x0109,
    Teardown = 0x01FF,

    VideoSliceData = 0x0201,
    VideoFecParity = 0x0202,
    VideoIdrRequest = 0x0203,
    VideoHeaderConfig = 0x0204,

    AudioOpusFrame = 0x0301,
    AudioFecParity = 0x0302,

    InputKeyboardState = 0x0401,
    InputMouseState = 0x0402,
    InputGamepadState = 0x0403,
    InputTouchState = 0x0404,

    ReceiverFeedbackReport = 0x0501,
    BitrateAdaptationHint = 0x0502
}

/// <summary>
/// Normative directional key and salt bundle derived via RFC 5869 HKDF-SHA256 (docs/PROTOCOL_SPEC_V1.md Section 6.3).
/// </summary>
public sealed class MnbpV12SessionKeyMaterial : IDisposable
{
    public const int KeySize = 32;
    public const int SaltSize = 12;
    public const int TagSize = 16;
    public const int NonceSize = 12;

    private readonly byte[] _handshakeSecret;
    private readonly byte[] _clientWriteKey;
    private readonly byte[] _serverWriteKey;
    private readonly byte[] _controlKey;
    private readonly byte[] _clientSalt;
    private readonly byte[] _serverSalt;
    private bool _disposed;

    public ulong SessionId { get; }

    public ReadOnlySpan<byte> HandshakeSecret => _handshakeSecret;
    public ReadOnlySpan<byte> ClientWriteKey => _clientWriteKey;
    public ReadOnlySpan<byte> ServerWriteKey => _serverWriteKey;
    public ReadOnlySpan<byte> ControlKey => _controlKey;
    public ReadOnlySpan<byte> ClientSalt => _clientSalt;
    public ReadOnlySpan<byte> ServerSalt => _serverSalt;

    public MnbpV12SessionKeyMaterial(
        byte[] handshakeSecret,
        byte[] clientWriteKey,
        byte[] serverWriteKey,
        byte[] controlKey,
        byte[] clientSalt,
        byte[] serverSalt,
        ulong sessionId)
    {
        ArgumentNullException.ThrowIfNull(handshakeSecret);
        ArgumentNullException.ThrowIfNull(clientWriteKey);
        ArgumentNullException.ThrowIfNull(serverWriteKey);
        ArgumentNullException.ThrowIfNull(controlKey);
        ArgumentNullException.ThrowIfNull(clientSalt);
        ArgumentNullException.ThrowIfNull(serverSalt);

        if (handshakeSecret.Length != KeySize ||
            clientWriteKey.Length != KeySize ||
            serverWriteKey.Length != KeySize ||
            controlKey.Length != KeySize)
        {
            throw new ArgumentException("All MNBP v1.2 symmetric keys must be exactly 32 bytes.");
        }

        if (clientSalt.Length != SaltSize || serverSalt.Length != SaltSize)
        {
            throw new ArgumentException("All MNBP v1.2 session salts must be exactly 12 bytes.");
        }

        _handshakeSecret = handshakeSecret;
        _clientWriteKey = clientWriteKey;
        _serverWriteKey = serverWriteKey;
        _controlKey = controlKey;
        _clientSalt = clientSalt;
        _serverSalt = serverSalt;
        SessionId = sessionId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_handshakeSecret);
        CryptographicOperations.ZeroMemory(_clientWriteKey);
        CryptographicOperations.ZeroMemory(_serverWriteKey);
        CryptographicOperations.ZeroMemory(_controlKey);
        CryptographicOperations.ZeroMemory(_clientSalt);
        CryptographicOperations.ZeroMemory(_serverSalt);
        _disposed = true;
    }
}

/// <summary>
/// Implements the normative MNBP v1.2 cryptographic key schedule (RFC 5869 HKDF-SHA256),
/// ChaCha20-Poly1305 AEAD packet encryption (RFC 8439), and mutual handshake authentication
/// as specified in docs/PROTOCOL_SPEC_V1.md Sections 6, 7, and 8.
/// </summary>
public static class MnbpV12CryptoEngine
{
    public const ushort ProtocolVersion12 = 0x0102;
    public const int HeaderLength = 32;
    public const int AeadTagLength = 16;
    public const int NonceLength = 12;
    public const int EphemeralKeyLength = 32;
    public const int RandomNonceLength = 16;

    private static readonly byte[] s_labelClientWrite = "MNBP-v1-C2S"u8.ToArray();
    private static readonly byte[] s_labelServerWrite = "MNBP-v1-S2C"u8.ToArray();
    private static readonly byte[] s_labelControl = "MNBP-v1-CTRL"u8.ToArray();
    private static readonly byte[] s_labelClientSalt = "MNBP-v1-CSALT"u8.ToArray();
    private static readonly byte[] s_labelServerSalt = "MNBP-v1-SSALT"u8.ToArray();
    private static readonly byte[] s_labelSessionId = "MNBP-v1-SID"u8.ToArray();
    private static readonly byte[] s_labelClientFinished = "MNBP-v1-CLIENT-FINISHED"u8.ToArray();
    private static readonly byte[] s_labelServerFinished = "MNBP-v1-SERVER-FINISHED"u8.ToArray();
    private static readonly byte[] s_labelEndpointConfirm = "MNBP-v1-ENDPOINT-CONFIRM"u8.ToArray();

    public static MnbpV12SessionKeyMaterial DeriveSessionKeys(
        ReadOnlySpan<byte> preSharedKey,
        ReadOnlySpan<byte> sharedEcdhSecret,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        if (preSharedKey.Length != 32)
        {
            throw new ArgumentException("Pre-shared key (PSK) must be exactly 32 bytes.", nameof(preSharedKey));
        }

        if (sharedEcdhSecret.Length != EphemeralKeyLength)
        {
            throw new ArgumentException("Shared ECDH secret must be 32 bytes.", nameof(sharedEcdhSecret));
        }

        if (clientNonce.Length != RandomNonceLength)
        {
            throw new ArgumentException("Client nonce must be 16 bytes.", nameof(clientNonce));
        }

        if (serverNonce.Length != RandomNonceLength)
        {
            throw new ArgumentException("Server nonce must be 16 bytes.", nameof(serverNonce));
        }

        Span<byte> ikm = stackalloc byte[EphemeralKeyLength + RandomNonceLength + RandomNonceLength];
        sharedEcdhSecret.CopyTo(ikm[..32]);
        clientNonce.CopyTo(ikm.Slice(32, 16));
        serverNonce.CopyTo(ikm.Slice(48, 16));

        byte[] handshakeSecret = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, ikm, preSharedKey, handshakeSecret);
        CryptographicOperations.ZeroMemory(ikm);

        byte[] clientWriteKey = new byte[32];
        byte[] serverWriteKey = new byte[32];
        byte[] controlKey = new byte[32];
        byte[] clientSalt = new byte[12];
        byte[] serverSalt = new byte[12];
        Span<byte> sessionIdBytes = stackalloc byte[8];

        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, clientWriteKey, s_labelClientWrite);
        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, serverWriteKey, s_labelServerWrite);
        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, controlKey, s_labelControl);
        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, clientSalt, s_labelClientSalt);
        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, serverSalt, s_labelServerSalt);
        HKDF.Expand(HashAlgorithmName.SHA256, handshakeSecret, sessionIdBytes, s_labelSessionId);

        ulong sessionId = BinaryPrimitives.ReadUInt64BigEndian(sessionIdBytes);
        if (sessionId == 0)
        {
            sessionId = 1;
        }

        return new MnbpV12SessionKeyMaterial(
            handshakeSecret,
            clientWriteKey,
            serverWriteKey,
            controlKey,
            clientSalt,
            serverSalt,
            sessionId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ComputeAeadNonce(
        ReadOnlySpan<byte> sessionSalt,
        byte pathId,
        ulong packetSequenceNumber,
        Span<byte> destinationNonce)
    {
        if (sessionSalt.Length != NonceLength || destinationNonce.Length < NonceLength)
        {
            throw new ArgumentException("Session salt and destination nonce must be 12 bytes.");
        }

        sessionSalt[..NonceLength].CopyTo(destinationNonce);
        destinationNonce[3] ^= pathId;

        Span<byte> seqBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(seqBytes, packetSequenceNumber);
        for (int i = 0; i < 8; i++)
        {
            destinationNonce[4 + i] ^= seqBytes[i];
        }
    }

    public static bool TrySealPacket(
        ReadOnlySpan<byte> writeKey,
        ReadOnlySpan<byte> sessionSalt,
        byte pathId,
        in MoonshinePacketHeader header,
        ReadOnlySpan<byte> plaintext,
        Span<byte> destinationPacket,
        out int bytesWritten)
    {
        bytesWritten = 0;
        int totalSize = HeaderLength + plaintext.Length + AeadTagLength;
        if (writeKey.Length != 32 || sessionSalt.Length != NonceLength || destinationPacket.Length < totalSize)
        {
            return false;
        }

        var updatedHeader = header with { PayloadSize = (uint)plaintext.Length };
        if (!MoonshineProtocolCodec.TryWriteHeader(in updatedHeader, destinationPacket[..HeaderLength]))
        {
            return false;
        }

        ReadOnlySpan<byte> aad = destinationPacket[..HeaderLength];
        Span<byte> ciphertext = destinationPacket.Slice(HeaderLength, plaintext.Length);
        Span<byte> tag = destinationPacket.Slice(HeaderLength + plaintext.Length, AeadTagLength);

        Span<byte> nonce = stackalloc byte[NonceLength];
        ComputeAeadNonce(sessionSalt, pathId, updatedHeader.SequenceNumber, nonce);

        using var aead = new ChaCha20Poly1305(writeKey);
        aead.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        bytesWritten = totalSize;
        return true;
    }

    public static bool TryOpenPacket(
        ReadOnlySpan<byte> readKey,
        ReadOnlySpan<byte> sessionSalt,
        byte pathId,
        ReadOnlySpan<byte> rawPacket,
        MnbpReplayWindow? replayWindow,
        Span<byte> plaintextDestination,
        out MoonshinePacketHeader header,
        out int plaintextBytesWritten)
    {
        header = default;
        plaintextBytesWritten = 0;

        if (readKey.Length != 32 || sessionSalt.Length != NonceLength || rawPacket.Length < HeaderLength + AeadTagLength)
        {
            return false;
        }

        if (!MoonshineProtocolCodec.TryReadHeader(rawPacket[..HeaderLength], out header))
        {
            return false;
        }

        int expectedTotalSize = HeaderLength + (int)header.PayloadSize + AeadTagLength;
        if (rawPacket.Length < expectedTotalSize || plaintextDestination.Length < (int)header.PayloadSize)
        {
            return false;
        }

        if (replayWindow != null && !replayWindow.CanAccept(header.SequenceNumber))
        {
            return false;
        }

        ReadOnlySpan<byte> aad = rawPacket[..HeaderLength];
        ReadOnlySpan<byte> ciphertext = rawPacket.Slice(HeaderLength, (int)header.PayloadSize);
        ReadOnlySpan<byte> tag = rawPacket.Slice(HeaderLength + (int)header.PayloadSize, AeadTagLength);

        Span<byte> nonce = stackalloc byte[NonceLength];
        ComputeAeadNonce(sessionSalt, pathId, header.SequenceNumber, nonce);

        try
        {
            using var aead = new ChaCha20Poly1305(readKey);
            aead.Decrypt(nonce, ciphertext, tag, plaintextDestination[..(int)header.PayloadSize], aad);
        }
        // ALLOWED_EXCEPTION: CryptographicException is thrown by ChaCha20Poly1305.Decrypt when the Poly1305 tag is forged or corrupted.
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintextDestination[..(int)header.PayloadSize]);
            return false;
        }

        replayWindow?.CommitAccepted(header.SequenceNumber);
        plaintextBytesWritten = (int)header.PayloadSize;
        return true;
    }

    public static void ComputeServerHelloHmac(
        ReadOnlySpan<byte> handshakeSecret,
        ReadOnlySpan<byte> helloRequestBytes,
        ReadOnlySpan<byte> helloResponsePrefix48,
        Span<byte> destinationHmac32)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, handshakeSecret);
        hmac.AppendData(helloRequestBytes);
        hmac.AppendData(helloResponsePrefix48);
        hmac.GetHashAndReset(destinationHmac32);
    }

    public static void ComputeClientAuthHmac(
        ReadOnlySpan<byte> handshakeSecret,
        ReadOnlySpan<byte> helloRequestBytes,
        ReadOnlySpan<byte> helloResponseBytes,
        ReadOnlySpan<byte> clientAuthPrefix52,
        Span<byte> destinationHmac32)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, handshakeSecret);
        hmac.AppendData(helloRequestBytes);
        hmac.AppendData(helloResponseBytes);
        hmac.AppendData(s_labelClientFinished);
        hmac.AppendData(clientAuthPrefix52);
        hmac.GetHashAndReset(destinationHmac32);
    }

    public static void ComputeServerConfirmHmac(
        ReadOnlySpan<byte> handshakeSecret,
        ReadOnlySpan<byte> helloRequestBytes,
        ReadOnlySpan<byte> helloResponseBytes,
        ReadOnlySpan<byte> clientAuthBytes,
        ReadOnlySpan<byte> serverConfirmPrefix48,
        Span<byte> destinationHmac32)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, handshakeSecret);
        hmac.AppendData(helloRequestBytes);
        hmac.AppendData(helloResponseBytes);
        hmac.AppendData(clientAuthBytes);
        hmac.AppendData(s_labelServerFinished);
        hmac.AppendData(serverConfirmPrefix48);
        hmac.GetHashAndReset(destinationHmac32);
    }

    public static void ComputeEndpointConfirmHmac(
        ReadOnlySpan<byte> controlKey,
        ulong sessionId,
        byte pathId,
        ReadOnlySpan<byte> clientEndpointBytes,
        Span<byte> destinationHmac32)
    {
        Span<byte> headerBuf = stackalloc byte[9];
        BinaryPrimitives.WriteUInt64BigEndian(headerBuf[..8], sessionId);
        headerBuf[8] = pathId;

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, controlKey);
        hmac.AppendData(headerBuf);
        hmac.AppendData(clientEndpointBytes);
        hmac.AppendData(s_labelEndpointConfirm);
        hmac.GetHashAndReset(destinationHmac32);
    }
}

/// <summary>
/// Normative 64-packet sliding-window anti-replay engine per docs/PROTOCOL_SPEC_V1.md Section 9.1 and RFC 1982.
/// Window state is never mutated until AEAD authentication succeeds.
/// </summary>
public sealed class MnbpReplayWindow
{
    public const ulong WindowSize = 64;

    private ulong _highestAcceptedSeq;
    private ulong _replayBitmap;
    private bool _initialized;

    public ulong HighestAcceptedSequence => _highestAcceptedSeq;
    public ulong ReplayBitmap => _replayBitmap;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanAccept(ulong sequenceNumber)
    {
        if (!_initialized)
        {
            return true;
        }

        if (sequenceNumber > _highestAcceptedSeq)
        {
            return true;
        }

        ulong delta = _highestAcceptedSeq - sequenceNumber;
        if (delta >= WindowSize)
        {
            return false;
        }

        ulong mask = 1UL << (int)delta;
        return (_replayBitmap & mask) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CommitAccepted(ulong sequenceNumber)
    {
        if (!CanAccept(sequenceNumber))
        {
            return false;
        }

        if (!_initialized)
        {
            _initialized = true;
            _highestAcceptedSeq = sequenceNumber;
            _replayBitmap = 1UL;
            return true;
        }

        if (sequenceNumber > _highestAcceptedSeq)
        {
            ulong shift = sequenceNumber - _highestAcceptedSeq;
            if (shift >= WindowSize)
            {
                _replayBitmap = 1UL;
            }
            else
            {
                _replayBitmap = (_replayBitmap << (int)shift) | 1UL;
            }

            _highestAcceptedSeq = sequenceNumber;
            return true;
        }

        ulong delta = _highestAcceptedSeq - sequenceNumber;
        _replayBitmap |= 1UL << (int)delta;
        return true;
    }
}

/// <summary>
/// Enforces the normative 3x pre-authentication UDP anti-amplification limit (docs/PROTOCOL_SPEC_V1.md Section 5 and Section 11).
/// </summary>
public sealed class MnbpAntiAmplificationLimiter
{
    public const ulong AmplificationFactor = 3;

    private ulong _bytesReceived;
    private ulong _bytesSent;
    private bool _addressValidated;

    public bool IsAddressValidated => _addressValidated;
    public ulong BytesReceived => _bytesReceived;
    public ulong BytesSent => _bytesSent;

    public void RecordBytesReceived(uint byteCount)
    {
        _bytesReceived += byteCount;
    }

    public bool TryConsumeSendBudget(uint byteCountToSend)
    {
        if (_addressValidated)
        {
            _bytesSent += byteCountToSend;
            return true;
        }

        ulong maxAllowed = _bytesReceived * AmplificationFactor;
        if (_bytesSent + byteCountToSend > maxAllowed)
        {
            return false;
        }

        _bytesSent += byteCountToSend;
        return true;
    }

    public void MarkAddressValidated()
    {
        _addressValidated = true;
    }
}

/// <summary>
/// Implements the normative 4-byte Big-Endian length-prefixed TCP control framing (docs/PROTOCOL_SPEC_V1.md Section 5).
/// </summary>
public static class MnbpTcpFrameCodec
{
    public const uint MaxControlFrameSize = 65536;
    public const int LengthPrefixSize = 4;

    public static bool TryWriteFrame(ReadOnlySpan<byte> mnbpPacket, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (mnbpPacket.Length == 0 || (uint)mnbpPacket.Length > MaxControlFrameSize)
        {
            return false;
        }

        int totalLength = LengthPrefixSize + mnbpPacket.Length;
        if (destination.Length < totalLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], (uint)mnbpPacket.Length);
        mnbpPacket.CopyTo(destination.Slice(4, mnbpPacket.Length));
        bytesWritten = totalLength;
        return true;
    }

    public static bool TryReadFrame(
        ReadOnlySpan<byte> sourceBuffer,
        out ReadOnlySpan<byte> framePayload,
        out int totalBytesConsumed,
        out bool isOversizeError)
    {
        framePayload = default;
        totalBytesConsumed = 0;
        isOversizeError = false;

        if (sourceBuffer.Length < LengthPrefixSize)
        {
            return false;
        }

        uint frameLength = BinaryPrimitives.ReadUInt32BigEndian(sourceBuffer[..4]);
        if (frameLength == 0 || frameLength > MaxControlFrameSize)
        {
            isOversizeError = true;
            return false;
        }

        int required = LengthPrefixSize + (int)frameLength;
        if (sourceBuffer.Length < required)
        {
            return false;
        }

        framePayload = sourceBuffer.Slice(LengthPrefixSize, (int)frameLength);
        totalBytesConsumed = required;
        return true;
    }
}
