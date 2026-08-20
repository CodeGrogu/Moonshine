# 1000Hz High-Resolution Raw Input Engine

Moonshine incorporates a dedicated, ultra-low-latency 1000Hz (1.0ms) input polling and dispatch pipeline delivering immediate mouse, keyboard, and gamepad state synchronization to the remote GameStream/Sunshine host.

---

## 1. High-Resolution Input Architecture

```
Hardware Input Devices (Mouse / Keyboard / Gamepad)
                     │
                     │ Raw Input / Windows Messages / XInput Polling
                     ▼
InputPollingEngine (Dedicated Real-Time Highest-Priority Polling Thread)
                     │
                     ├─► 1000Hz High-Precision Polling Loop (QueryPerformanceCounter / Stopwatch)
                     ├─► Atomic Interlocked delta accumulation (Zero GC Allocations)
                     │
                     ▼
Binary Input Packets (Packed StructLayout byte serialization)
                     │
                     ├─► MouseMovePacket (12 bytes: Relative DX/DY)
                     ├─► MouseButtonPacket (8 bytes: Button Index, Press/Release)
                     ├─► KeyboardPacket (8 bytes: KeyCode, Modifiers)
                     ├─► ControllerStatePacket (20 bytes: Analog Sticks, Triggers, Buttons)
                     └─► MouseScrollPacket (8 bytes: Wheel Delta)
                     │
                     ▼
Unmanaged UDP Control Pipeline (Direct Socket Transmit < 0.2ms)
```

---

## 2. Binary Packet Layout Specifications

All input structures are packed with 1-byte alignment (`Pack = 1`) and serialized using big-endian binary primitives for direct network transmission:

### A. Relative Mouse Motion (`MouseMovePacket` - 12 Bytes)
| Offset | Type | Field Name | Description |
| :--- | :--- | :--- | :--- |
| `0..3` | `uint32` | `PacketType` | `0x00000007` (`MouseMoveRel`) |
| `4..5` | `int16` | `DeltaX` | Relative horizontal motion delta |
| `6..7` | `int16` | `DeltaY` | Relative vertical motion delta |
| `8..11` | `uint32` | `Padding` | Reserved 32-bit zero boundary |

### B. Gamepad Controller State (`ControllerStatePacket` - 20 Bytes)
| Offset | Type | Field Name | Description |
| :--- | :--- | :--- | :--- |
| `0..3` | `uint32` | `PacketType` | `0x0000000A` (`ControllerState`) |
| `4` | `byte` | `ControllerNumber` | Controller index (0 to 3) |
| `5` | `byte` | `Reserved` | Reserved zero padding |
| `6..7` | `uint16` | `Buttons` | Bitmask of digital button states |
| `8` | `byte` | `LeftTrigger` | Analog trigger (0 to 255) |
| `9` | `byte` | `RightTrigger` | Analog trigger (0 to 255) |
| `10..11` | `int16` | `LeftStickX` | Left analog stick X (-32768 to 32767) |
| `12..13` | `int16` | `LeftStickY` | Left analog stick Y (-32768 to 32767) |
| `14..15` | `int16` | `RightStickX` | Right analog stick X (-32768 to 32767) |
| `16..17` | `int16` | `RightStickY` | Right analog stick Y (-32768 to 32767) |
| `18..19` | `int16` | `Padding` | Struct 16-bit alignment padding |

---

## 3. 1000Hz Timing Discipline & Jitter Elimination

1. **Dedicated Polling Loop**:
   - The poller runs on a dedicated high-priority thread (`ThreadPriority.Highest`) pinned to an isolated hardware core.
   - Computes sub-millisecond ticks via `Stopwatch.Frequency` and `Stopwatch.GetTimestamp()`.
2. **Atomic Interlocked Staging**:
   - High-DPI mouse movements arriving between 1000Hz ticks are accumulated atomically via `Interlocked.Add`, eliminating race conditions and mutex locks.
3. **Zero-Allocation Stack Buffers**:
   - Packets are serialized directly into stack-allocated spans (`stackalloc byte[32]`) and dispatched directly to the UDP socket pipeline.
