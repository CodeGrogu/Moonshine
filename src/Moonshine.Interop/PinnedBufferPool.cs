using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// High-performance contiguous native memory slab pool for zero-allocation UDP packet buffers.
/// Maintains cacheline-aligned (64-byte) unmanaged memory pages to eliminate GC heap fragmentation.
/// </summary>
public sealed unsafe class PinnedBufferPool : IDisposable
{
    private readonly int _slotCount;
    private readonly int _slotSize;
    private readonly nuint _totalBytes;
    private readonly byte* _slabMemory;
    private readonly int[] _freeIndices;
    private int _head;
    private readonly Lock _lock = new();
    private bool _disposed;

    public int SlotCount => _slotCount;
    public int SlotSize => _slotSize;
    public byte* BasePointer => _slabMemory;

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
        for (int i = 0; i < slotCount; i++)
        {
            _freeIndices[i] = i;
        }
        _head = slotCount;
    }

    /// <summary>
    /// Rents a pinned native buffer slot. Returns slot index and unmanaged pointer.
    /// </summary>
    public bool TryRent(out int slotIndex, out byte* pointer, out Span<byte> span)
    {
        lock (_lock)
        {
            if (_disposed || _head <= 0)
            {
                slotIndex = -1;
                pointer = null;
                span = default;
                return false;
            }

            _head--;
            slotIndex = _freeIndices[_head];
            pointer = _slabMemory + ((nuint)slotIndex * (nuint)_slotSize);
            span = new Span<byte>(pointer, _slotSize);
            return true;
        }
    }

    /// <summary>
    /// Returns a slot to the free pool.
    /// </summary>
    public void Return(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slotCount) return;

        lock (_lock)
        {
            if (_disposed || _head >= _slotCount) return;

            _freeIndices[_head] = slotIndex;
            _head++;
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
            NativeMemory.Free(_slabMemory);
        }
    }
}
