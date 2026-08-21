#include "mintopo.hpp"

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
