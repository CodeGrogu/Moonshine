#include "mintopo.hpp"

#ifdef _KERNEL_MODE

// The kernel-mode topology miniport (CMiniportTopologyMoonshine) is
// implemented in adapter.cpp alongside the other PortCls COM objects.
// This file is intentionally minimal in kernel mode; the topology node
// descriptors and connection table are defined there.

#else // !_KERNEL_MODE

// ============================================================================
// User-mode topology for test harness
// ============================================================================

CMiniportTopology::CMiniportTopology()
    : m_initialized(false)
{
}

CMiniportTopology::~CMiniportTopology()
{
    m_initialized = false;
}

int CMiniportTopology::Init()
{
    m_initialized = true;
    return 0;
}

int CMiniportTopology::GetDescription(void* outDescription)
{
    if (!outDescription) {
        return -1;
    }
    return 0;
}

int CMiniportTopology::PropertyHandler(void* propertyRequest)
{
    if (!propertyRequest) {
        return -1;
    }
    return 0;
}

uint32_t CMiniportTopology::GetRenderPinCount()
{
    return 1;
}

uint32_t CMiniportTopology::GetCapturePinCount()
{
    return 1;
}

#endif // _KERNEL_MODE
