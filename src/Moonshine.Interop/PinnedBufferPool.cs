using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// State tracking for buffer pool slot lifecycle.
/// </summary>
public enum SlotState : byte
{
    Free = 0,
    Rented = 1,
    InFlight = 2
}

/// <summary>
/// High-performance contiguous native memory slab pool for zero-allocation UDP packet buffers.
/// Maintains cacheline-aligned (64-byte) unmanaged memory pages to eliminate GC heap fragmentation.
/// Incorporates an unmanaged SPSC return queue for lock-free cross-boundary slot reclamation.
/// Thread Topology & Invariants:
/// - Return-Ring Producer: Each return ring has exactly one producer: the stream's dedicated native forward-queue consumer thread, which is also the sole native consumer of MoonshinePacketDesc for that stream.
/// - Return-Ring Consumer: TryRent() drains recycled slot indices exclusively on the stream's managed UDP ingestion thread.
/// - Stream Isolation: Each UDP pipeline (Video, Audio, Mic) instantiates its own dedicated PinnedBufferPool and return queue.
/// </summary>
public sealed unsafe class PinnedBufferPool : IDisposable
{
    private readonly int _slotCount;
    private readonly int _slotSize;
    private readonly nuint _totalBytes;
    private readonly byte* _slabMemory;
    private readonly int[] _freeIndices;
    private readonly SlotState[] _slotStates;
    private int _head;
    private readonly Lock _lock = new();
    private readonly IntPtr _returnRingHandle;
    private bool _disposed;

    public int SlotCount => _slotCount;
    public int SlotSize => _slotSize;
    public byte* BasePointer => _slabMemory;
    public IntPtr ReturnQueueHandle => _returnRingHandle;

    public int FreeCount
    {
        get
        {
            lock (_lock) { return _head; }
        }
    }

    public int RentedCount
    {
        get
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < _slotCount; i++)
                {
                    if (_slotStates[i] == SlotState.Rented) count++;
                }
                return count;
            }
        }
    }

    public int InFlightCount
    {
        get
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < _slotCount; i++)
                {
                    if (_slotStates[i] == SlotState.InFlight) count++;
                }
                return count;
            }
        }
    }

    public PinnedBufferPool(int slotCount = 2048, int slotSize = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotSize);

        _slotCount = slotCount;
        _slotSize = slotSize;
        _totalBytes = (nuint)slotCount * (nuint)slotSize;

        // Allocate aligned unmanaged memory block
        _slabMemory = (byte*)NativeMemory.AllocZeroed(_totalBytes);

        _freeIndices = new int[slotCount];
        _slotStates = new SlotState[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            _freeIndices[i] = i;
            _slotStates[i] = SlotState.Free;
        }
        _head = slotCount;

        // Instantiate unmanaged lock-free SPSC return queue
        _returnRingHandle = MoonshineNativeMethods.SlotReturnCreate((nuint)slotCount);
    }

    /// <summary>
    /// Rents a pinned native buffer slot. Drains recycled slots from the return ring before allocation.
    /// </summary>
    public bool TryRent(out int slotIndex, out byte* pointer, out Span<byte> span)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                slotIndex = -1;
                pointer = null;
                span = default;
                return false;
            }

            // Drain all recycled slots from the unmanaged return ring
            if (_returnRingHandle != IntPtr.Zero)
            {
                while (MoonshineNativeMethods.SlotReturnDequeue(_returnRingHandle, out int recycledSlot) != 0)
                {
                    if (recycledSlot >= 0 && recycledSlot < _slotCount)
                    {
                        if (_slotStates[recycledSlot] == SlotState.InFlight)
                        {
                            _slotStates[recycledSlot] = SlotState.Free;
                            _freeIndices[_head] = recycledSlot;
                            _head++;
                        }
                    }
                }
            }

            if (_head <= 0)
            {
                slotIndex = -1;
                pointer = null;
                span = default;
                return false;
            }

            _head--;
            slotIndex = _freeIndices[_head];
            _slotStates[slotIndex] = SlotState.Rented;
            pointer = _slabMemory + ((nuint)slotIndex * (nuint)_slotSize);
            span = new Span<byte>(pointer, _slotSize);
            return true;
        }
    }

    /// <summary>
    /// Transitions a rented slot to InFlight state upon publication to the forward SPSC queue.
    /// </summary>
    public void MarkInFlight(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) return;

        lock (_lock)
        {
            if (_disposed) return;
            if (_slotStates[slotIndex] == SlotState.Rented)
            {
                _slotStates[slotIndex] = SlotState.InFlight;
            }
        }
    }

    /// <summary>
    /// Returns a slot from Rented state directly to Free (e.g. non-native managed completion).
    /// </summary>
    public void ReturnRented(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) return;

        lock (_lock)
        {
            if (_disposed || _head >= _slotCount) return;
            if (_slotStates[slotIndex] == SlotState.Rented)
            {
                _slotStates[slotIndex] = SlotState.Free;
                _freeIndices[_head] = slotIndex;
                _head++;
            }
        }
    }

    /// <summary>
    /// Returns an InFlight slot back to Free (e.g. forward SPSC enqueue failure).
    /// </summary>
    public void ReturnInFlight(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) return;

        lock (_lock)
        {
            if (_disposed || _head >= _slotCount) return;
            if (_slotStates[slotIndex] == SlotState.InFlight)
            {
                _slotStates[slotIndex] = SlotState.Free;
                _freeIndices[_head] = slotIndex;
                _head++;
            }
        }
    }

    /// <summary>
    /// General return API returning a Rented or InFlight slot back to Free.
    /// </summary>
    public void Return(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) return;

        lock (_lock)
        {
            if (_disposed || _head >= _slotCount) return;
            if (_slotStates[slotIndex] == SlotState.Rented || _slotStates[slotIndex] == SlotState.InFlight)
            {
                _slotStates[slotIndex] = SlotState.Free;
                _freeIndices[_head] = slotIndex;
                _head++;
            }
        }
    }

    /// <summary>
    /// Validates the fundamental invariant: FreeCount + RentedCount + InFlightCount == SlotCount.
    /// </summary>
    public bool ValidateInvariant()
    {
        lock (_lock)
        {
            int free = _head;
            int rented = 0;
            int inFlight = 0;
            for (int i = 0; i < _slotCount; i++)
            {
                if (_slotStates[i] == SlotState.Rented) rented++;
                else if (_slotStates[i] == SlotState.InFlight) inFlight++;
            }
            return (free + rented + inFlight) == _slotCount;
        }
    }

    /// <summary>
    /// Gets direct pointer for a slot index.
    /// </summary>
    public byte* GetPointer(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        return _slabMemory + ((nuint)slotIndex * (nuint)_slotSize);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_returnRingHandle != IntPtr.Zero)
            {
                MoonshineNativeMethods.SlotReturnDestroy(_returnRingHandle);
            }

            NativeMemory.Free(_slabMemory);
        }
    }
}
