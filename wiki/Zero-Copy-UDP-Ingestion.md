# High-Throughput Zero-Copy UDP Ingestion Pipeline

Moonshine implements an ultra-high-throughput UDP packet receiver engineered to ingest up to 250,000 packets per second (150+ Mbps video bitrates) with zero Garbage Collection allocations in the streaming hot path.

---

## 1. Zero-Copy Pipeline Architecture

```
UDP Socket (Kernel)
      │
      │ 8MB - 16MB Socket OS Buffer (SO_RCVBUF)
      ▼
PinnedBufferPool (Native Slab Memory)
      │
      │ Pinned unmanaged cacheline-aligned (64B) memory pages
      ▼
UdpSocketPipeline (Hot Path Ingestion Loop)
      │
      ├─► RtpHeader.TryParse (0-allocation span parsing)
      ├─► RtpSequenceUnwrapper (64-bit monotonic epoch counter)
      ├─► Sequence discontinuity & loss metrics tracking
      ▼
Native Interop Dispatch
      │
      ├─► MoonshineNativeMethods.SpscEnqueue (Lock-free C++23 SPSC queue)
      └─► SIMD AVX2/AVX-512 Galois Field FEC / Jitter Buffer
```

---

## 2. Pinned Native Buffer Pool (`PinnedBufferPool`)

To eliminate GC pause jitter and heap fragmentation during high-bitrate streaming:

- **Contiguous Native Slab**: Allocates a continuous unmanaged memory slab via `NativeMemory.AllocZeroed` on startup:
  $$\text{TotalBytes} = \text{SlotCount} \times \text{SlotSize}$$
- **Fixed Slot Sizing**: Default 2048 slots of 2048 bytes (4 MB contiguous native block) matching network MTUs.
- **Cacheline Alignment**: Memory boundaries are aligned to 64-byte L1 CPU cacheline boundaries to prevent false sharing and misaligned vector reads.
- **O(1) Lease/Return Lifecycle**: Zero heap allocations when leasing slots (`TryRent`) and returning slots (`Return`).

---

## 3. Real-Time Packet Demuxing and Descriptors

The UDP ingestion engine parses incoming datagrams in-place over `ReadOnlySpan<byte>`:

1. **RTP Video Header**:
   - Parses RFC 3550 12-byte header using `MemoryMarshal.Read<RtpHeader>`.
   - Extracts payload identifier (H.264: 96, HEVC: 98, AV1: 100).
   - Extracts marker bit (Bit 7 of payload type) signifying end of frame.
2. **Monotonic Sequence Unwrapping**:
   - Converts 16-bit wrapping RTP sequence numbers ($0 \dots 65535$) into a monotonic 64-bit epoch sequence using signed modular arithmetic:
     $$\Delta = (s_n - s_{n-1}) \pmod{2^{16}}$$
3. **C-ABI Blittable Descriptor Dispatch**:
   - Constructs a 1:1 binary-compatible `MoonshinePacketDesc` struct containing raw payload pointers (`byte*`), sequence number, frame timestamp, payload size, and frame boundary flags.
   - Enqueues directly into the lock-free single-producer single-consumer circular ring buffer in C++23 (`moonshine_spsc_enqueue`).

---

## 4. Telemetry and Diagnostic Metrics

`UdpSocketPipeline` maintains atomic volatile performance metrics:
- **`PacketsReceived`**: Total datagram count received by socket.
- **`BytesReceived`**: Cumulative byte throughput ingested.
- **`PacketsDropped`**: Count of truncated packets or queue overflow drops.
- **`SequenceDiscontinuities`**: Gap detection in RTP sequence continuity used for predictive RTCP feedback.
