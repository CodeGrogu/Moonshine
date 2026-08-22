using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;

namespace Moonshine.Core.Media;

/// <summary>
/// Delegate for zero-allocation streaming hot path video packet emission.
/// </summary>
public delegate void VideoPacketSink(ReadOnlySpan<byte> datagram);

/// <summary>
/// Zero-allocation Moonshine video frame packetiser and MTU fragmenter.
/// Fragments high-bitrate encoded frames into MTU-safe Moonshine media datagrams with optional Galois Field GF(2^8) Reed-Solomon FEC parity generation.
/// </summary>
public sealed class MoonshineVideoPacketiser
{
    public const int DefaultMtuPayloadSize = 1188; // MTU-safe slice payload size (fits 1500 MTU with IP, UDP, MSHN, and Video headers)
    public const int TotalHeaderOverhead = MoonshineProtocolConstants.HeaderSize + MoonshineVideoPacketCodec.HeaderSize; // 32 + 32 = 64 bytes

    private readonly uint _streamId;
    private readonly ulong _sessionId;
    private readonly int _mtuPayloadSize;
    private readonly int _fecDataShards;
    private readonly int _fecParityShards;
    private readonly byte[]? _fecParityArena;
    private readonly byte[]? _fecZeroShard;
    private uint _sequenceNumber;

    public uint StreamId => _streamId;
    public ulong SessionId => _sessionId;
    public int MtuPayloadSize => _mtuPayloadSize;
    public int FecDataShards => _fecDataShards;
    public int FecParityShards => _fecParityShards;
    public uint CurrentSequenceNumber => _sequenceNumber;

    public MoonshineVideoPacketiser(
        uint streamId,
        ulong sessionId,
        int mtuPayloadSize = DefaultMtuPayloadSize,
        int fecDataShards = 0,
        int fecParityShards = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mtuPayloadSize, 64, nameof(mtuPayloadSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mtuPayloadSize, 2048 - TotalHeaderOverhead, nameof(mtuPayloadSize));

        if (fecDataShards > 0 && fecParityShards > 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(fecDataShards + fecParityShards, 255, nameof(fecDataShards));
            _fecParityArena = new byte[fecParityShards * mtuPayloadSize];
            _fecZeroShard = new byte[mtuPayloadSize];
        }

        _streamId = streamId;
        _sessionId = sessionId;
        _mtuPayloadSize = mtuPayloadSize;
        _fecDataShards = fecDataShards;
        _fecParityShards = fecParityShards;
    }

    /// <summary>
    /// Computes the exact number of data packets required to carry a frame of the given size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CalculatePacketCount(int frameBytes)
    {
        if (frameBytes <= 0) return 0;
        return (frameBytes + _mtuPayloadSize - 1) / _mtuPayloadSize;
    }

    /// <summary>
    /// Fragments an encoded frame into MTU-safe Moonshine media datagrams and feeds them directly to the provided sink.
    /// Zero heap allocations in steady-state streaming hot path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int PacketiseFrame(
        ReadOnlySpan<byte> frameData,
        ulong frameIndex,
        ulong timestampUs,
        bool isKeyframe,
        bool isHdr10,
        VideoPacketSink sink)
    {
        if (frameData.IsEmpty) return 0;

        int totalBytes = frameData.Length;
        int totalPackets = CalculatePacketCount(totalBytes);
        int packetsEmitted = 0;

        Span<byte> packetBuffer = stackalloc byte[2048]; // Maximum MTU datagram stack buffer

        fixed (byte* pFrame = frameData)
        {
            for (int packetIdx = 0; packetIdx < totalPackets; packetIdx++)
            {
                int offset = packetIdx * _mtuPayloadSize;
                int sliceSize = Math.Min(_mtuPayloadSize, totalBytes - offset);

                MoonshineVideoAttributes flags = MoonshineVideoAttributes.None;
                if (isKeyframe) flags |= MoonshineVideoAttributes.Keyframe;
                if (isHdr10) flags |= MoonshineVideoAttributes.Hdr10Present;
                if (packetIdx == 0) flags |= MoonshineVideoAttributes.FrameStart;
                if (packetIdx == totalPackets - 1) flags |= MoonshineVideoAttributes.FrameEnd;

                uint seq = _sequenceNumber++;

                // 1. Serialize Moonshine Packet Header (32 bytes)
                var header = new MoonshinePacketHeader(
                    Magic: MoonshineProtocolConstants.Magic,
                    Version: MoonshineProtocolConstants.Version10,
                    MessageType: MoonshineMessageType.VideoPacket,
                    PayloadSize: (uint)(MoonshineVideoPacketCodec.HeaderSize + sliceSize),
                    SequenceNumber: seq,
                    SessionId: _sessionId,
                    TimestampUs: timestampUs
                );

                MoonshineProtocolCodec.TryWriteHeader(in header, packetBuffer[..MoonshineProtocolConstants.HeaderSize]);

                // 2. Serialize Moonshine Video Packet Header (32 bytes)
                var videoHeader = new MoonshineVideoPacketHeader
                {
                    StreamId = _streamId,
                    FrameIndex = frameIndex,
                    PacketIndex = (uint)packetIdx,
                    TotalPackets = (uint)totalPackets,
                    FecBlockIndex = (uint)(packetIdx / Math.Max(1, _fecDataShards > 0 ? _fecDataShards : totalPackets)),
                    PayloadSize = (ushort)sliceSize,
                    PacketType = 0, // Data
                    Flags = flags,
                    TotalFrameBytes = (uint)totalBytes
                };

                MoonshineVideoPacketCodec.TryWriteHeader(in videoHeader, packetBuffer.Slice(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize));

                // 3. Copy slice payload
                new ReadOnlySpan<byte>(pFrame + offset, sliceSize)
                    .CopyTo(packetBuffer.Slice(TotalHeaderOverhead, sliceSize));

                int datagramLength = TotalHeaderOverhead + sliceSize;
                sink(packetBuffer[..datagramLength]);
                packetsEmitted++;
            }

            // 4. Generate FEC Parity packets if enabled
            if (_fecDataShards > 0 && _fecParityShards > 0 && totalPackets > 1)
            {
                packetsEmitted += GenerateFecParityPackets(pFrame, totalBytes, totalPackets, frameIndex, timestampUs, isKeyframe, isHdr10, sink);
            }
        }

        return packetsEmitted;
    }

    private unsafe int GenerateFecParityPackets(
        byte* pFrame,
        int totalBytes,
        int totalPackets,
        ulong frameIndex,
        ulong timestampUs,
        bool isKeyframe,
        bool isHdr10,
        VideoPacketSink sink)
    {
        int parityPacketsEmitted = 0;
        int shardSize = _mtuPayloadSize;
        int numBlocks = (totalPackets + _fecDataShards - 1) / _fecDataShards;

        byte** dataShardsPtrs = stackalloc byte*[_fecDataShards];
        byte** parityShardsPtrs = stackalloc byte*[_fecParityShards];

        byte[] parityArena = _fecParityArena ?? new byte[_fecParityShards * shardSize];
        byte[] zeroShard = _fecZeroShard ?? new byte[shardSize];

        // Allocate stack buffer outside loops to prevent stack overflow (CA2014)
        Span<byte> packetBuffer = stackalloc byte[2048];

        fixed (byte* pParity = parityArena)
        fixed (byte* pZero = zeroShard)
        {
            for (int p = 0; p < _fecParityShards; p++)
            {
                parityShardsPtrs[p] = pParity + (p * shardSize);
            }

            for (int blockIdx = 0; blockIdx < numBlocks; blockIdx++)
            {
                int blockStartPacket = blockIdx * _fecDataShards;
                int actualDataInBlock = Math.Min(_fecDataShards, totalPackets - blockStartPacket);

                for (int i = 0; i < _fecDataShards; i++)
                {
                    int packetIdx = blockStartPacket + i;
                    if (packetIdx < totalPackets)
                    {
                        int offset = packetIdx * _mtuPayloadSize;
                        int sliceSize = Math.Min(_mtuPayloadSize, totalBytes - offset);

                        if (sliceSize == shardSize)
                        {
                            dataShardsPtrs[i] = pFrame + offset;
                        }
                        else
                        {
                            // Trailing packet is smaller than shardSize: zero-pad
                            new Span<byte>(pZero, shardSize).Clear();
                            new ReadOnlySpan<byte>(pFrame + offset, sliceSize).CopyTo(new Span<byte>(pZero, sliceSize));
                            dataShardsPtrs[i] = pZero;
                        }
                    }
                    else
                    {
                        dataShardsPtrs[i] = pZero;
                    }
                }

                // Clear parity arena before SIMD encoding
                new Span<byte>(pParity, _fecParityShards * shardSize).Clear();

                int fecRes = MoonshineNativeMethods.FecEncodeSimd(
                    dataShardsPtrs,
                    _fecDataShards,
                    parityShardsPtrs,
                    _fecParityShards,
                    shardSize
                );

                if (fecRes != 0) continue;

                // Emit Parity Packets
                for (int p = 0; p < _fecParityShards; p++)
                {
                    uint seq = _sequenceNumber++;
                    MoonshineVideoAttributes flags = MoonshineVideoAttributes.None;
                    if (isKeyframe) flags |= MoonshineVideoAttributes.Keyframe;
                    if (isHdr10) flags |= MoonshineVideoAttributes.Hdr10Present;

                    var header = new MoonshinePacketHeader(
                        Magic: MoonshineProtocolConstants.Magic,
                        Version: MoonshineProtocolConstants.Version10,
                        MessageType: MoonshineMessageType.VideoPacket,
                        PayloadSize: (uint)(MoonshineVideoPacketCodec.HeaderSize + shardSize),
                        SequenceNumber: seq,
                        SessionId: _sessionId,
                        TimestampUs: timestampUs
                    );

                    MoonshineProtocolCodec.TryWriteHeader(in header, packetBuffer[..MoonshineProtocolConstants.HeaderSize]);

                    var videoHeader = new MoonshineVideoPacketHeader
                    {
                        StreamId = _streamId,
                        FrameIndex = frameIndex,
                        PacketIndex = (uint)(totalPackets + (blockIdx * _fecParityShards) + p),
                        TotalPackets = (uint)totalPackets,
                        FecBlockIndex = (uint)blockIdx,
                        PayloadSize = (ushort)shardSize,
                        PacketType = 1, // Parity
                        Flags = flags,
                        TotalFrameBytes = (uint)totalBytes
                    };

                    MoonshineVideoPacketCodec.TryWriteHeader(in videoHeader, packetBuffer.Slice(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize));

                    new ReadOnlySpan<byte>(parityShardsPtrs[p], shardSize)
                        .CopyTo(packetBuffer.Slice(TotalHeaderOverhead, shardSize));

                    int datagramLength = TotalHeaderOverhead + shardSize;
                    sink(packetBuffer[..datagramLength]);
                    parityPacketsEmitted++;
                }
            }
        }

        return parityPacketsEmitted;
    }
}
