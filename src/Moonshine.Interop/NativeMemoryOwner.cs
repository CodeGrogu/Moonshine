using System.Buffers;
using System.Runtime.InteropServices;

namespace Moonshine.Interop;

/// <summary>
/// High-performance unmanaged memory owner wrapping pinned/native memory slabs
/// to provide zero-allocation IMemoryOwner and Span/Memory instances to System.IO.Pipelines.
/// </summary>
public sealed unsafe class NativeMemoryOwner : MemoryManager<byte>
{
    private readonly byte* _pointer;
    private readonly int _length;
    private bool _disposed;

    public NativeMemoryOwner(int length)
    {
        _length = length;
        _pointer = (byte*)NativeMemory.AllocZeroed((nuint)length);
    }

    public NativeMemoryOwner(byte* pointer, int length)
    {
        _pointer = pointer;
        _length = length;
    }

    public byte* Pointer => _pointer;

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)elementIndex > (uint)_length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        }

        return new MemoryHandle(_pointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            NativeMemory.Free(_pointer);
        }
    }
}
