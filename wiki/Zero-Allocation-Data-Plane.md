# Zero-Allocation Data Plane

One of the primary architectural goals of Moonshine is eliminating managed Garbage Collection (GC) pauses completely during streaming sessions. In legacy streaming clients, high-frequency socket buffer allocations cause periodic Gen0 and Gen1 GC sweeps, resulting in micro-stutter and frame presentation spikes.

---

## 1. Zero-Allocation Design Principles

```
[UDP Datagram Ingestion]
        │
        ▼  (Zero-Copy)
[Pinned Native Slab (NativeMemoryOwner)]
        │
        ▼  (Zero-Alloc Slice)
[ReadOnlySpan<byte> and RtpHeader.TryParse]
        │
        ▼  (Blittable Pointer Pass)
[Lock-Free SPSC Ring Buffer]
        │
        ▼  (AVX2 SIMD In-Place Recovery)
[Direct3D Hardware Decoder Texture Surface]
```

### Key Pillars:
1. No Managed Heap Allocations per Packet: Every UDP packet received is read directly into a pre-allocated unmanaged memory pool (`NativeMemoryOwner`). Slicing and header extraction are performed entirely via `ReadOnlySpan<byte>`.
2. ValueTask and Ref Struct Discipline: Asynchronous socket calls return `ValueTask` or execute on dedicated polling threads without Task allocations.
3. ArrayPool and Pinned Buffers: Any auxiliary scratch buffers are rented from `ArrayPool<byte>.Shared` and returned immediately upon frame dispatch.

---

## 2. Pinned Native Memory Arena (NativeMemoryOwner)

Moonshine implements `System.Buffers.IMemoryOwner<byte>` backed by `NativeMemory.Alloc`:

```csharp
public sealed unsafe class NativeMemoryOwner : IMemoryOwner<byte>
{
    private readonly void* _pointer;
    private readonly int _length;
    private int _disposed;

    public NativeMemoryOwner(int length)
    {
        _length = length;
        _pointer = NativeMemory.Alloc((nuint)length);
    }

    public Memory<byte> Memory => new NativeMemoryManager(_pointer, _length).Memory;

    public void* UnsafePointer => _pointer;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            NativeMemory.Free(_pointer);
        }
    }
}
```

---

## 3. High-Performance Span-Based RTP Parsing

RTP packet parsing operates on stack spans without creating managed objects:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly ref struct RtpHeader
{
    public byte Version { get; init; }
    public bool HasPadding { get; init; }
    public bool HasExtension { get; init; }
    public byte CsrcCount { get; init; }
    public bool Marker { get; init; }
    public byte PayloadType { get; init; }
    public ushort SequenceNumber { get; init; }
    public uint Timestamp { get; init; }
    public uint Ssrc { get; init; }
    public ReadOnlySpan<byte> Payload { get; init; }

    public static bool TryParse(ReadOnlySpan<byte> data, out RtpHeader header)
    {
        if (data.Length < 12)
        {
            header = default;
            return false;
        }

        byte b0 = data[0];
        byte b1 = data[1];
        ushort seq = BinaryPrimitives.ReadUInt16BigEndian(data[2..4]);
        uint ts = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(data[8..12]);

        header = new RtpHeader
        {
            Version = (byte)((b0 >> 6) & 0x03),
            HasPadding = (b0 & 0x20) != 0,
            HasExtension = (b0 & 0x10) != 0,
            CsrcCount = (byte)(b0 & 0x0F),
            Marker = (b1 & 0x80) != 0,
            PayloadType = (byte)(b1 & 0x7F),
            SequenceNumber = seq,
            Timestamp = ts,
            Ssrc = ssrc,
            Payload = data[12..]
        };

        return true;
    }
}
```
