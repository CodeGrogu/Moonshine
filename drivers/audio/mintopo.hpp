#ifndef MOONSHINE_MINTOPO_HPP
#define MOONSHINE_MINTOPO_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#include <ksdebug.h>
#include <ks.h>
#include <ksmedia.h>
#else
#include <cstdint>
#endif
#include "shared_audio_buffer.h"

#ifdef _KERNEL_MODE

// ============================================================================
// Topology Node Indices
// ============================================================================

/// Render path: [Host Pin 0] -> [Volume 0] -> [Mute 0] -> [DAC 0] -> [Bridge Pin 1]
/// Capture path: [Bridge Pin 2] -> [ADC 0] -> [Volume 1] -> [Mute 1] -> [Host Pin 3]

/// Node indices for the topology filter.
enum MoonshineTopologyNode {
    MOONSHINE_TOPO_NODE_RENDER_VOLUME = 0,
    MOONSHINE_TOPO_NODE_RENDER_MUTE   = 1,
    MOONSHINE_TOPO_NODE_RENDER_DAC    = 2,
    MOONSHINE_TOPO_NODE_CAPTURE_ADC   = 3,
    MOONSHINE_TOPO_NODE_CAPTURE_VOLUME = 4,
    MOONSHINE_TOPO_NODE_CAPTURE_MUTE  = 5,
    MOONSHINE_TOPO_NODE_COUNT         = 6
};

/// Pin indices for the topology filter.
enum MoonshineTopologyPin {
    MOONSHINE_TOPO_PIN_RENDER_HOST     = 0,   // Host render output (from wave)
    MOONSHINE_TOPO_PIN_RENDER_BRIDGE   = 1,   // Bridge to speaker endpoint
    MOONSHINE_TOPO_PIN_CAPTURE_BRIDGE  = 2,   // Bridge from microphone endpoint
    MOONSHINE_TOPO_PIN_CAPTURE_HOST    = 3,   // Host capture input (to wave)
    MOONSHINE_TOPO_PIN_COUNT           = 4
};

// Forward declaration (kernel-mode topology miniport is defined in adapter.hpp)
// CMiniportTopologyMoonshine is declared in adapter.hpp

#else // !_KERNEL_MODE

// ============================================================================
// User-mode topology class for test harness compilation
// ============================================================================

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

#endif // _KERNEL_MODE

#endif // MOONSHINE_MINTOPO_HPP
