#include "adapter.hpp"

#ifdef _KERNEL_MODE

// ============================================================================
// CAdapterCommon: Shared adapter context and power management
// ============================================================================

CAdapterCommon::CAdapterCommon(PUNKNOWN OuterUnknown)
    : CUnknown(OuterUnknown)
    , m_deviceObject(nullptr)
    , m_powerState(PowerDeviceD0)
{
}

CAdapterCommon::~CAdapterCommon()
{
    m_deviceObject = nullptr;
}

NTSTATUS CAdapterCommon::CreateInstance(
    PUNKNOWN* OutUnknown,
    PUNKNOWN OuterUnknown,
    POOL_TYPE PoolType
)
{
    if (!OutUnknown)
    {
        return STATUS_INVALID_PARAMETER;
    }

    auto* instance = new(PoolType, 'adpC') CAdapterCommon(OuterUnknown);
    if (!instance)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    instance->AddRef();
    *OutUnknown = reinterpret_cast<PUNKNOWN>(instance);
    return STATUS_SUCCESS;
}

NTSTATUS CAdapterCommon::Init(PDEVICE_OBJECT DeviceObject)
{
    m_deviceObject = DeviceObject;
    m_powerState = PowerDeviceD0;
    return STATUS_SUCCESS;
}

PDEVICE_OBJECT CAdapterCommon::GetDeviceObject()
{
    return m_deviceObject;
}

DEVICE_POWER_STATE CAdapterCommon::GetPowerState()
{
    return m_powerState;
}

/// @brief Handles power state transitions.
///
/// For a virtual audio device there is no physical hardware to configure,
/// but the adapter tracks the current power state so miniports can query it.
/// On power-down transitions (D1, D2, D3), any active streams should be
/// paused. On power-up (D0), streams may resume.
void CAdapterCommon::PowerChangeState(POWER_STATE NewState)
{
    m_powerState = NewState.DeviceState;
}

/// @brief Approves proposed power state changes.
///
/// A virtual device always accepts power transitions since there is no
/// hardware that might prevent state changes.
NTSTATUS CAdapterCommon::QueryPowerChangeState(POWER_STATE NewState)
{
    UNREFERENCED_PARAMETER(NewState);
    return STATUS_SUCCESS;
}

/// @brief Reports device power capabilities.
///
/// Virtual devices support all standard power states. The system idle
/// timeout is left at the PortCls default.
NTSTATUS CAdapterCommon::QueryDeviceCapabilities(
    PDEVICE_CAPABILITIES PowerDeviceCaps
)
{
    UNREFERENCED_PARAMETER(PowerDeviceCaps);
    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CAdapterCommon::QueryInterface(
    REFIID Interface,
    PVOID* Object
)
{
    if (!Object)
    {
        return STATUS_INVALID_PARAMETER;
    }

    *Object = nullptr;

    if (IsEqualGUIDAligned(Interface, IID_IUnknown))
    {
        *Object = static_cast<IUnknown*>(static_cast<IAdapterCommon*>(this));
    }
    else if (IsEqualGUIDAligned(Interface, IID_IAdapterCommon))
    {
        *Object = static_cast<IAdapterCommon*>(this);
    }
    else if (IsEqualGUIDAligned(Interface, IID_IAdapterPowerManagement))
    {
        *Object = static_cast<IAdapterPowerManagement*>(this);
    }
    else
    {
        return STATUS_NOINTERFACE;
    }

    AddRef();
    return STATUS_SUCCESS;
}

// ============================================================================
// CMiniportWaveRTMoonshine: PortCls WaveRT miniport implementation
// ============================================================================

CMiniportWaveRTMoonshine::CMiniportWaveRTMoonshine(PUNKNOWN OuterUnknown)
    : CUnknown(OuterUnknown)
    , m_port(nullptr)
    , m_adapterCommon(nullptr)
    , m_initialized(FALSE)
{
}

CMiniportWaveRTMoonshine::~CMiniportWaveRTMoonshine()
{
    if (m_adapterCommon)
    {
        m_adapterCommon->Release();
        m_adapterCommon = nullptr;
    }
    if (m_port)
    {
        m_port->Release();
        m_port = nullptr;
    }
    m_initialized = FALSE;
}

NTSTATUS CMiniportWaveRTMoonshine::CreateInstance(
    PUNKNOWN* OutUnknown,
    PUNKNOWN OuterUnknown,
    POOL_TYPE PoolType
)
{
    if (!OutUnknown)
    {
        return STATUS_INVALID_PARAMETER;
    }

    auto* instance = new(PoolType, 'wrtC') CMiniportWaveRTMoonshine(OuterUnknown);
    if (!instance)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    instance->AddRef();
    *OutUnknown = reinterpret_cast<PUNKNOWN>(instance);
    return STATUS_SUCCESS;
}

NTSTATUS CMiniportWaveRTMoonshine::Init(
    PUNKNOWN UnknownAdapter,
    PRESOURCELIST ResourceList,
    PPORTWAVERT Port
)
{
    UNREFERENCED_PARAMETER(ResourceList);

    if (!UnknownAdapter || !Port)
    {
        return STATUS_INVALID_PARAMETER;
    }

    NTSTATUS status = UnknownAdapter->QueryInterface(
        IID_IAdapterCommon,
        reinterpret_cast<PVOID*>(&m_adapterCommon)
    );
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    m_port = Port;
    m_port->AddRef();
    m_initialized = TRUE;

    return STATUS_SUCCESS;
}

NTSTATUS CMiniportWaveRTMoonshine::NewStream(
    PMINIPORTWAVERTSTREAM* OutStream,
    PPORTWAVERTSTREAM PortStream,
    ULONG Pin,
    BOOLEAN Capture,
    PKSDATAFORMAT DataFormat
)
{
    // STUB: Stream creation delegates to CMiniportWaveRTStreamMoonshine (requires full KS pin descriptor implementation)
    UNREFERENCED_PARAMETER(OutStream);
    UNREFERENCED_PARAMETER(PortStream);
    UNREFERENCED_PARAMETER(Pin);
    UNREFERENCED_PARAMETER(Capture);
    UNREFERENCED_PARAMETER(DataFormat);
    return STATUS_NOT_IMPLEMENTED;
}

NTSTATUS CMiniportWaveRTMoonshine::GetDeviceDescription(
    PDEVICE_DESCRIPTION OutDeviceDescription
)
{
    if (!OutDeviceDescription)
    {
        return STATUS_INVALID_PARAMETER;
    }

    RtlZeroMemory(OutDeviceDescription, sizeof(*OutDeviceDescription));
    OutDeviceDescription->ScatterGather = TRUE;
    OutDeviceDescription->Dma32BitAddresses = TRUE;
    OutDeviceDescription->MaximumLength = PAGE_SIZE * 16;

    return STATUS_SUCCESS;
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTMoonshine::QueryInterface(
    REFIID Interface,
    PVOID* Object
)
{
    if (!Object)
    {
        return STATUS_INVALID_PARAMETER;
    }

    *Object = nullptr;

    if (IsEqualGUIDAligned(Interface, IID_IUnknown))
    {
        *Object = static_cast<IUnknown*>(static_cast<IMiniportWaveRT*>(this));
    }
    else if (IsEqualGUIDAligned(Interface, IID_IMiniportWaveRT))
    {
        *Object = static_cast<IMiniportWaveRT*>(this);
    }
    else
    {
        return STATUS_NOINTERFACE;
    }

    AddRef();
    return STATUS_SUCCESS;
}

// ============================================================================
// CMiniportTopologyMoonshine: PortCls Topology miniport implementation
// ============================================================================

CMiniportTopologyMoonshine::CMiniportTopologyMoonshine(PUNKNOWN OuterUnknown)
    : CUnknown(OuterUnknown)
    , m_port(nullptr)
    , m_adapterCommon(nullptr)
    , m_initialized(FALSE)
{
}

CMiniportTopologyMoonshine::~CMiniportTopologyMoonshine()
{
    if (m_adapterCommon)
    {
        m_adapterCommon->Release();
        m_adapterCommon = nullptr;
    }
    if (m_port)
    {
        m_port->Release();
        m_port = nullptr;
    }
    m_initialized = FALSE;
}

NTSTATUS CMiniportTopologyMoonshine::CreateInstance(
    PUNKNOWN* OutUnknown,
    PUNKNOWN OuterUnknown,
    POOL_TYPE PoolType
)
{
    if (!OutUnknown)
    {
        return STATUS_INVALID_PARAMETER;
    }

    auto* instance = new(PoolType, 'topC') CMiniportTopologyMoonshine(OuterUnknown);
    if (!instance)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    instance->AddRef();
    *OutUnknown = reinterpret_cast<PUNKNOWN>(instance);
    return STATUS_SUCCESS;
}

NTSTATUS CMiniportTopologyMoonshine::Init(
    PUNKNOWN UnknownAdapter,
    PRESOURCELIST ResourceList,
    PPORTTOPOLOGY Port
)
{
    UNREFERENCED_PARAMETER(ResourceList);

    if (!UnknownAdapter || !Port)
    {
        return STATUS_INVALID_PARAMETER;
    }

    NTSTATUS status = UnknownAdapter->QueryInterface(
        IID_IAdapterCommon,
        reinterpret_cast<PVOID*>(&m_adapterCommon)
    );
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    m_port = Port;
    m_port->AddRef();
    m_initialized = TRUE;

    return STATUS_SUCCESS;
}

NTSTATUS CMiniportTopologyMoonshine::GetDescription(
    PPCFILTER_DESCRIPTOR* OutFilterDescriptor
)
{
    // STUB: Topology filter descriptor with volume and mute nodes (requires PCCONNECTION_DESCRIPTOR and KSNODETYPE definitions)
    if (!OutFilterDescriptor)
    {
        return STATUS_INVALID_PARAMETER;
    }

    *OutFilterDescriptor = nullptr;
    return STATUS_NOT_IMPLEMENTED;
}

NTSTATUS CMiniportTopologyMoonshine::DataRangeIntersection(
    ULONG PinId,
    PKSDATARANGE ClientDataRange,
    PKSDATARANGE MyDataRange,
    ULONG OutputBufferLength,
    PVOID ResultantFormat,
    PULONG ResultantFormatLength
)
{
    UNREFERENCED_PARAMETER(PinId);
    UNREFERENCED_PARAMETER(ClientDataRange);
    UNREFERENCED_PARAMETER(MyDataRange);
    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(ResultantFormat);
    UNREFERENCED_PARAMETER(ResultantFormatLength);

    // Returning STATUS_NOT_IMPLEMENTED causes PortCls to use its default
    // data range intersection handler, which is appropriate for standard
    // audio formats.
    return STATUS_NOT_IMPLEMENTED;
}

STDMETHODIMP_(NTSTATUS) CMiniportTopologyMoonshine::QueryInterface(
    REFIID Interface,
    PVOID* Object
)
{
    if (!Object)
    {
        return STATUS_INVALID_PARAMETER;
    }

    *Object = nullptr;

    if (IsEqualGUIDAligned(Interface, IID_IUnknown))
    {
        *Object = static_cast<IUnknown*>(static_cast<IMiniportTopology*>(this));
    }
    else if (IsEqualGUIDAligned(Interface, IID_IMiniportTopology))
    {
        *Object = static_cast<IMiniportTopology*>(this);
    }
    else
    {
        return STATUS_NOINTERFACE;
    }

    AddRef();
    return STATUS_SUCCESS;
}

#else // !_KERNEL_MODE

// ============================================================================
// User-mode adapter for test harness
// ============================================================================

CMoonshineAudioAdapter::CMoonshineAudioAdapter()
    : m_isRunning(false)
{
}

CMoonshineAudioAdapter::~CMoonshineAudioAdapter()
{
    Stop();
}

int CMoonshineAudioAdapter::Start()
{
    if (m_isRunning) {
        return 0;
    }

    if (m_topology.Init() != 0) {
        return -1;
    }

    if (m_waveRt.Init() != 0) {
        return -2;
    }

    m_isRunning = true;
    return 0;
}

int CMoonshineAudioAdapter::Stop()
{
    m_isRunning = false;
    return 0;
}

#endif // _KERNEL_MODE
