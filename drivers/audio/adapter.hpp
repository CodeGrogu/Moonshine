#ifndef MOONSHINE_ADAPTER_HPP
#define MOONSHINE_ADAPTER_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#include <stdunk.h>
#else
#include <cstdint>
#endif
#include "mintopo.hpp"
#include "minwave.hpp"

#ifdef _KERNEL_MODE

// ============================================================================
// IAdapterCommon: Private interface for shared adapter context
// ============================================================================

// {A5F4E3D2-1B2C-3D4E-5F6A-7B8C9D0E1F2A}
DEFINE_GUID(IID_IAdapterCommon,
    0xa5f4e3d2, 0x1b2c, 0x3d4e, 0x5f, 0x6a, 0x7b, 0x8c, 0x9d, 0x0e, 0x1f, 0x2a);

/// @brief Private adapter common interface for the Moonshine virtual audio device.
///
/// Provides shared adapter context, power state tracking, and mixer topology
/// access across WaveRT and Topology miniport objects.
DECLARE_INTERFACE_(IAdapterCommon, IUnknown)
{
    STDMETHOD_(NTSTATUS, Init)(THIS_
        PDEVICE_OBJECT DeviceObject
    ) PURE;

    STDMETHOD_(PDEVICE_OBJECT, GetDeviceObject)(THIS) PURE;

    STDMETHOD_(DEVICE_POWER_STATE, GetPowerState)(THIS) PURE;
};

/// @brief Shared adapter context implementing IAdapterCommon and IAdapterPowerManagement.
///
/// This class is the central coordination point for the Moonshine virtual audio
/// device. It owns the power state and provides shared context that WaveRT and
/// Topology miniport objects can query.
///
/// Power management follows PortCls conventions: PortCls handles all power IRPs
/// and calls into the adapter's IAdapterPowerManagement interface for state
/// transitions.
class CAdapterCommon :
    public IAdapterCommon,
    public IAdapterPowerManagement,
    public CUnknown
{
public:
    DECLARE_STD_UNKNOWN();
    DEFINE_STD_CONSTRUCTOR(CAdapterCommon);
    ~CAdapterCommon();

    // IAdapterCommon
    STDMETHODIMP_(NTSTATUS) Init(PDEVICE_OBJECT DeviceObject) override;
    STDMETHODIMP_(PDEVICE_OBJECT) GetDeviceObject() override;
    STDMETHODIMP_(DEVICE_POWER_STATE) GetPowerState() override;

    // IAdapterPowerManagement
    STDMETHODIMP_(void) PowerChangeState(POWER_STATE NewState) override;
    STDMETHODIMP_(NTSTATUS) QueryPowerChangeState(POWER_STATE NewState) override;
    STDMETHODIMP_(NTSTATUS) QueryDeviceCapabilities(
        PDEVICE_CAPABILITIES PowerDeviceCaps
    ) override;

    // Factory method for PortCls
    static NTSTATUS CreateInstance(
        PUNKNOWN* OutUnknown,
        PUNKNOWN OuterUnknown,
        POOL_TYPE PoolType
    );

private:
    PDEVICE_OBJECT m_deviceObject;
    DEVICE_POWER_STATE m_powerState;
};

/// @brief Kernel-mode WaveRT miniport with PortCls COM interfaces.
///
/// Implements IMiniportWaveRT for stream creation and device description,
/// and delegates per-stream operations to CMiniportWaveRTStreamMoonshine.
class CMiniportWaveRTMoonshine :
    public IMiniportWaveRT,
    public CUnknown
{
public:
    DECLARE_STD_UNKNOWN();
    DEFINE_STD_CONSTRUCTOR(CMiniportWaveRTMoonshine);
    ~CMiniportWaveRTMoonshine();

    // IMiniportWaveRT
    STDMETHODIMP_(NTSTATUS) Init(
        PUNKNOWN UnknownAdapter,
        PRESOURCELIST ResourceList,
        PPORTWAVERT Port
    ) override;

    STDMETHODIMP_(NTSTATUS) NewStream(
        PMINIPORTWAVERTSTREAM* OutStream,
        PPORTWAVERTSTREAM PortStream,
        ULONG Pin,
        BOOLEAN Capture,
        PKSDATAFORMAT DataFormat
    ) override;

    STDMETHODIMP_(NTSTATUS) GetDeviceDescription(
        PDEVICE_DESCRIPTION OutDeviceDescription
    ) override;

    static NTSTATUS CreateInstance(
        PUNKNOWN* OutUnknown,
        PUNKNOWN OuterUnknown,
        POOL_TYPE PoolType
    );

private:
    PPORTWAVERT m_port;
    IAdapterCommon* m_adapterCommon;
    BOOL m_initialized;
};

/// @brief Kernel-mode Topology miniport with PortCls COM interfaces.
class CMiniportTopologyMoonshine :
    public IMiniportTopology,
    public CUnknown
{
public:
    DECLARE_STD_UNKNOWN();
    DEFINE_STD_CONSTRUCTOR(CMiniportTopologyMoonshine);
    ~CMiniportTopologyMoonshine();

    // IMiniportTopology
    STDMETHODIMP_(NTSTATUS) Init(
        PUNKNOWN UnknownAdapter,
        PRESOURCELIST ResourceList,
        PPORTTOPOLOGY Port
    ) override;

    STDMETHODIMP_(NTSTATUS) GetDescription(
        PPCFILTER_DESCRIPTOR* OutFilterDescriptor
    ) override;

    STDMETHODIMP_(NTSTATUS) DataRangeIntersection(
        ULONG PinId,
        PKSDATARANGE ClientDataRange,
        PKSDATARANGE MyDataRange,
        ULONG OutputBufferLength,
        PVOID ResultantFormat,
        PULONG ResultantFormatLength
    ) override;

    static NTSTATUS CreateInstance(
        PUNKNOWN* OutUnknown,
        PUNKNOWN OuterUnknown,
        POOL_TYPE PoolType
    );

private:
    PPORTTOPOLOGY m_port;
    IAdapterCommon* m_adapterCommon;
    BOOL m_initialized;
};

#else // !_KERNEL_MODE

// ============================================================================
// User-mode adapter class for test harness compilation
// ============================================================================

class CMoonshineAudioAdapter {
public:
    CMoonshineAudioAdapter();
    ~CMoonshineAudioAdapter();

    int Start();
    int Stop();
    bool IsRunning() const { return m_isRunning; }

    CMiniportWaveRT* GetWaveRT() { return &m_waveRt; }
    CMiniportTopology* GetTopology() { return &m_topology; }

private:
    bool m_isRunning;
    CMiniportWaveRT m_waveRt;
    CMiniportTopology m_topology;
};

#endif // _KERNEL_MODE

#endif // MOONSHINE_ADAPTER_HPP
