/// @file driver_entry.cpp
/// @brief PortCls adapter driver entry points for the Moonshine Virtual Audio Device.
///
/// This file provides the WDM DriverEntry, AddDevice, and StartDevice routines
/// that initialise the Moonshine virtual audio adapter and register WaveRT and
/// Topology subdevices with the PortCls framework.
///
/// In kernel mode (_KERNEL_MODE defined), these routines call into PortCls system
/// functions. In user mode, stubs are provided so the driver source compiles as
/// part of the native test harness without requiring the WDK.

#include "adapter.hpp"
#include "minwave.hpp"
#include "mintopo.hpp"
#include "shared_audio_buffer.h"

#ifdef _KERNEL_MODE
#include <portcls.h>

// Forward declarations for PortCls callbacks
extern "C" DRIVER_ADD_DEVICE MoonshineAudioAddDevice;
extern "C" NTSTATUS MoonshineAudioStartDevice(
    PDEVICE_OBJECT DeviceObject,
    PIRP Irp,
    PRESOURCELIST ResourceList
);

/// Subdevice reference string names. These must match the INF AddInterface
/// section names exactly for the audio engine to bind endpoints correctly.
static constexpr PCWSTR kWaveSubdeviceName = L"Wave";
static constexpr PCWSTR kTopologySubdeviceName = L"Topology";

/// Maximum number of subdevices registered by this adapter (Wave + Topology).
static constexpr ULONG kMaxSubdeviceCount = 2;

// ============================================================================
// DriverEntry
// ============================================================================

/// @brief WDM driver entry point.
///
/// Called by the operating system when the driver is first loaded. Delegates
/// all adapter registration to PcInitializeAdapterDriver, which installs the
/// AddDevice callback and standard PortCls IRP dispatch routines.
///
/// @param DriverObject  Pointer to the DRIVER_OBJECT created by the I/O manager.
/// @param RegistryPath  Unicode string path to the driver's registry parameters.
/// @return STATUS_SUCCESS on successful initialisation; an NTSTATUS error otherwise.
extern "C" NTSTATUS DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath
)
{
    return PcInitializeAdapterDriver(
        DriverObject,
        RegistryPath,
        MoonshineAudioAddDevice
    );
}

// ============================================================================
// AddDevice
// ============================================================================

/// @brief PnP AddDevice callback.
///
/// Called by the PnP manager for each instance of the Moonshine audio device.
/// Creates the Functional Device Object (FDO) and associates it with the
/// Physical Device Object (PDO) via PcAddAdapterDevice. The StartDevice
/// callback is registered to handle IRP_MN_START_DEVICE, at which point
/// subdevices are created and bound.
///
/// @param DriverObject         The driver object.
/// @param PhysicalDeviceObject The PDO representing the hardware instance.
/// @return STATUS_SUCCESS on success; an NTSTATUS error otherwise.
extern "C" NTSTATUS MoonshineAudioAddDevice(
    PDRIVER_OBJECT DriverObject,
    PDEVICE_OBJECT PhysicalDeviceObject
)
{
    return PcAddAdapterDevice(
        DriverObject,
        PhysicalDeviceObject,
        MoonshineAudioStartDevice,
        kMaxSubdeviceCount,
        0   // DeviceExtensionSize: PortCls manages the extension
    );
}

// ============================================================================
// StartDevice
// ============================================================================

/// @brief Creates and registers WaveRT and Topology subdevices.
///
/// Invoked by PortCls when IRP_MN_START_DEVICE arrives. This routine:
/// 1. Creates the shared CAdapterCommon instance.
/// 2. Creates WaveRT port and miniport objects, binds them, and registers
///    the "Wave" subdevice.
/// 3. Creates Topology port and miniport objects, binds them, and registers
///    the "Topology" subdevice.
/// 4. Registers adapter power management via PcRegisterAdapterPowerManagement.
///
/// @param DeviceObject  The FDO created in AddDevice.
/// @param Irp           The IRP_MN_START_DEVICE IRP.
/// @param ResourceList  Hardware resources assigned by the PnP manager.
/// @return STATUS_SUCCESS if all subdevices are created; an error NTSTATUS otherwise.
extern "C" NTSTATUS MoonshineAudioStartDevice(
    PDEVICE_OBJECT DeviceObject,
    PIRP Irp,
    PRESOURCELIST ResourceList
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PUNKNOWN pUnknownAdapter = nullptr;
    PUNKNOWN pUnknownWavePort = nullptr;
    PUNKNOWN pUnknownWaveMiniport = nullptr;
    PUNKNOWN pUnknownTopoPort = nullptr;
    PUNKNOWN pUnknownTopoMiniport = nullptr;

    // ---------------------------------------------------------------
    // 1. Create adapter common object (shared context, power management)
    // ---------------------------------------------------------------
    status = CAdapterCommon::CreateInstance(
        &pUnknownAdapter,
        nullptr,    // OuterUnknown
        NonPagedPoolNx
    );
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    {
        IAdapterCommon* pAdapterCommon = nullptr;
        status = pUnknownAdapter->QueryInterface(
            __uuidof(IAdapterCommon),
            reinterpret_cast<PVOID*>(&pAdapterCommon)
        );
        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }

        status = pAdapterCommon->Init(DeviceObject);
        pAdapterCommon->Release();

        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }
    }

    // Register adapter power management
    {
        IUnknown* pUnkPower = nullptr;
        status = pUnknownAdapter->QueryInterface(
            IID_IAdapterPowerManagement,
            reinterpret_cast<PVOID*>(&pUnkPower)
        );
        if (NT_SUCCESS(status) && pUnkPower)
        {
            PcRegisterAdapterPowerManagement(pUnkPower, DeviceObject);
            pUnkPower->Release();
        }
    }

    // ---------------------------------------------------------------
    // 2. Create and register WaveRT subdevice
    // ---------------------------------------------------------------
    status = PcNewPort(&pUnknownWavePort, CLSID_PortWaveRT);
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    status = CMiniportWaveRTMoonshine::CreateInstance(
        &pUnknownWaveMiniport,
        nullptr,    // OuterUnknown
        NonPagedPoolNx
    );
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    {
        IPortWaveRT* pWavePort = nullptr;
        status = pUnknownWavePort->QueryInterface(
            IID_IPortWaveRT,
            reinterpret_cast<PVOID*>(&pWavePort)
        );
        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }

        status = pWavePort->Init(
            DeviceObject,
            Irp,
            reinterpret_cast<PUNKNOWN>(pUnknownWaveMiniport),
            pUnknownAdapter,
            ResourceList
        );
        pWavePort->Release();

        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }
    }

    status = PcRegisterSubdevice(
        DeviceObject,
        kWaveSubdeviceName,
        pUnknownWavePort
    );
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    // ---------------------------------------------------------------
    // 3. Create and register Topology subdevice
    // ---------------------------------------------------------------
    status = PcNewPort(&pUnknownTopoPort, CLSID_PortTopology);
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    status = CMiniportTopologyMoonshine::CreateInstance(
        &pUnknownTopoMiniport,
        nullptr,    // OuterUnknown
        NonPagedPoolNx
    );
    if (!NT_SUCCESS(status))
    {
        goto cleanup;
    }

    {
        IPortTopology* pTopoPort = nullptr;
        status = pUnknownTopoPort->QueryInterface(
            IID_IPortTopology,
            reinterpret_cast<PVOID*>(&pTopoPort)
        );
        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }

        status = pTopoPort->Init(
            DeviceObject,
            Irp,
            reinterpret_cast<PUNKNOWN>(pUnknownTopoMiniport),
            pUnknownAdapter,
            ResourceList
        );
        pTopoPort->Release();

        if (!NT_SUCCESS(status))
        {
            goto cleanup;
        }
    }

    status = PcRegisterSubdevice(
        DeviceObject,
        kTopologySubdeviceName,
        pUnknownTopoPort
    );

cleanup:
    if (pUnknownAdapter)        pUnknownAdapter->Release();
    if (pUnknownWavePort)       pUnknownWavePort->Release();
    if (pUnknownWaveMiniport)   pUnknownWaveMiniport->Release();
    if (pUnknownTopoPort)       pUnknownTopoPort->Release();
    if (pUnknownTopoMiniport)   pUnknownTopoMiniport->Release();

    return status;
}

#else // !_KERNEL_MODE

// ============================================================================
// User-mode stubs for test compilability
// ============================================================================

// STUB: User-mode DriverEntry placeholder for CTest harness compilation without WDK headers.
// The actual DriverEntry is kernel-mode only and calls PcInitializeAdapterDriver.

#endif // _KERNEL_MODE
