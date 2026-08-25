using System.Buffers;
using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// Represents a strictly-scoped lease over unmanaged native memory.
/// Guarantees single-owner semantics and guards against use-after-free or double-release faults.
/// </summary>
public sealed unsafe class NativeBufferLease : IDisposable
{
    private readonly NativeMemoryOwner _owner;
    private readonly byte* _pointer;
    private readonly int _length;
    private int _disposed;

    internal NativeBufferLease(NativeMemoryOwner owner, byte* pointer, int length)
    {
        _owner = owner;
        _pointer = pointer;
        _length = length;
    }

    public byte* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _pointer;
        }
    }

    public int Length => _length;

    public Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return new Span<byte>(_pointer, _length);
        }
    }

    public Memory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _owner.Memory.Slice(0, _length);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.ReleaseLease();
        }
    }
}

/// <summary>
/// High-performance unmanaged memory owner wrapping native memory allocations
/// to provide zero-allocation IMemoryOwner and Span/Memory instances with strict lease/release lifetime semantics.
/// Guarantees race-free deferred memory deallocation until all active leases have completed.
/// </summary>
public sealed unsafe class NativeMemoryOwner : MemoryManager<byte>, IDisposable
{
    private readonly byte* _pointer;
    private readonly int _length;
    private readonly bool _ownsAllocation;
    private int _activeLeases;
    private int _disposed;
    private int _freed;

    public NativeMemoryOwner(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _length = length;
        _pointer = (byte*)NativeMemory.AllocZeroed((nuint)length);
        _ownsAllocation = true;
    }

    public NativeMemoryOwner(byte* pointer, int length, bool ownsAllocation = false)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _pointer = pointer;
        _length = length;
        _ownsAllocation = ownsAllocation;
    }

    public byte* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _pointer;
        }
    }

    public int Length => _length;
    public bool OwnsAllocation => _ownsAllocation;
    public int ActiveLeases => Volatile.Read(ref _activeLeases);
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Acquires an explicit lease over this unmanaged memory slab.
    /// Atomically increments active leases first and checks disposal state to prevent races with concurrent Dispose().
    /// </summary>
    public NativeBufferLease Lease()
    {
        Interlocked.Increment(ref _activeLeases);
        if (Volatile.Read(ref _disposed) != 0)
        {
            ReleaseLease();
            throw new ObjectDisposedException(GetType().FullName);
        }

        return new NativeBufferLease(this, _pointer, _length);
    }

    internal void ReleaseLease()
    {
        int remaining = Interlocked.Decrement(ref _activeLeases);
        if (remaining < 0)
        {
            throw new InvalidOperationException("Double-release detected: active lease count dropped below zero.");
        }

        // If the owner was marked disposed while leases were active, the final lease release triggers the free
        if (remaining == 0 && Volatile.Read(ref _disposed) != 0)
        {
            FreeAllocationIfOwned();
        }
    }

    private void FreeAllocationIfOwned()
    {
        if (_ownsAllocation && _pointer != null)
        {
            if (Interlocked.Exchange(ref _freed, 1) == 0)
            {
                NativeMemory.Free(_pointer);
            }
        }
    }

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if ((uint)elementIndex > (uint)_length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        }

        return new MemoryHandle(_pointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Only deallocate immediately if there are zero active leases
            if (Volatile.Read(ref _activeLeases) == 0)
            {
                FreeAllocationIfOwned();
            }
        }
    }
}
