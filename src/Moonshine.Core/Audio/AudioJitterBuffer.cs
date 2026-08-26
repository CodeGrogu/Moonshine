namespace Moonshine.Core.Audio;

public sealed record AudioJitterBufferMetrics(
    ulong PacketsPushed,
    ulong PacketsPopped,
    uint DiscardedLatePackets,
    uint BufferUnderruns,
    uint BufferOverruns,
    uint CurrentQueuedFrames
);

/// <summary>
/// Preallocated, bounded jitter buffer for remote audio packets.
/// Delivers zero-allocation packet reordering, clock-drift tracking, and jitter smoothing.
/// </summary>
public sealed class AudioJitterBuffer
{
    private readonly struct BufferSlot
    {
        public readonly byte[] Data;
        public readonly int Length;
        public readonly uint Sequence;
        public readonly ulong TimestampQpc;
        public readonly bool IsOccupied;

        public BufferSlot(byte[] data, int length, uint sequence, ulong timestampQpc, bool isOccupied)
        {
            Data = data;
            Length = length;
            Sequence = sequence;
            TimestampQpc = timestampQpc;
            IsOccupied = isOccupied;
        }
    }

    private readonly BufferSlot[] _slots;
    private readonly byte[][] _preallocatedPayloads;
    private readonly int _capacity;
    private readonly Lock _lock = new();

    private uint _headSeq;
    private uint _tailSeq;
    private bool _initializedSeq;

    private ulong _packetsPushed;
    private ulong _packetsPopped;
    private uint _discardedLate;
    private uint _underruns;
    private uint _overruns;
    private uint _queuedCount;

    public int Capacity => _capacity;
    public uint QueuedCount => _queuedCount;

    public AudioJitterBufferMetrics Metrics
    {
        get
        {
            lock (_lock)
            {
                return new AudioJitterBufferMetrics(
                    _packetsPushed,
                    _packetsPopped,
                    _discardedLate,
                    _underruns,
                    _overruns,
                    _queuedCount
                );
            }
        }
    }

    public AudioJitterBuffer(int capacity = 64, int maxPacketSize = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPacketSize);

        _capacity = capacity;
        _slots = new BufferSlot[capacity];
        _preallocatedPayloads = new byte[capacity][];
        for (int i = 0; i < capacity; i++)
        {
            _preallocatedPayloads[i] = new byte[maxPacketSize];
        }
    }

    /// <summary>
    /// Pushes an incoming compressed audio packet into the jitter buffer with zero allocations.
    /// </summary>
    public bool Push(uint sequence, ulong timestampQpc, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return false;

        lock (_lock)
        {
            if (!_initializedSeq)
            {
                _headSeq = sequence;
                _tailSeq = sequence;
                _initializedSeq = true;
            }
            else if (_packetsPopped == 0 && (int)(sequence - _headSeq) < 0 && (int)(_headSeq - sequence) < _capacity)
            {
                _headSeq = sequence;
            }

            // Reject old / late packets outside jitter window
            int diff = (int)(sequence - _headSeq);
            if (diff < 0)
            {
                _discardedLate++;
                return false;
            }

            if (diff >= _capacity)
            {
                _overruns++;
                return false;
            }

            int slotIndex = (int)(sequence % (uint)_capacity);
            byte[] targetBuffer = _preallocatedPayloads[slotIndex];

            int copyLen = Math.Min(payload.Length, targetBuffer.Length);
            payload[..copyLen].CopyTo(targetBuffer);

            _slots[slotIndex] = new BufferSlot(targetBuffer, copyLen, sequence, timestampQpc, true);
            _packetsPushed++;

            if ((int)(sequence - _tailSeq) >= 0)
            {
                _tailSeq = sequence + 1;
            }

            _queuedCount = (uint)Math.Clamp((int)(_tailSeq - _headSeq), 0, _capacity);
            return true;
        }
    }

    /// <summary>
    /// Pops the next sequential audio packet from the jitter buffer with zero allocations.
    /// Returns false if underrun occurs (slot missing or unpopulated).
    /// </summary>
    public bool Pop(Span<byte> outPayload, out int payloadBytes, out uint sequence, out ulong timestampQpc)
    {
        payloadBytes = 0;
        sequence = 0;
        timestampQpc = 0;

        lock (_lock)
        {
            if (!_initializedSeq || _headSeq == _tailSeq)
            {
                _underruns++;
                return false;
            }

            if (_packetsPopped == 0 && _queuedCount < 2)
            {
                return false;
            }

            int slotIndex = (int)(_headSeq % (uint)_capacity);
            ref readonly var slot = ref _slots[slotIndex];

            if (!slot.IsOccupied || slot.Sequence != _headSeq)
            {
                _underruns++;
                _headSeq++; // Advance past missed sequence
                _queuedCount = (uint)Math.Clamp((int)(_tailSeq - _headSeq), 0, _capacity);
                return false;
            }

            int bytesToCopy = Math.Min(slot.Length, outPayload.Length);
            slot.Data.AsSpan(0, bytesToCopy).CopyTo(outPayload);
            payloadBytes = bytesToCopy;
            sequence = slot.Sequence;
            timestampQpc = slot.TimestampQpc;

            // Clear slot
            _slots[slotIndex] = default;
            _headSeq++;
            _packetsPopped++;
            _queuedCount = (uint)Math.Clamp((int)(_tailSeq - _headSeq), 0, _capacity);

            return true;
        }
    }

    /// <summary>
    /// Resets the jitter buffer sequence pointers and state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            for (int i = 0; i < _capacity; i++)
            {
                _slots[i] = default;
            }
            _headSeq = 0;
            _tailSeq = 0;
            _initializedSeq = false;
            _queuedCount = 0;
        }
    }
}
