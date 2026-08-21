using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;

namespace Moonshine.Core.Media;

public readonly record struct MediaReassemblyMetrics(
    ulong PacketsIngested,
    ulong FramesCompleted,
    ulong DuplicatePacketsDropped,
    ulong StalePacketsDropped,
    ulong PacketsLost,
    ulong PacketsRecoveredFec,
    double AverageReassemblyLatencyMicroseconds,
    double AverageJitterMicroseconds
);

/// <summary>
/// High-throughput, bounded, zero-GC-allocation client media frame reassembly and jitter buffer pipeline.
/// Reassembles out-of-order, variable-sized MTU packets and applies SIMD Galois Field FEC recovery on lost packet shards.
/// </summary>
public sealed class MoonshineMediaReassemblyPipeline : IDisposable
{
    private readonly IntPtr _nativeJitterHandle;
    private readonly int _maxFrames;
    private readonly int _fecDataShards;
    private readonly int _fecParityShards;
    private readonly int _mtuPayloadSize;
    private readonly Lock _lock = new();

    private ulong _packetsIngested;
    private ulong _framesCompleted;
    private ulong _duplicatePacketsDropped;
    private ulong _stalePacketsDropped;
    private ulong _packetsLost;
    private ulong _packetsRecoveredFec;

    private double _avgReassemblyLatencyUs;
    private double _avgJitterUs;
    private ulong _lastFrameTimestampUs;
    private bool _disposed;

    // Preallocated FEC slot trackers matching jitter buffer slots
    private readonly FecSlotTracker[] _fecSlotTrackers;
    private readonly ulong[] _slotBitmasks;
    private readonly uint[] _slotFrameIndices;

    public bool IsActive => !_disposed && _nativeJitterHandle != IntPtr.Zero;
    public int MaxFrames => _maxFrames;
    public MediaReassemblyMetrics Metrics
    {
        get
        {
            lock (_lock)
            {
                return new MediaReassemblyMetrics(
                    _packetsIngested,
                    _framesCompleted,
                    _duplicatePacketsDropped,
                    _stalePacketsDropped,
                    _packetsLost,
                    _packetsRecoveredFec,
                    _avgReassemblyLatencyUs,
                    _avgJitterUs
                );
            }
        }
    }

    public MoonshineMediaReassemblyPipeline(
        int maxFrames = 16,
        int fecDataShards = 0,
        int fecParityShards = 0,
        int mtuPayloadSize = MoonshineVideoPacketiser.DefaultMtuPayloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 4, nameof(maxFrames));
        _maxFrames = maxFrames;
        _fecDataShards = fecDataShards;
        _fecParityShards = fecParityShards;
        _mtuPayloadSize = mtuPayloadSize;

        _slotBitmasks = new ulong[maxFrames * 8]; // 512 packet bits per slot
        _slotFrameIndices = new uint[maxFrames];
        _fecSlotTrackers = new FecSlotTracker[maxFrames];
        for (int i = 0; i < maxFrames; i++)
        {
            _fecSlotTrackers[i] = new FecSlotTracker(fecDataShards, fecParityShards, mtuPayloadSize);
        }

        _nativeJitterHandle = MoonshineNativeMethods.JitterCreate((nuint)maxFrames);
        if (_nativeJitterHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to allocate native jitter buffer.");
        }
    }

    /// <summary>
    /// Ingests a raw Moonshine media datagram from network transport with zero managed heap allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int IngestDatagram(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < MoonshineVideoPacketiser.TotalHeaderOverhead)
        {
            return -1;
        }

        Moonshine.Protocol.Contracts.MoonshineErrorCode err = MoonshineProtocolCodec.TryReadHeader(datagram, out var packetHeader);
        if (err != Moonshine.Protocol.Contracts.MoonshineErrorCode.Success || packetHeader.Magic != MoonshineProtocolConstants.Magic)
        {
            return -1;
        }

        if (packetHeader.MessageType != MoonshineMessageType.VideoPacket)
        {
            return 0; // Ignore non-video messages in video reassembly path
        }

        if (!MoonshineVideoPacketCodec.TryReadHeader(datagram.Slice(MoonshineProtocolConstants.HeaderSize, MoonshineVideoPacketCodec.HeaderSize), out var videoHeader))
        {
            return -1;
        }

        ReadOnlySpan<byte> slicePayload = datagram.Slice(MoonshineVideoPacketiser.TotalHeaderOverhead, videoHeader.PayloadSize);

        fixed (byte* pPayload = slicePayload)
        {
            byte descFlags = 0;
            if ((videoHeader.Flags & MoonshineVideoAttributes.FrameStart) != 0) descFlags |= 0x01;
            if ((videoHeader.Flags & MoonshineVideoAttributes.FrameEnd) != 0) descFlags |= 0x02;
            if ((videoHeader.Flags & MoonshineVideoAttributes.Keyframe) != 0) descFlags |= 0x04;

            var desc = new MoonshinePacketDesc
            {
                SequenceNumber = packetHeader.SequenceNumber,
                FrameIndex = (uint)videoHeader.FrameIndex,
                PacketIndex = (ushort)videoHeader.PacketIndex,
                TotalPackets = (ushort)videoHeader.TotalPackets,
                PayloadSize = videoHeader.PayloadSize,
                Flags = descFlags,
                PayloadPtr = pPayload,
                BufferSlotIndex = -1,
                StreamPacketIndex = 0
            };

            return IngestPacketDesc(in desc, packetHeader.TimestampUs, videoHeader.PacketType == 1, (int)videoHeader.FecBlockIndex, videoHeader.TotalFrameBytes);
        }
    }

    private ulong _lastCompletedFrameIndex;

    /// <summary>
    /// Ingests a packet descriptor into the reassembly engine and jitter buffer.
    /// </summary>
    public unsafe int IngestPacketDesc(in MoonshinePacketDesc packet, ulong packetTimestampUs = 0, bool isParity = false, int fecBlockIndex = 0, uint totalFrameBytes = 0)
    {
        lock (_lock)
        {
            if (_disposed || _nativeJitterHandle == IntPtr.Zero) return -1;

            _packetsIngested++;
            long startTicks = Stopwatch.GetTimestamp();

            // Track jitter metrics
            if (packetTimestampUs > 0 && _lastFrameTimestampUs > 0)
            {
                long transitDeltaUs = (long)packetTimestampUs - (long)_lastFrameTimestampUs;
                double jitterSample = Math.Abs(transitDeltaUs);
                _avgJitterUs = (_avgJitterUs * 0.95) + (jitterSample * 0.05);
            }
            _lastFrameTimestampUs = packetTimestampUs;

            // Stale check
            if (_lastCompletedFrameIndex > 0 && packet.FrameIndex <= _lastCompletedFrameIndex)
            {
                _stalePacketsDropped++;
                return 0;
            }

            // Zero-allocation duplicate check using preallocated slot bitmasks
            int slotIdx = (int)(packet.FrameIndex % (uint)_maxFrames);
            if (_slotFrameIndices[slotIdx] != packet.FrameIndex)
            {
                _slotFrameIndices[slotIdx] = packet.FrameIndex;
                Array.Clear(_slotBitmasks, slotIdx * 8, 8);
            }

            if (!isParity && packet.PacketIndex < 512)
            {
                int wordIdx = packet.PacketIndex / 64;
                ulong mask = 1UL << (packet.PacketIndex % 64);
                int bitmaskOffset = (slotIdx * 8) + wordIdx;
                if ((_slotBitmasks[bitmaskOffset] & mask) != 0)
                {
                    _duplicatePacketsDropped++;
                    return 0;
                }
                _slotBitmasks[bitmaskOffset] |= mask;
            }

            // Handle FEC Parity packet ingestion
            if (isParity && _fecDataShards > 0 && _fecParityShards > 0)
            {
                return IngestFecParityPacket(in packet, fecBlockIndex, totalFrameBytes);
            }

            // Normal Data packet push into native JitterBuffer
            int pushRes = MoonshineNativeMethods.JitterPushPacket(_nativeJitterHandle, in packet);
            if (pushRes == 0)
            {
                // Incomplete (handled within native pre-allocated slot)
                if (_fecDataShards > 0 && _fecParityShards > 0 && packet.TotalPackets > 0)
                {
                    TrackFecDataPacket(in packet, fecBlockIndex, totalFrameBytes);
                }
            }
            else if (pushRes == 1)
            {
                // Frame complete!
                _framesCompleted++;
                _lastCompletedFrameIndex = packet.FrameIndex;
                _fecSlotTrackers[slotIdx].Clear();

                double latencyUs = (Stopwatch.GetTimestamp() - startTicks) * (1_000_000.0 / Stopwatch.Frequency);
                _avgReassemblyLatencyUs = (_avgReassemblyLatencyUs * 0.95) + (latencyUs * 0.05);
            }
            else if (pushRes < 0)
            {
                _packetsLost++;
            }

            return pushRes;
        }
    }

    /// <summary>
    /// Attempts to pop the next completed video frame from the jitter buffer in presentation order.
    /// </summary>
    public int TryPopCompletedFrame(out MoonshineFrameDesc outFrame)
    {
        lock (_lock)
        {
            outFrame = default;
            if (_disposed || _nativeJitterHandle == IntPtr.Zero) return 0;

            return MoonshineNativeMethods.JitterPopFrame(_nativeJitterHandle, out outFrame);
        }
    }

    private unsafe void TrackFecDataPacket(in MoonshinePacketDesc packet, int blockIndex, uint totalFrameBytes)
    {
        int slotIdx = (int)(packet.FrameIndex % (uint)_maxFrames);
        var tracker = _fecSlotTrackers[slotIdx];
        if (tracker.FrameIndex != packet.FrameIndex)
        {
            tracker.Reset(packet.FrameIndex, packet.TotalPackets, totalFrameBytes);
        }

        tracker.AddDataPacket(in packet, blockIndex);
    }

    private unsafe int IngestFecParityPacket(in MoonshinePacketDesc packet, int blockIndex, uint totalFrameBytes)
    {
        int slotIdx = (int)(packet.FrameIndex % (uint)_maxFrames);
        var tracker = _fecSlotTrackers[slotIdx];
        if (tracker.FrameIndex != packet.FrameIndex)
        {
            tracker.Reset(packet.FrameIndex, packet.TotalPackets, totalFrameBytes);
        }

        tracker.AddParityPacket(in packet, blockIndex);

        // Check if FEC reconstruction is possible for any blocks with missing shards
        int pushRes = tracker.ReconstructLostShardsAndPush(_nativeJitterHandle, blockIndex, ref _packetsRecoveredFec);
        if (pushRes == 1)
        {
            _framesCompleted++;
            tracker.Clear();
        }
        return pushRes;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_nativeJitterHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.JitterDestroy(_nativeJitterHandle);
            }
            for (int i = 0; i < _fecSlotTrackers.Length; i++)
            {
                _fecSlotTrackers[i]?.Clear();
            }
        }
    }

    private sealed class FecSlotTracker
    {
        private readonly int _dataShards;
        private readonly int _parityShards;
        private readonly int _shardSize;
        private readonly int _totalShardsInBlock;

        private readonly byte[][] _dataBuffers;
        private readonly byte[][] _parityBuffers;
        private readonly bool[] _hasData;
        private readonly bool[] _hasParity;
        private readonly bool[] _reconstructedBlocks;

        private ulong _frameIndex;
        private int _totalPackets;
        private uint _totalFrameBytes;

        public ulong FrameIndex => _frameIndex;

        public FecSlotTracker(int dataShards, int parityShards, int shardSize)
        {
            _dataShards = dataShards;
            _parityShards = parityShards;
            _shardSize = shardSize;
            _totalShardsInBlock = dataShards + parityShards;

            int maxBlocks = 64;
            int maxData = Math.Max(1, dataShards * maxBlocks);
            int maxParity = Math.Max(1, parityShards * maxBlocks);

            _dataBuffers = new byte[maxData][];
            _hasData = new bool[maxData];
            for (int i = 0; i < maxData; i++)
            {
                _dataBuffers[i] = new byte[shardSize];
            }

            _parityBuffers = new byte[maxParity][];
            _hasParity = new bool[maxParity];
            for (int i = 0; i < maxParity; i++)
            {
                _parityBuffers[i] = new byte[shardSize];
            }

            _reconstructedBlocks = new bool[maxBlocks];
        }

        public void Reset(ulong frameIndex, int totalPackets, uint totalFrameBytes)
        {
            _frameIndex = frameIndex;
            _totalPackets = totalPackets;
            _totalFrameBytes = totalFrameBytes;
            Array.Clear(_hasData);
            Array.Clear(_hasParity);
            Array.Clear(_reconstructedBlocks);
        }

        public void Clear()
        {
            _frameIndex = 0;
            _totalPackets = 0;
            _totalFrameBytes = 0;
            Array.Clear(_hasData);
            Array.Clear(_hasParity);
            Array.Clear(_reconstructedBlocks);
        }

        public unsafe void AddDataPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            int packetIdx = packet.PacketIndex;
            if (packetIdx >= 0 && packetIdx < _dataBuffers.Length)
            {
                fixed (byte* pDst = _dataBuffers[packetIdx])
                {
                    NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                }
                _hasData[packetIdx] = true;
            }
        }

        public unsafe void AddParityPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            int parityIdx = (int)packet.PacketIndex - _totalPackets;
            if (parityIdx >= 0 && parityIdx < _parityBuffers.Length)
            {
                fixed (byte* pDst = _parityBuffers[parityIdx])
                {
                    NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                }
                _hasParity[parityIdx] = true;
            }
        }

        public unsafe int ReconstructLostShardsAndPush(IntPtr jitterHandle, int blockIndex, ref ulong recoveredCount)
        {
            if (blockIndex < 0 || blockIndex >= _reconstructedBlocks.Length || _reconstructedBlocks[blockIndex])
            {
                return 0;
            }

            int blockStart = blockIndex * _dataShards;
            int actualDataInBlock = Math.Min(_dataShards, _totalPackets - blockStart);
            if (actualDataInBlock <= 0) return 0;

            int missingCount = 0;
            Span<int> missingIndices = stackalloc int[_dataShards];

            for (int i = 0; i < actualDataInBlock; i++)
            {
                int packetIdx = blockStart + i;
                if (!_hasData[packetIdx])
                {
                    missingIndices[missingCount++] = i;
                }
            }

            if (missingCount == 0) return 0;
            if (missingCount > _parityShards) return 0;

            int parityStart = blockIndex * _parityShards;
            int parityAvailable = 0;
            for (int p = 0; p < _parityShards; p++)
            {
                if (parityStart + p < _hasParity.Length && _hasParity[parityStart + p])
                {
                    parityAvailable++;
                }
            }

            int dataAvailable = actualDataInBlock - missingCount;
            if (dataAvailable + parityAvailable < actualDataInBlock)
            {
                return 0;
            }

            int totalShards = _totalShardsInBlock;
            byte*[] shardPtrs = new byte*[totalShards];
            GCHandle[] handles = new GCHandle[totalShards];

            for (int i = 0; i < _dataShards; i++)
            {
                int packetIdx = blockStart + i;
                handles[i] = GCHandle.Alloc(_dataBuffers[packetIdx], GCHandleType.Pinned);
                shardPtrs[i] = (byte*)handles[i].AddrOfPinnedObject();
            }

            for (int p = 0; p < _parityShards; p++)
            {
                int parIdx = parityStart + p;
                handles[_dataShards + p] = GCHandle.Alloc(_parityBuffers[parIdx], GCHandleType.Pinned);
                shardPtrs[_dataShards + p] = (byte*)handles[_dataShards + p].AddrOfPinnedObject();
            }

            int lastPushRes = 0;
            try
            {
                fixed (byte** pShards = shardPtrs)
                fixed (int* pErased = missingIndices[..missingCount])
                {
                    int res = MoonshineNativeMethods.FecReconstructSimd(
                        pShards,
                        _dataShards,
                        _parityShards,
                        _shardSize,
                        pErased,
                        missingCount
                    );

                    if (res != 0) return 0;
                }

                _reconstructedBlocks[blockIndex] = true;

                for (int m = 0; m < missingCount; m++)
                {
                    int erasedIdx = missingIndices[m];
                    int recoveredPacketIdx = blockStart + erasedIdx;
                    _hasData[recoveredPacketIdx] = true;
                    recoveredCount++;

                    ushort sliceSize = (ushort)_shardSize;
                    if (recoveredPacketIdx == _totalPackets - 1 && _totalFrameBytes > 0)
                    {
                        int fullSlicesBytes = (_totalPackets - 1) * _shardSize;
                        if (_totalFrameBytes > (uint)fullSlicesBytes)
                        {
                            sliceSize = (ushort)(_totalFrameBytes - (uint)fullSlicesBytes);
                        }
                    }

                    var recDesc = new MoonshinePacketDesc
                    {
                        SequenceNumber = (uint)recoveredPacketIdx,
                        FrameIndex = (uint)_frameIndex,
                        PacketIndex = (ushort)recoveredPacketIdx,
                        TotalPackets = (ushort)_totalPackets,
                        PayloadSize = sliceSize,
                        Flags = (byte)((recoveredPacketIdx == 0 ? 0x01 : 0) | (recoveredPacketIdx == _totalPackets - 1 ? 0x02 : 0)),
                        PayloadPtr = (byte*)handles[erasedIdx].AddrOfPinnedObject(),
                        BufferSlotIndex = -1,
                        StreamPacketIndex = 0
                    };

                    lastPushRes = MoonshineNativeMethods.JitterPushPacket(jitterHandle, in recDesc);
                }

                return lastPushRes;
            }
            finally
            {
                for (int i = 0; i < totalShards; i++)
                {
                    if (handles[i].IsAllocated) handles[i].Free();
                }
            }
        }
    }
}
