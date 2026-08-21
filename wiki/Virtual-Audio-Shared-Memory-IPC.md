# Real-Time Shared Memory IPC Bridge for Virtual Audio Driver & Host Server

The **Real-Time Shared Memory IPC Bridge** connects the user-mode Moonshine host audio engine directly to the dedicated Windows virtual audio driver endpoints (`Moonshine Audio` / Speaker and `Moonshine Microphone` / Mic) on Windows 10 and Windows 11.

---

## 1. Architectural Overview & Memory Layout

The IPC bridge provides bidirectional, zero-copy, lock-free streaming between Ring 0 / user processes and the Moonshine host server:

```
┌────────────────────────────────────────────────────────────┐
│                  Windows CoreAudio Engine                  │
└──────────────┬──────────────────────────────▲──────────────┘
               │                              │
       (Playback Stream)               (Capture Stream)
               ▼                              │
┌──────────────────────────────┐┌─────────────────────────────┐
│  Moonshine Audio (Speaker)   ││Moonshine Microphone (Record)│
│  - KSNODETYPE_SPEAKER        ││- KSNODETYPE_MICROPHONE      │
└──────────────┬───────────────┘└─────────────▲───────────────┘
               │                              │
               ▼                              │
┌────────────────────────────────────────────────────────────┐
│      Win32 Shared Memory Mappings (Global\ Named IPC)       │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Cacheline 1 (64B): Producer Write State & Atomic Pos │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ Cacheline 2 (64B): Consumer Read State & Underruns   │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ Cacheline 3 (64B): Format Descriptors & Volume State │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │ 32-Byte Aligned SIMD Audio Ring Buffer Region        │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────┬──────────────────────────────▲──────────────┘
               │                              │
               ▼                              │
┌────────────────────────────────────────────────────────────┐
│          Host Server (VirtualAudioIpcBridgePipeline)       │
│   - MMCSS "Pro Audio" Thread Priority Scheduling           │
│   - Opus Encoding & RTP Packetisation                      │
│   - Client Microphone Stream Injection                      │
│   - Micro Crossfade Glitch Suppression Fallback            │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Lock-Free Cache Coherency & Memory Barriers

To eliminate thread contention and CPU spinning across processes:

* **Cacheline Isolation**: Producer state (`write_position_bytes`, `write_packet_count`) and consumer state (`read_position_bytes`, `read_packet_count`, `underrun_count`, `overrun_count`) occupy dedicated 64-byte cachelines separated by explicit padding.
* **Acquire/Release Ordering**:
  - Producers issue `std::atomic_thread_fence(std::memory_order_release)` prior to committing write positions.
  - Consumers issue `std::atomic_thread_fence(std::memory_order_acquire)` prior to reading payload buffers.
* **SIMD Alignment**: The ring data buffer starts at byte offset 192 (a multiple of 64 bytes), guaranteeing 32-byte alignment for AVX2/AVX-512 SIMD vectorization during copy and format transformation.

---

## 3. MMCSS Event-Driven Signaling & Zero CPU Spinning

* **Named Win32 Synchronization**: Each channel pairs memory-mapped buffers with named Win32 Events (`Global\Moonshine_Audio_Event_Render` and `Global\Moonshine_Audio_Event_Capture`).
* **Microsecond Wake Latency**: Producers signal events on fixed chunk intervals (5-10ms, 240-480 samples @ 48kHz).
* **MMCSS Pro Audio Scheduling**: Background streaming worker threads register with the Multimedia Class Scheduler Service via `AvSetMmThreadCharacteristicsW(L"Pro Audio", ...)` to ensure guaranteed real-time execution priority without audio dropouts.

---

## 4. Security & Access Control (DACL)

To enable seamless cross-session communication between standard user processes, elevated admin tasks, and system services:

* **Security Descriptor String (SDDL)**:
  ```
  D:(A;;GA;;;WD)(A;;GA;;;AC)(A;;GA;;;S-1-15-2-1)
  ```
  Grants `GENERIC_ALL` access to Everyone (`WD`), All Application Packages (`AC`), and AppContainer profiles (`S-1-15-2-1`).
* **Namespace Hierarchy**: Automatically targets `Global\` namespace with seamless fallback to `Local\` session namespace when running non-elevated unit test runners.

---

## 5. Overrun, Underrun & Micro Crossfade Glitch Suppression

* **Overrun Strategy**: If the write buffer is exhausted, the oldest audio frame is automatically advanced and dropped to prevent progressive latency buildup.
* **Underrun Mitigation**: If insufficient frames are present during consumer reads, the buffer is zero-padded with silence, eliminating audible crackling and pops.
* **Micro Crossfade Fallback**: When transitioning on the fly between the virtual audio driver and native WASAPI loopback capture, a 5-10ms quadratic gain ramp crossfade (`ApplyCrossfade`) is executed to suppress transient switching clicks.

---

## 6. Managed Pipeline Usage Example

```csharp
using Moonshine.Host.Audio;

// Initialize Virtual Audio IPC Bridge Pipeline
using var ipcPipeline = new VirtualAudioIpcBridgePipeline(
    isHostServer: true,
    sampleRate: 48000,
    channels: 2
);

// Enable real-time Pro Audio MMCSS thread scheduling
ipcPipeline.TryEnableMmcss();

// Audio Processing Loop
Span<float> renderBuffer = stackalloc float[960]; // 10ms of stereo audio
while (streamingActive)
{
    // Wait for driver event notification
    if (ipcPipeline.WaitRenderEvent(timeoutMs: 15))
    {
        int readSamples = ipcPipeline.ReadRenderPcm(renderBuffer);
        if (readSamples > 0)
        {
            // Submit renderBuffer to Opus audio encoder
        }
    }
}
```
