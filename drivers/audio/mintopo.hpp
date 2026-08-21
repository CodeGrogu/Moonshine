#ifndef MOONSHINE_MINTOPO_HPP
#define MOONSHINE_MINTOPO_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#include <ksdebug.h>
#else
#include <cstdint>
#endif
#include "shared_audio_buffer.h"

class CMiniportTopology {
public:
    CMiniportTopology();
    ~CMiniportTopology();

    int Init();
    int GetDescription(void* outDescription);
    int PropertyHandler(void* propertyRequest);

    static uint32_t GetRenderPinCount();
    static uint32_t GetCapturePinCount();

private:
    bool m_initialized;
};

#endif // MOONSHINE_MINTOPO_HPP
