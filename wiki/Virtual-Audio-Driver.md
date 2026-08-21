# Dedicated Windows Virtual Audio Driver

The **Moonshine Virtual Audio Driver** is a custom Microsoft Windows Driver Kit (WDK) PortCls WaveRT miniport driver (`IMiniportWaveRT` and `IMiniportTopology`) providing isolated, persistent hardware audio endpoints for the Moonshine host ecosystem on Windows 11.

---

## 1. Architectural Topology & Endpoints

The driver exposes two distinct Windows CoreAudio streaming endpoints:

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
│  - Stereo 2.0 / 5.1 / 7.1    ││- 48kHz Mono / Stereo Voice  │
│  - 44.1kHz - 192kHz Rates    ││- Sub-millisecond Buffer Pos │
└──────────────┬───────────────┘└─────────────▲───────────────┘
               │                              │
               ▼                              │
┌────────────────────────────────────────────────────────────┐
│          PortCls WaveRT Miniport (MoonshineAudio.sys)      │
│   - Cyclic DMA Buffer Allocation (4KB page aligned)        │
│   - Hardware Position Register Tracking                    │
│   - Lock-Free 64-Byte Cacheline Shared Ring Buffer         │
└──────────────────────────────┬─────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────┐
│             User-Mode Moonshine Host Coordinator           │
│   - VirtualAudioDriverController & VirtualAudioDriverService│
│   - MMCSS Pro Audio Priority Thread Scheduling             │
│   - Automated PnP Deployment (PnPUtil & DevCon)            │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Multi-Format & Sample Rate Negotiation

To eliminate initialization crashes in legacy games and media players that query non-default formats, the driver pin descriptors define broad `KSDATARANGE_AUDIO` data ranges:

* **Supported Sample Rates**: `44,100 Hz`, `48,000 Hz`, `88,200 Hz`, `96,000 Hz`, `192,000 Hz`.
* **Channel Configurations**: `Mono (1.0)`, `Stereo (2.0)`, `Surround 5.1 (6.0)`, `Surround 7.1 (8.0)`.
* **Sample Formats**: `16-bit PCM`, `24-bit PCM`, `32-bit PCM`, `32-bit Float PCM`.
* **Default Operating Mode**: `48,000 Hz`, 24-bit Float PCM in WASAPI Shared Mode.

---

## 3. Crash Surface Elimination & Ring 0 Safety

* **Micro-Kernel Discipline**: The kernel driver (`MoonshineAudio.sys`) is strictly restricted to DMA buffer allocation, position reporting, and raw frame transfers.
* **Ring 3 Processing**: All DSP algorithms, Opus audio compression, noise gating, jitter buffering, and network serialization execute entirely in user mode (Ring 3).
* **Pointer Probing**: All user/kernel buffer interactions enforce `ProbeForRead` and `ProbeForWrite` validation wrapped in structured exception handling (`__try / __except`).

---

## 4. Microsoft Attestation Signing & WHQL Workflow

Distributing the driver on 64-bit Windows without requiring Test Signing Mode (`bcdedit /set testsigning on`) or disabling Secure Boot:

1. **Hardware Dev Center Account**: Register legal entity on Microsoft Partner Center.
2. **EV Code Signing Certificate**: Acquire an Extended Validation (EV) certificate or utilize Azure Trusted Signing.
3. **Driver Packaging**:
   ```cmd
   makecab.exe /f MoonshineAudio.ddf
   signtool.exe sign /fd sha256 /a /tr http://timestamp.digicert.com /td sha256 MoonshineAudio.cab
   ```
4. **Attestation Signing**: Submit `MoonshineAudio.cab` to the Microsoft Hardware Developer Dashboard. Microsoft signs the catalog (`MoonshineAudio.cat`) with the official Windows Hardware Compatibility CA.
5. **Silent Production Deployment**:
   ```cmd
   pnputil.exe /add-driver MoonshineAudio.inf /install
   ```

---

## 5. Managed Service Integration Example

```csharp
using Moonshine.Host.Audio;

// Initialize Virtual Audio Driver Service
using var driverService = new VirtualAudioDriverService();

if (driverService.IsDriverInstalled())
{
    driverService.TryGetStatus(out var status);
    driverService.TryGetEndpointNames(out string renderName, out string captureName);
    
    // Register high-priority Pro Audio MMCSS scheduling
    driverService.TryEnableMmcss(out IntPtr mmcssTask);
}
```
