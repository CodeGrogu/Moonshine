#include "adapter.hpp"

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
