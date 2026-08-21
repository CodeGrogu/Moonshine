using System.Security.Cryptography;
using System.Text;

namespace Moonshine.Core.Security;

public readonly record struct MoonshineSessionKeys(
    byte[] ClientToHostMediaKey,
    byte[] HostToClientMediaKey,
    byte[] ControlChannelKey,
    byte[] HeaderHmacKey);

public enum SessionValidationStatus
{
    Valid = 0,
    StaleTimestamp = 1,
    DuplicateSequence = 2,
    UnsupportedVersion = 3,
    InvalidSignature = 4,
    UntrustedPeer = 5,
    DowngradeDetected = 6
}

public readonly record struct SessionValidationResult(
    SessionValidationStatus Status,
    string Message);

/// <summary>
/// Authenticated session orchestrator deriving directional session keys via HKDF-SHA256
/// and enforcing strict fail-closed freshness, replay, and downgrade validation.
/// </summary>
public sealed class MoonshineSessionAuthenticator
{
    private const ulong DefaultFreshnessWindowUs = 5_000_000UL; // 5 seconds
    private readonly Lock _lock = new();
    private readonly HashSet<uint> _seenSequences = [];
    private uint _highestSequence;

    /// <summary>
    /// Derives discrete directional session keys from a shared secret, client/host nonces, and session ID via HKDF-SHA256.
    /// </summary>
    public static MoonshineSessionKeys DeriveSessionKeys(
        ReadOnlySpan<byte> sharedMasterSecret,
        ulong clientNonce,
        ulong hostNonce,
        ulong sessionId)
    {
        if (sharedMasterSecret.Length < 16)
        {
            throw new ArgumentException("Shared master secret must be at least 16 bytes.", nameof(sharedMasterSecret));
        }

        // Salt = ClientNonce (8 bytes) || HostNonce (8 bytes) || SessionId (8 bytes)
        Span<byte> salt = stackalloc byte[24];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(salt[..8], clientNonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(salt[8..16], hostNonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(salt[16..24], sessionId);

        byte[] infoBytes = Encoding.UTF8.GetBytes($"Moonshine-Session-v1-{sessionId}");

        // Derive 128 bytes total pseudo-random key material (4 x 32 bytes)
        byte[] derivedMaterial = HKDF.DeriveKey(
            hashAlgorithmName: HashAlgorithmName.SHA256,
            ikm: sharedMasterSecret.ToArray(),
            outputLength: 128,
            salt: salt.ToArray(),
            info: infoBytes);

        byte[] c2hMedia = new byte[32];
        byte[] h2cMedia = new byte[32];
        byte[] control = new byte[32];
        byte[] hmac = new byte[32];

        Array.Copy(derivedMaterial, 0, c2hMedia, 0, 32);
        Array.Copy(derivedMaterial, 32, h2cMedia, 0, 32);
        Array.Copy(derivedMaterial, 64, control, 0, 32);
        Array.Copy(derivedMaterial, 96, hmac, 0, 32);

        // Wipe intermediate material from buffer memory
        CryptographicOperations.ZeroMemory(derivedMaterial);

        return new MoonshineSessionKeys(c2hMedia, h2cMedia, control, hmac);
    }

    /// <summary>
    /// Validates message freshness and sequence uniqueness to reject replay and stale attacks.
    /// </summary>
    public SessionValidationResult ValidateMessage(
        ushort protocolVersion,
        uint sequenceNumber,
        ulong timestampUs,
        ulong currentEpochUs,
        ulong freshnessWindowUs = DefaultFreshnessWindowUs)
    {
        if (protocolVersion < 0x0001)
        {
            return new SessionValidationResult(
                SessionValidationStatus.DowngradeDetected,
                "Protocol version downgrade attempt rejected. Minimum supported version is 1.0 (0x0001).");
        }

        if (protocolVersion > 0x0001)
        {
            return new SessionValidationResult(
                SessionValidationStatus.UnsupportedVersion,
                $"Unsupported protocol version 0x{protocolVersion:X4}.");
        }

        // Freshness check
        if (currentEpochUs > timestampUs && (currentEpochUs - timestampUs) > freshnessWindowUs)
        {
            return new SessionValidationResult(
                SessionValidationStatus.StaleTimestamp,
                $"Message timestamp is stale: age {currentEpochUs - timestampUs}us exceeds window of {freshnessWindowUs}us.");
        }

        // Replay & Monotonic check
        lock (_lock)
        {
            if (_seenSequences.Contains(sequenceNumber))
            {
                return new SessionValidationResult(
                    SessionValidationStatus.DuplicateSequence,
                    $"Duplicate sequence number {sequenceNumber} detected. Replay attempt rejected.");
            }

            _seenSequences.Add(sequenceNumber);
            if (sequenceNumber > _highestSequence)
            {
                _highestSequence = sequenceNumber;
            }

            // Prune sliding window to bound memory
            if (_seenSequences.Count > 10000)
            {
                uint threshold = _highestSequence > 5000 ? _highestSequence - 5000 : 0;
                _seenSequences.RemoveWhere(seq => seq < threshold);
            }
        }

        return new SessionValidationResult(SessionValidationStatus.Valid, "Message successfully validated.");
    }

    /// <summary>
    /// Computes HMAC-SHA256 authentication tag over message header and payload.
    /// </summary>
    public static void ComputeMessageAuthTag(
        ReadOnlySpan<byte> hmacKey,
        ReadOnlySpan<byte> headerAndPayload,
        Span<byte> destination)
    {
        if (destination.Length < 32)
        {
            throw new ArgumentException("Destination buffer must be at least 32 bytes for HMAC-SHA256 tag.", nameof(destination));
        }

        HMACSHA256.HashData(hmacKey, headerAndPayload, destination);
    }

    /// <summary>
    /// Verifies HMAC-SHA256 authentication tag in constant time.
    /// </summary>
    public static bool VerifyMessageAuthTag(
        ReadOnlySpan<byte> hmacKey,
        ReadOnlySpan<byte> headerAndPayload,
        ReadOnlySpan<byte> expectedTag)
    {
        if (expectedTag.Length != 32) return false;

        Span<byte> computed = stackalloc byte[32];
        HMACSHA256.HashData(hmacKey, headerAndPayload, computed);
        return CryptographicOperations.FixedTimeEquals(computed, expectedTag);
    }
}
