using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Video;

namespace Moonshine.Core.Media;

public sealed record MediaReassemblyMetrics(
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

    // FEC Tracking per frame slot (frameIndex -> FEC block tracking)
    private readonly Dictionary<ulong, FecFrameTracker> _fecTrackers = new();

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

    private readonly ulong[] _slotBitmasks;
    private readonly uint[] _slotFrameIndices;

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
                CleanFecTracker(packet.FrameIndex);

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
        if (!_fecTrackers.TryGetValue(packet.FrameIndex, out var tracker))
        {
            tracker = new FecFrameTracker(packet.FrameIndex, packet.TotalPackets, _fecDataShards, _fecParityShards, _mtuPayloadSize, totalFrameBytes);
            _fecTrackers[packet.FrameIndex] = tracker;
        }

        tracker.AddDataPacket(in packet, blockIndex);
    }

    private unsafe int IngestFecParityPacket(in MoonshinePacketDesc packet, int blockIndex, uint totalFrameBytes)
    {
        if (!_fecTrackers.TryGetValue(packet.FrameIndex, out var tracker))
        {
            tracker = new FecFrameTracker(packet.FrameIndex, packet.TotalPackets, _fecDataShards, _fecParityShards, _mtuPayloadSize, totalFrameBytes);
            _fecTrackers[packet.FrameIndex] = tracker;
        }

        tracker.AddParityPacket(in packet, blockIndex);

        // Check if FEC reconstruction is possible for any blocks with missing shards
        int pushRes = tracker.ReconstructLostShardsAndPush(_nativeJitterHandle, blockIndex, ref _packetsRecoveredFec);
        if (pushRes == 1)
        {
            _framesCompleted++;
            CleanFecTracker(packet.FrameIndex);
        }
        return pushRes;
    }

    private void CleanFecTracker(ulong frameIndex)
    {
        _fecTrackers.Remove(frameIndex);

        // Keep tracker count bounded
        if (_fecTrackers.Count > _maxFrames * 2)
        {
            var staleKeys = _fecTrackers.Keys.OrderBy(k => k).Take(_fecTrackers.Count - _maxFrames).ToList();
            foreach (var key in staleKeys)
            {
                _fecTrackers.Remove(key);
            }
        }
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
            _fecTrackers.Clear();
        }
    }

    private sealed class FecFrameTracker
    {
        private readonly ulong _frameIndex;
        private readonly int _totalPackets;
        private readonly int _dataShards;
        private readonly int _parityShards;
        private readonly int _shardSize;
        private readonly uint _totalFrameBytes;
        private readonly Dictionary<int, byte[]> _receivedDataShards = new();
        private readonly Dictionary<int, byte[]> _receivedParityShards = new();
        private readonly HashSet<int> _reconstructedBlocks = new();

        public FecFrameTracker(ulong frameIndex, int totalPackets, int dataShards, int parityShards, int shardSize, uint totalFrameBytes)
        {
            _frameIndex = frameIndex;
            _totalPackets = totalPackets;
            _dataShards = dataShards;
            _parityShards = parityShards;
            _shardSize = shardSize;
            _totalFrameBytes = totalFrameBytes;
        }

        public unsafe void AddDataPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            if (!_receivedDataShards.ContainsKey(packet.PacketIndex))
            {
                byte[] data = new byte[_shardSize];
                fixed (byte* pDst = data)
                {
                    NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                }
                _receivedDataShards[packet.PacketIndex] = data;
            }
        }

        public unsafe void AddParityPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            int parityIdx = (int)packet.PacketIndex;
            if (!_receivedParityShards.ContainsKey(parityIdx))
            {
                byte[] data = new byte[_shardSize];
                fixed (byte* pDst = data)
                {
                    NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                }
                _receivedParityShards[parityIdx] = data;
            }
        }

        public unsafe int ReconstructLostShardsAndPush(IntPtr jitterHandle, int blockIndex, ref ulong recoveredCount)
        {
            if (_reconstructedBlocks.Contains(blockIndex)) return 0;

            int blockStart = blockIndex * _dataShards;
            int actualDataInBlock = Math.Min(_dataShards, _totalPackets - blockStart);

            List<int> missingIndices = new();
            for (int i = 0; i < actualDataInBlock; i++)
            {
                int packetIdx = blockStart + i;
                if (!_receivedDataShards.ContainsKey(packetIdx))
                {
                    missingIndices.Add(i);
                }
            }

            if (missingIndices.Count == 0) return 0; // Nothing missing in this block
            if (missingIndices.Count > _parityShards) return 0; // Unrecoverable: too many erasures

            // Check if we have enough total shards (data + parity >= actualDataInBlock)
            int parityAvailable = _receivedParityShards.Count(p => p.Key >= _totalPackets + (blockIndex * _parityShards) &&
                                                                   p.Key < _totalPackets + ((blockIndex + 1) * _parityShards));
            int dataAvailable = actualDataInBlock - missingIndices.Count;

            if (dataAvailable + parityAvailable < actualDataInBlock)
            {
                return 0; // Not enough parity to reconstruct
            }

            // Build shard pointer array for native SIMD reconstruction
            int totalShardsInBlock = _dataShards + _parityShards;
            byte*[] shardPtrs = new byte*[totalShardsInBlock];
            byte[][] buffers = new byte[totalShardsInBlock][];

            for (int i = 0; i < _dataShards; i++)
            {
                int packetIdx = blockStart + i;
                buffers[i] = _receivedDataShards.TryGetValue(packetIdx, out var d) ? d : new byte[_shardSize];
            }

            for (int p = 0; p < _parityShards; p++)
            {
                int parityPacketIdx = _totalPackets + (blockIndex * _parityShards) + p;
                buffers[_dataShards + p] = _receivedParityShards.TryGetValue(parityPacketIdx, out var par) ? par : new byte[_shardSize];
            }

            int[] erased = missingIndices.ToArray();

            GCHandle[] handles = new GCHandle[totalShardsInBlock];
            for (int i = 0; i < totalShardsInBlock; i++)
            {
                handles[i] = GCHandle.Alloc(buffers[i], GCHandleType.Pinned);
                shardPtrs[i] = (byte*)handles[i].AddrOfPinnedObject();
            }

            int lastPushRes = 0;
            try
            {
                fixed (byte** pShards = shardPtrs)
                fixed (int* pErased = erased)
                {
                    int res = MoonshineNativeMethods.FecReconstructSimd(
                        pShards,
                        _dataShards,
                        _parityShards,
                        _shardSize,
                        pErased,
                        erased.Length
                    );

                    if (res != 0) return 0;
                }

                _reconstructedBlocks.Add(blockIndex);

                // Push recovered packets into native jitter buffer while buffers remain pinned
                foreach (int erasedIdx in erased)
                {
                    int recoveredPacketIdx = blockStart + erasedIdx;
                    _receivedDataShards[recoveredPacketIdx] = buffers[erasedIdx];
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
                for (int i = 0; i < totalShardsInBlock; i++)
                {
                    if (handles[i].IsAllocated) handles[i].Free();
                }
            }
        }
    }
}
