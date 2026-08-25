# Moonshine Virtual Audio Driver Specification

## 1. Overview

The Moonshine Virtual Audio Driver provides dedicated, low-latency, kernel-level audio endpoints for the Moonshine remote streaming platform on Microsoft Windows. It exposes two distinct CoreAudio streaming endpoints to the Windows audio subsystem:

* **Moonshine Audio** (Render / Playback Endpoint): Captures host system audio output with deterministic hardware position reporting and routes raw PCM frames to the user-mode streaming pipeline.
* **Moonshine Microphone** (Capture / Recording Endpoint): Injects remote client voice audio into the host operating system, appearing as a standard recording peripheral to games, voice communication applications, and system utilities.

```
+---------------------------------------------------------------+
|                   Windows Audio Subsystem                     |
|                 (WASAPI / CoreAudio Engine)                   |
+-------------------------------+-------------------------------+
                                | (Render / Capture Streams)
                                v
+---------------------------------------------------------------+
|      PortCls WaveRT Kernel-Mode Driver (MoonshineAudio.sys)   |
|  - Miniport WaveRT (IMiniportWaveRT / IMiniportWaveRTStream)  |
|  - Miniport Topology (IMiniportTopology)                      |
|  - Cyclic DMA Page Allocation & Hardware Position Tracking    |
+-------------------------------+-------------------------------+
                                | (Shared Memory Ring Buffer Contract)
                                v
+---------------------------------------------------------------+
|             User-Mode Native IPC Bridge Layer                 |
|               (virtual_audio_ipc.cpp / .hpp)                  |
|  - Lock-Free Single-Producer Single-Consumer (SPSC) Ring      |
|  - Cacheline-Isolated Headers (64-byte alignment)             |
|  - Win32 Named Shared Memory & Named Event Signalling         |
+-------------------------------+-------------------------------+
                                | (P/Invoke C-ABI Bridge)
                                v
+---------------------------------------------------------------+
|         Managed Host Service (VirtualAudioDriverService.cs)   |
|  - Endpoint Lifecycle & Status Telemetry                      |
|  - MMCSS Pro Audio Priority Thread Scheduling                 |
|  - Opus Compression, Jitter Buffering, Network Transport      |
+---------------------------------------------------------------+
```

The driver is implemented as a Windows Driver Kit (WDK) PortCls WaveRT miniport driver producing a kernel-mode binary (`MoonshineAudio.sys`). Audio frames cross the kernel/user boundary through a high-performance, lock-free shared memory ring buffer contract. All computational workloads, including Opus codec compression, acoustic echo cancellation, jitter handling, network packetisation, and stream routing policies, remain strictly isolated within user mode to ensure kernel stability and prevent system crashes.

---

## 2. Architecture Layers

The virtual audio subsystem is structured across three distinct operational layers:

### 2.1. Kernel-Mode Driver Layer (`drivers/audio/`)
The kernel driver produces [MoonshineAudio.sys](../drivers/audio/MoonshineAudio.vcxproj) and executes in Ring 0 under the Windows Port Class (`PortCls.sys`) framework. It comprises three primary miniport components:
* **Adapter Common** ([adapter.hpp](../drivers/audio/adapter.hpp), [adapter.cpp](../drivers/audio/adapter.cpp)): Coordinates adapter initialisation, Plug and Play (PnP) resource management, and power state transitions.
* **WaveRT Miniport** ([minwave.hpp](../drivers/audio/minwave.hpp), [minwave.cpp](../drivers/audio/minwave.cpp)): Implements `IMiniportWaveRT` and `IMiniportWaveRTStream` interfaces. It allocates 4KB page-aligned cyclic shared memory buffers from non-paged pool (`ExAllocatePool2` / `IoAllocateMdl`), manages emulated stream position registers (`GetPosition`), and handles streaming state transitions without physical DMA hardware.
* **Topology Miniport** ([mintopo.hpp](../drivers/audio/mintopo.hpp), [mintopo.cpp](../drivers/audio/mintopo.cpp)): Implements `IMiniportTopology` to expose device topology pins (`KSNODETYPE_SPEAKER` and `KSNODETYPE_MICROPHONE`), volume controls, and mute states to the audio engine.

### 2.2. User-Mode IPC Bridge Layer (`src/Moonshine.Native/src/audio/virtual_audio_ipc.cpp`)
The native IPC bridge provides a zero-copy, lock-free Single-Producer Single-Consumer (SPSC) communication channel between the driver endpoints and user-mode processes:
* Uses Win32 Named File Mappings (`CreateFileMappingW` / `MapViewOfFile`) backed by system paging memory.
* Synchronises buffer reads and writes via Win32 Named Auto-Reset Events (`CreateEventW` / `SetEvent`).
* Employs standard C++23 `std::atomic_ref<uint32_t>` primitives with acquire/release memory ordering semantics (`std::memory_order_acquire` / `std::memory_order_release`) to guarantee memory ordering across memory-mapped boundaries without kernel mutexes or spinlocks.
* Enforces strict Discretionary Access Control Lists (DACLs) granting access exclusively to `NT AUTHORITY\SYSTEM` (`S-1-5-18`), `Builtin\Administrators` (`S-1-5-32-544`), and the active user security identifier (SID). Prohibits broad access groups such as `Everyone` (`WD`) and `Authenticated Users` (`AU`).

### 2.3. Managed Service Layer (`src/Moonshine.Host/Audio/VirtualAudioDriverService.cs`)
The managed service operates within the Moonshine host server process:
* Interacts with the native runtime via high-speed P/Invoke C-ABI exports (`VirtualAudioDriverCreate`, `VirtualAudioDriverGetStatus`, `VirtualAudioDriverValidateFormat`).
* Enforces Multimedia Class Scheduler Service (MMCSS) registration under the `Pro Audio` task profile via `AvSetMmThreadCharacteristicsW` to eliminate scheduling jitter and buffer underruns.
* Coordinates endpoint enumeration, format validation, dynamic fallbacks to WASAPI loopback capture, and automated device lifecycle management.

---

## 3. Shared Buffer Contract

The shared memory structure is defined in [drivers/audio/shared_audio_buffer.h](../drivers/audio/shared_audio_buffer.h) as `MoonshineSharedAudioRing`. The header structure spans three consecutive 64-byte cachelines (192 bytes total) to eliminate false sharing between concurrent producer and consumer threads:

```c
typedef struct MoonshineSharedAudioRing {
    /* 64-byte Cacheline 1: Producer Write State */
    uint64_t magic;                     /* 0x00: 0x314455414E48534D ("MSHNAUD1") */
    uint32_t version;                   /* 0x08: Protocol version (1) */
    uint32_t endpoint_type;             /* 0x0C: 0 = Render (Speaker), 1 = Capture (Mic) */
    volatile uint32_t write_position_bytes; /* 0x10: Atomic producer write offset */
    volatile uint32_t write_packet_count;   /* 0x14: Total frames written */
    uint8_t pad1[40];                   /* 0x18: Padding to 64 bytes */

    /* 64-byte Cacheline 2: Consumer Read State */
    volatile uint32_t read_position_bytes;  /* 0x40: Atomic consumer read offset */
    volatile uint32_t read_packet_count;    /* 0x44: Total frames consumed */
    volatile uint32_t underrun_count;   /* 0x48: Total underrun events */
    volatile uint32_t overrun_count;    /* 0x4C: Total overrun events */
    uint8_t pad2[48];                   /* 0x50: Padding to 128 bytes */

    /* 64-byte Cacheline 3: Audio Format Parameters */
    uint32_t sample_rate;               /* 0x80: e.g. 48000 Hz */
    uint32_t channels;                  /* 0x84: e.g. 2 (Stereo), 6 (5.1), 8 (7.1) */
    uint32_t sample_format;             /* 0x88: 1=PCM16, 2=PCM24, 3=PCM32, 4=Float32 */
    uint32_t bytes_per_sample;          /* 0x8C: e.g. 4 for Float32 */
    uint32_t frame_size_bytes;          /* 0x90: channels * bytes_per_sample * period_samples */
    uint32_t buffer_capacity_bytes;     /* 0x94: Total ring capacity (16 frames) */
    uint32_t latency_ms;                /* 0x98: Frame period latency (10 ms) */
    volatile uint32_t is_active;        /* 0x9C: Stream streaming state flag */
    volatile uint32_t is_muted;         /* 0xA0: Endpoint mute status */
    float volume_scalar;                /* 0xA4: Linear volume multiplier (0.0f - 1.0f) */
    uint8_t pad3[24];                   /* 0xA8: Padding to 192 bytes */
} MoonshineSharedAudioRing;
```

The payload buffer begins immediately at byte offset 192, ensuring 32-byte alignment for AVX2 and AVX-512 SIMD vectorised memory operations.

### 3.1. Win32 Named IPC Identifiers
* **Render Shared Memory**: `Global\Moonshine_Audio_RingBuffer_Render`
* **Render Sync Event**: `Global\Moonshine_Audio_Event_Render`
* **Capture Shared Memory**: `Global\Moonshine_Audio_RingBuffer_Capture`
* **Capture Sync Event**: `Global\Moonshine_Audio_Event_Capture`

### 3.2. IOCTL Control Codes
* `0x8001` (`MOONSHINE_AUDIO_IOCTL_GET_STATUS`): Queries driver version, state, and active endpoint metrics.
* `0x8002` (`MOONSHINE_AUDIO_IOCTL_SET_FORMAT`): Requests audio sample rate, bit depth, or channel count reconfiguration.
* `0x8003` (`MOONSHINE_AUDIO_IOCTL_GET_BUFFER_PTR`): Maps or retrieves cyclic DMA buffer addresses.
* `0x8004` (`MOONSHINE_AUDIO_IOCTL_RESET_BUFFER`): Flushes ring buffers and zeroes underrun/overrun telemetry.

### 3.3. Audio Format and Layout Matrix
* **Supported Formats**: `PCM_16` (16-bit integer), `PCM_24` (24-bit integer packed), `PCM_32` (32-bit integer), `FLOAT_32` (32-bit IEEE floating point).
* **Supported Channel Layouts**: `Mono` (1 channel), `Stereo` (2 channels), `Surround 5.1` (6 channels), `Surround 7.1` (8 channels).
* **Supported Sample Rates**: 44,100 Hz, 48,000 Hz, 88,200 Hz, 96,000 Hz, 192,000 Hz.
* **Default Operating Mode**: 48,000 Hz Stereo Float32, 10 ms frame duration (480 samples / 3,840 bytes per frame), 16-frame ring buffer (61,440 bytes capacity).

---

## 4. WDK Build Pipeline

Building the kernel-mode driver binary requires the Microsoft Windows Driver Kit toolchain:

### 4.1. Toolchain Requirements
* **IDE**: Visual Studio 2022 (v143) or Visual Studio 2026 with the "Desktop development with C++" workload installed.
* **SDK**: Windows Software Development Kit (SDK) version `10.0.26100.0` or later.
* **WDK**: Windows Driver Kit (WDK) matching the installed Windows SDK version.
* **Libraries**: Spectre-mitigated MSVC runtime libraries (`MSVC v143 - VS 2022 C++ x64/x86 Spectre-mitigated libs`) and C++ ATL for Spectre.

### 4.2. Compilation Process
The driver project is defined in [drivers/audio/MoonshineAudio.vcxproj](../drivers/audio/MoonshineAudio.vcxproj). It targets the Universal Driver Platform (`DriverTargetPlatform=Universal`, `DriverType=KMDF`) and links against `portcls.lib` and `ks.lib`.

```cmd
msbuild.exe drivers\audio\MoonshineAudio.vcxproj /p:Configuration=Release /p:Platform=x64 /p:TargetVersion=Windows10
```

### 4.3. Dual-Mode User/Kernel Compilation
The driver headers and implementation files ([adapter.hpp](../drivers/audio/adapter.hpp), [minwave.hpp](../drivers/audio/minwave.hpp), [mintopo.hpp](../drivers/audio/mintopo.hpp)) utilise `#ifdef _KERNEL_MODE` preprocessor guards. When compiled in user mode (without `_KERNEL_MODE` defined), the classes map portcls types to standard C++ primitives, enabling comprehensive unit testing under CTest without requiring a kernel debugger or virtual test harness.

---

## 5. Driver Signing Requirements

Windows 64-bit kernel mode mandates digital signatures for all loaded driver binaries (`.sys`). Moonshine supports two deployment modes:

### 5.1. Development Mode (Test-Signing)
For internal engineering and continuous testing on local developer workstations:
1. Enable test-signing in the Windows Boot Configuration Data store (requires administrator elevation):
   ```cmd
   bcdedit /set TESTSIGNING ON
   ```
2. Reboot the system. Note: On hardware with Secure Boot enabled, Secure Boot must be disabled in UEFI firmware to allow test-signed kernel binaries.
3. Generate a local self-signed test certificate and sign the driver package:
   ```cmd
   makecert.exe -r -pe -ss MoonshineTestCertStore -n "CN=MoonshineTestDriver" MoonshineTest.cer
   signtool.exe sign /v /s MoonshineTestCertStore /n "MoonshineTestDriver" /t http://timestamp.digicert.com MoonshineAudio.sys
   ```
4. The desktop displays a "Test Mode" watermark in the bottom-right corner when active.

### 5.2. Production Mode (WHQL / Attestation Signing)
For general release distribution without requiring users to alter Secure Boot or enable test-signing:
1. Obtain an Extended Validation (EV) Code Signing Certificate or enrol in Microsoft Azure Trusted Signing.
2. Register the organisation with the Microsoft Partner Center Hardware Developer Dashboard.
3. Run the Windows Hardware Lab Kit (HLK) audio device test suite to generate an official test log package (`.hlkx`).
4. Package the driver directory into a cabinet container:
   ```cmd
   makecab.exe /f MoonshineAudio.ddf
   ```
5. Sign the cabinet file with the EV certificate using SHA256 hashing:
   ```cmd
   signtool.exe sign /fd sha256 /a /tr http://timestamp.digicert.com /td sha256 MoonshineAudio.cab
   ```
6. Submit the signed cabinet package to the Hardware Developer Center Dashboard for WHQL certification (or Attestation Signing for rapid pre-production distribution). Microsoft returns a production-signed catalogue file (`MoonshineAudio.cat`).

---

## 6. Installation and Removal

The driver package is defined by [drivers/audio/MoonshineAudio.inf](../drivers/audio/MoonshineAudio.inf). Automated installation and uninstallation are handled via PowerShell scripts or administrative CLI tools.

### 6.1. Device Installation
```powershell
# Using the automated installation script
powershell.exe -ExecutionPolicy Bypass -File scripts\install_virtual_audio_driver.ps1

# Manual installation via devcon
devcon.exe install drivers\audio\MoonshineAudio.inf ROOT\MoonshineAudio

# Alternative installation via PnPUtil
pnputil.exe /add-driver drivers\audio\MoonshineAudio.inf /install
```

### 6.2. Device Removal
```powershell
# Using the automated removal script
powershell.exe -ExecutionPolicy Bypass -File scripts\uninstall_virtual_audio_driver.ps1

# Manual removal via devcon
devcon.exe remove ROOT\MoonshineAudio

# Package removal via PnPUtil
pnputil.exe /delete-driver oemXX.inf /uninstall /force
```

### 6.3. Device Registration Details
* **Device Class**: `MEDIA` (`{4d36e96c-e325-11ce-bfc1-08002be10318}`).
* **Device Manager Category**: "Sound, video and game controllers".
* **Registered Interfaces**: `KSCATEGORY_AUDIO`, `KSCATEGORY_RENDER`, `KSCATEGORY_CAPTURE`, `KSCATEGORY_REALTIME`, `KSCATEGORY_TOPOLOGY`.
* **Service Definition**: `MoonshineAudio` (`ServiceType = 1` / `SERVICE_KERNEL_DRIVER`, `StartType = 3` / `SERVICE_DEMAND_START`).

---

## 7. Endpoint Visibility

Following successful installation and device node instantiation:
* **Moonshine Audio** appears under Windows Sound Settings, Control Panel, and CoreAudio enumerations as an active output / playback device (`KSNODETYPE_SPEAKER`).
* **Moonshine Microphone** appears as an active input / recording device (`KSNODETYPE_MICROPHONE`).
* Standard third-party software (e.g. OBS Studio, Discord, Steam, web browsers) can bind to and stream from these endpoints natively.
* Applications enumerate endpoints programmatically via Windows `IMMDeviceEnumerator`, `WASAPI`, or DirectSound interfaces without requiring custom client SDKs or proprietary audio hooks.

---

## 8. Sleep/Resume and PnP Behaviour

The driver strictly conforms to Windows Driver Model (WDM) power and PnP state management requirements:
* **PortCls Power Coordination**: The `PortCls` framework processes power management IRPs (`IRP_MJ_POWER`) on behalf of the miniport driver.
* **Adapter Power Management**: `CAdapterCommon` implements `IAdapterPowerManagement` to monitor system and device power states (`PowerDeviceD0` through `PowerDeviceD3`).
* **Power Transitions**: On transition to low-power states (`D1`, `D2`, `D3`), the driver ramps active output levels to zero to prevent acoustic clicks or transient pops, pauses DMA streaming timers, and freezes hardware position counters. The driver never accesses hardware resources when outside the `PowerDeviceD0` power state.
* **Resume Behaviour**: Upon returning to `PowerDeviceD0`, stream positions, format descriptors, and shared memory pointers are restored, signalling user-mode bridges to resume audio processing.
* **PnP Lifecycle**: Handles `IRP_MN_START_DEVICE`, `IRP_MN_QUERY_STOP_DEVICE`, `IRP_MN_STOP_DEVICE`, `IRP_MN_QUERY_REMOVE_DEVICE`, and `IRP_MN_REMOVE_DEVICE` deterministically, safely unmapping shared memory buffers before device disposal.

---

## 9. Secure Boot Deployment Matrix

| Environment | Secure Boot | Test-Signing | Signing Method | Verification Notes |
|---|---|---|---|---|
| Development (Local) | Off | On | Self-Signed Test Certificate | Requires `bcdedit /set TESTSIGNING ON`, displays watermark on desktop |
| Development (Kernel Debugger) | On / Off | N/A | Unsigned / Test-Signed | Kernel debugger attached via KDNET/WinDbg automatically bypasses signing checks |
| Pre-Production (Staging) | On | Off | Microsoft Attestation Signing | Signed via Partner Center Dashboard without HLK tests; runs on production Windows |
| Production (General Release) | On | Off | Microsoft WHQL Signed | Passes Windows Hardware Lab Kit (HLK) test suite; full catalogue signing |

---

## 10. Current Maturity Status

The Moonshine Virtual Audio Driver component is classified as **Prototype** under Rule 8 of the Moonshine architectural governance standards:

* **User-Mode IPC Layer**: Fully verified. Passes native test suites ([test_virtual_audio_ipc.cpp](../tests/Moonshine.Native.Tests/test_virtual_audio_ipc.cpp)) with concrete value assertions covering SPSC ring buffer reads/writes, underrun silence padding, overrun frame advancement, bidirectional bridge pumping, MMCSS registration, and DACL security verification via `GetSecurityInfo`.
* **Shared Buffer Contract**: Fully verified. Cacheline alignment (64-byte boundaries), struct memory packing, and atomic synchronisation barriers match the cross-process layout across native and managed layers.
* **Kernel-Mode Driver Source**: Prototype. The C++ miniport implementation files ([adapter.cpp](../drivers/audio/adapter.cpp), [minwave.cpp](../drivers/audio/minwave.cpp), [mintopo.cpp](../drivers/audio/mintopo.cpp)) provide an architecturally valid PortCls WaveRT implementation that compiles in user mode for test execution, but requires a dedicated Windows Driver Kit (WDK) build environment to generate `MoonshineAudio.sys`.
* **Real Device PnP Deployment**: Not yet tested on live hardware. Full end-to-end kernel installation and device manager enumeration will be validated following the establishment of the automated WDK continuous integration pipeline and test-signing certificate provisioning.

### 10.1. Subsystem Capability and Validation Matrix

| Capability / Subsystem Area | Current Maturity Status | Verification Context |
|---|---|---|
| PortCls driver architecture | Verified (Source) | WDM DriverEntry, AddDevice, StartDevice, subdevice registration |
| WaveRT miniport interfaces | Verified (Source) | `IMiniportWaveRT` & `IMiniportWaveRTStream` implementation |
| Topology miniport | Verified (Source) | `IMiniportTopology` & pin/node routing descriptors |
| User-mode controller | Verified | Multi-stage discovery (`SetupDi` + CoreAudio), lifecycle methods |
| C-ABI export boundary | Verified | Strict 1:1 blittable layout matching |
| IPC ring buffer architecture | Verified | Lock-free SPSC 64-byte cacheline isolated ring buffer |
| IPC security model | Verified | Strict DACL (SYSTEM, Administrators, User SID only) |
| Managed lifecycle service | Verified | `VirtualAudioDriverService` with exception & disposal safety |
| Software test suites | Verified | 25 CTests + 706 xUnit tests passing (100% of applicable tests) |
| Driver binary compilation | Prototype | Requires WDK toolchain |
| Test-signed installation | Prototype | Requires `bcdedit /set TESTSIGNING ON` |
| Actual PnP enumeration | Prototype | Requires signed `.sys` load |
| WASAPI sees the device | Prototype | Requires active KS filter registration |
| Real render stream | Prototype | Requires physical WaveRT cyclic buffer pumping |
| Real capture stream | Prototype | Requires physical WaveRT microphone injection |
| Third-party app compatibility | Prototype | Pending live endpoint testing |
| Long-duration stress stability | Prototype | Pending continuous hardware soak tests |
| Driver Verifier validation | Prototype | Pending WDK Driver Verifier test run |
| Production WHQL signing | Prototype | Pending HLK test suite submission |

### 10.2. Format Capability Scope
* **Primary Verified Streaming Path**: 48 kHz, Stereo (2-channel), 32-bit Float (`MOONSHINE_FORMAT_FLOAT_32`). This format aligns 1:1 with Moonshine's internal audio pipeline and Opus encoder.
* **Declared Pin Capabilities**: 44.1 kHz, 88.2 kHz, 96 kHz, 192 kHz sample rates, and Mono (1), 5.1 Surround (6), and 7.1 Surround (8) channel layouts with PCM16, PCM24, and PCM32 formats are declared in the miniport format descriptor table and validated in software format parsers, but remain classified as declared capabilities until physically exercised on real hardware.
