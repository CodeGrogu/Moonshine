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
    private readonly int _maxPacketsPerFrame;
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
    public int MaxPacketsPerFrame => _maxPacketsPerFrame;
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
        int mtuPayloadSize = MoonshineVideoPacketiser.DefaultMtuPayloadSize,
        int maxPacketsPerFrame = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 4, nameof(maxFrames));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrames, 1024, nameof(maxFrames));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPacketsPerFrame, 16, nameof(maxPacketsPerFrame));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPacketsPerFrame, ushort.MaxValue, nameof(maxPacketsPerFrame));
        ArgumentOutOfRangeException.ThrowIfLessThan(mtuPayloadSize, 64, nameof(mtuPayloadSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mtuPayloadSize, 65535, nameof(mtuPayloadSize));

        _maxFrames = maxFrames;
        _fecDataShards = fecDataShards;
        _fecParityShards = fecParityShards;
        _mtuPayloadSize = mtuPayloadSize;
        _maxPacketsPerFrame = maxPacketsPerFrame;

        _slotBitmasks = new ulong[maxFrames * 8]; // 512 packet bits per slot
        _slotFrameIndices = new uint[maxFrames];
        _fecSlotTrackers = new FecSlotTracker[maxFrames];
        for (int i = 0; i < maxFrames; i++)
        {
            _fecSlotTrackers[i] = new FecSlotTracker(fecDataShards, fecParityShards, mtuPayloadSize, maxPacketsPerFrame);
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

        if (videoHeader.PayloadSize == 0 ||
            videoHeader.PayloadSize > _mtuPayloadSize ||
            datagram.Length < MoonshineVideoPacketiser.TotalHeaderOverhead + videoHeader.PayloadSize)
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

            // Packet-index and total-packet validation against protocol invariants
            if (packet.TotalPackets == 0 || packet.TotalPackets > _maxPacketsPerFrame)
            {
                _packetsLost++;
                return -1;
            }

            if (!isParity)
            {
                if (packet.PacketIndex >= packet.TotalPackets)
                {
                    _packetsLost++;
                    return -1;
                }

                if (_fecDataShards > 0 && fecBlockIndex != (packet.PacketIndex / _fecDataShards))
                {
                    _packetsLost++;
                    return -1;
                }
            }
            else
            {
                if (packet.PacketIndex < packet.TotalPackets)
                {
                    _packetsLost++;
                    return -1;
                }

                if (_fecParityShards > 0 && fecBlockIndex != ((packet.PacketIndex - packet.TotalPackets) / _fecParityShards))
                {
                    _packetsLost++;
                    return -1;
                }
            }

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

            // Global stale check: packets belonging to an already-completed frame are dropped
            if (_lastCompletedFrameIndex > 0 && packet.FrameIndex <= _lastCompletedFrameIndex)
            {
                _stalePacketsDropped++;
                return 0;
            }

            // Slot-specific staleness and recycling check
            int slotIdx = (int)(packet.FrameIndex % (uint)_maxFrames);
            uint currentSlotFrame = _slotFrameIndices[slotIdx];

            if (currentSlotFrame != 0)
            {
                if (packet.FrameIndex < currentSlotFrame)
                {
                    // Late packet belonging to an older, superseded frame that previously used this slot
                    _stalePacketsDropped++;
                    return 0;
                }
                else if (packet.FrameIndex > currentSlotFrame)
                {
                    // Newer frame evicts and reuses this slot
                    _slotFrameIndices[slotIdx] = packet.FrameIndex;
                    Array.Clear(_slotBitmasks, slotIdx * 8, 8);
                    _fecSlotTrackers[slotIdx].Reset(packet.FrameIndex, packet.TotalPackets, totalFrameBytes);
                }
            }
            else
            {
                // Initial frame assignment to this slot
                _slotFrameIndices[slotIdx] = packet.FrameIndex;
                Array.Clear(_slotBitmasks, slotIdx * 8, 8);
                _fecSlotTrackers[slotIdx].Reset(packet.FrameIndex, packet.TotalPackets, totalFrameBytes);
            }

            // Zero-allocation duplicate check using preallocated slot bitmask
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
                // Incomplete (tracked within native pre-allocated slot)
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
            _lastCompletedFrameIndex = packet.FrameIndex;
            tracker.Clear();
        }
        return pushRes;
    }

    /// <summary>
    /// Attempts to pop the next complete reassembled frame from the jitter buffer.
    /// </summary>
    public bool TryPopFrame(out MoonshineFrameDesc frame)
    {
        lock (_lock)
        {
            if (_disposed || _nativeJitterHandle == IntPtr.Zero)
            {
                frame = default;
                return false;
            }

            int result = MoonshineNativeMethods.JitterPopFrame(_nativeJitterHandle, out frame);
            return result == 1;
        }
    }

    /// <summary>
    /// Sets simulated loss count for unit testing and diagnostic telemetry validation.
    /// </summary>
    public void SetSimulatedLossCount(ulong count)
    {
        lock (_lock)
        {
            _packetsLost = count;
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
            for (int i = 0; i < _fecSlotTrackers.Length; i++)
            {
                _fecSlotTrackers[i]?.Dispose();
            }
        }
    }

    private sealed unsafe class FecSlotTracker : IDisposable
    {
        private readonly int _dataShards;
        private readonly int _parityShards;
        private readonly int _shardSize;
        private readonly int _totalShardsInBlock;
        private readonly int _maxBlocks;
        private readonly int _maxData;
        private readonly int _maxParity;

        private readonly byte* _pDataBuffers;
        private readonly byte* _pParityBuffers;
        private readonly bool[] _hasData;
        private readonly bool[] _hasParity;
        private readonly bool[] _reconstructedBlocks;

        private ulong _frameIndex;
        private int _totalPackets;
        private uint _totalFrameBytes;
        private bool _disposed;

        public ulong FrameIndex => _frameIndex;

        public FecSlotTracker(int dataShards, int parityShards, int shardSize, int maxPacketsPerFrame = 512)
        {
            _dataShards = dataShards;
            _parityShards = parityShards;
            _shardSize = shardSize;
            _totalShardsInBlock = dataShards + parityShards;

            if (dataShards > 0 && parityShards > 0)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(dataShards, 128, nameof(dataShards));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(parityShards, 128, nameof(parityShards));

                _maxBlocks = Math.Max(1, (maxPacketsPerFrame + dataShards - 1) / dataShards);
                _maxData = checked(dataShards * _maxBlocks);
                _maxParity = checked(parityShards * _maxBlocks);

                nuint dataBytes = checked((nuint)_maxData * (nuint)shardSize);
                nuint parityBytes = checked((nuint)_maxParity * (nuint)shardSize);

                _pDataBuffers = (byte*)NativeMemory.AllocZeroed(dataBytes);
                _pParityBuffers = (byte*)NativeMemory.AllocZeroed(parityBytes);

                _hasData = new bool[_maxData];
                _hasParity = new bool[_maxParity];
                _reconstructedBlocks = new bool[_maxBlocks];
            }
            else
            {
                _maxBlocks = 0;
                _maxData = 0;
                _maxParity = 0;
                _pDataBuffers = null;
                _pParityBuffers = null;
                _hasData = Array.Empty<bool>();
                _hasParity = Array.Empty<bool>();
                _reconstructedBlocks = Array.Empty<bool>();
            }
        }

        public void Reset(ulong frameIndex, int totalPackets, uint totalFrameBytes)
        {
            _frameIndex = frameIndex;
            _totalPackets = totalPackets;
            _totalFrameBytes = totalFrameBytes;
            if (_hasData.Length > 0) Array.Clear(_hasData);
            if (_hasParity.Length > 0) Array.Clear(_hasParity);
            if (_reconstructedBlocks.Length > 0) Array.Clear(_reconstructedBlocks);
        }

        public void Clear()
        {
            _frameIndex = 0;
            _totalPackets = 0;
            _totalFrameBytes = 0;
            if (_hasData.Length > 0) Array.Clear(_hasData);
            if (_hasParity.Length > 0) Array.Clear(_hasParity);
            if (_reconstructedBlocks.Length > 0) Array.Clear(_reconstructedBlocks);
        }

        public void AddDataPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            if (_pDataBuffers == null) return;
            int packetIdx = packet.PacketIndex;
            if (packetIdx >= 0 && packetIdx < _maxData)
            {
                byte* pDst = _pDataBuffers + (packetIdx * _shardSize);
                NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                _hasData[packetIdx] = true;
            }
        }

        public void AddParityPacket(in MoonshinePacketDesc packet, int blockIndex)
        {
            if (_pParityBuffers == null) return;
            int parityIdx = (int)packet.PacketIndex - _totalPackets;
            if (parityIdx >= 0 && parityIdx < _maxParity)
            {
                byte* pDst = _pParityBuffers + (parityIdx * _shardSize);
                NativeMemory.Copy(packet.PayloadPtr, pDst, (nuint)Math.Min((int)packet.PayloadSize, _shardSize));
                _hasParity[parityIdx] = true;
            }
        }

        public int ReconstructLostShardsAndPush(IntPtr jitterHandle, int blockIndex, ref ulong recoveredCount)
        {
            if (_pDataBuffers == null || _pParityBuffers == null) return 0;
            if (blockIndex < 0 || blockIndex >= _reconstructedBlocks.Length || _reconstructedBlocks[blockIndex])
            {
                return 0;
            }

            int blockStart = blockIndex * _dataShards;
            int actualDataInBlock = Math.Min(_dataShards, _totalPackets - blockStart);
            if (actualDataInBlock <= 0) return 0;

            int missingCount = 0;
            Span<int> missingIndices = stackalloc int[_totalShardsInBlock];

            for (int i = 0; i < actualDataInBlock; i++)
            {
                int packetIdx = blockStart + i;
                if (!_hasData[packetIdx])
                {
                    missingIndices[missingCount++] = i;
                }
            }

            int missingDataCount = missingCount;

            if (missingDataCount == 0) return 0;
            if (missingDataCount > _parityShards) return 0;

            int parityStart = blockIndex * _parityShards;
            int parityAvailable = 0;
            for (int p = 0; p < _parityShards; p++)
            {
                if (parityStart + p < _maxParity && _hasParity[parityStart + p])
                {
                    parityAvailable++;
                }
                else
                {
                    missingIndices[missingCount++] = _dataShards + p;
                }
            }

            int dataAvailable = actualDataInBlock - missingDataCount;
            if (dataAvailable + parityAvailable < actualDataInBlock)
            {
                return 0;
            }

            int totalShards = _totalShardsInBlock;
            byte** pShards = stackalloc byte*[totalShards];

            for (int i = 0; i < _dataShards; i++)
            {
                int packetIdx = blockStart + i;
                pShards[i] = _pDataBuffers + (packetIdx * _shardSize);
            }

            for (int p = 0; p < _parityShards; p++)
            {
                int parIdx = parityStart + p;
                pShards[_dataShards + p] = _pParityBuffers + (parIdx * _shardSize);
            }

            int lastPushRes = 0;
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

            for (int m = 0; m < missingDataCount; m++)
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
                    PayloadPtr = _pDataBuffers + (recoveredPacketIdx * _shardSize),
                    BufferSlotIndex = -1,
                    StreamPacketIndex = 0
                };

                lastPushRes = MoonshineNativeMethods.JitterPushPacket(jitterHandle, in recDesc);
            }

            return lastPushRes;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_pDataBuffers != null)
            {
                NativeMemory.Free(_pDataBuffers);
            }
            if (_pParityBuffers != null)
            {
                NativeMemory.Free(_pParityBuffers);
            }
            GC.SuppressFinalize(this);
        }

        ~FecSlotTracker()
        {
            Dispose();
        }
    }
}
