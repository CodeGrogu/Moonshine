using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Core.Media;

/// <summary>
/// Delegate for zero-allocation streaming hot path audio packet emission.
/// </summary>
public delegate void AudioPacketSink(ReadOnlySpan<byte> datagram);

/// <summary>
/// Zero-allocation Moonshine audio frame packetiser.
/// Wraps compressed Opus or linear PCM audio frames into Moonshine media datagrams.
/// </summary>
public sealed class MoonshineAudioPacketiser
{
    public const int TotalHeaderOverhead = MoonshineProtocolConstants.HeaderSize + MoonshineAudioPacketCodec.HeaderSize; // 32 + 24 = 56 bytes

    private readonly uint _streamId;
    private readonly ulong _sessionId;
    private readonly uint _sampleRate;
    private readonly byte _channels;
    private readonly MoonshineAudioCodec _codec;
    private uint _sequenceNumber;

    public uint StreamId => _streamId;
    public ulong SessionId => _sessionId;
    public uint SampleRate => _sampleRate;
    public byte Channels => _channels;
    public MoonshineAudioCodec Codec => _codec;
    public uint CurrentSequenceNumber => _sequenceNumber;

    public MoonshineAudioPacketiser(
        uint streamId,
        ulong sessionId,
        uint sampleRate = 48000,
        byte channels = 2,
        MoonshineAudioCodec codec = MoonshineAudioCodec.Opus)
    {
        _streamId = streamId;
        _sessionId = sessionId;
        _sampleRate = sampleRate;
        _channels = channels;
        _codec = codec;
    }

    /// <summary>
    /// Packetises an audio frame into an MTU-safe Moonshine media datagram and invokes the sink.
    /// Zero heap allocations in steady-state streaming hot path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int PacketiseAudioFrame(
        ReadOnlySpan<byte> audioPayload,
        ulong sampleIndex,
        ushort frameDurationUs,
        ulong timestampUs,
        AudioPacketSink sink)
    {
        if (audioPayload.IsEmpty || audioPayload.Length > 2048 - TotalHeaderOverhead) return 0;

        Span<byte> packetBuffer = stackalloc byte[2048];
        uint seq = _sequenceNumber++;

        // 1. Serialize Moonshine Packet Header (32 bytes)
        var header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.AudioPacket,
            PayloadSize: (uint)(MoonshineAudioPacketCodec.HeaderSize + audioPayload.Length),
            SequenceNumber: seq,
            SessionId: _sessionId,
            TimestampUs: timestampUs
        );

        MoonshineProtocolCodec.TryWriteHeader(in header, packetBuffer[..MoonshineProtocolConstants.HeaderSize]);

        // 2. Serialize Moonshine Audio Packet Header (24 bytes)
        var audioHeader = new MoonshineAudioPacketHeader
        {
            StreamId = _streamId,
            SampleIndex = sampleIndex,
            SampleRate = _sampleRate,
            FrameDurationUs = frameDurationUs,
            PayloadSize = (ushort)audioPayload.Length,
            Channels = _channels,
            Codec = _codec,
            Reserved = 0
        };

        MoonshineAudioPacketCodec.TryWriteHeader(in audioHeader, packetBuffer.Slice(MoonshineProtocolConstants.HeaderSize, MoonshineAudioPacketCodec.HeaderSize));

        // 3. Copy payload
        audioPayload.CopyTo(packetBuffer.Slice(TotalHeaderOverhead, audioPayload.Length));

        int datagramLength = TotalHeaderOverhead + audioPayload.Length;
        sink(packetBuffer[..datagramLength]);

        return 1;
    }
}
