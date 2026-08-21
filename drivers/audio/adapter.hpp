#ifndef MOONSHINE_ADAPTER_HPP
#define MOONSHINE_ADAPTER_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#else
#include <cstdint>
#endif
#include "mintopo.hpp"
#include "minwave.hpp"

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

#endif // MOONSHINE_ADAPTER_HPP
