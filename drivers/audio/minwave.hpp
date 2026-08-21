#ifndef MOONSHINE_MINWAVE_HPP
#define MOONSHINE_MINWAVE_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#include <ksdebug.h>
#else
#include <cstdint>
#include <cstddef>
#endif
#include "shared_audio_buffer.h"

class CMiniportWaveRTStream {
public:
    CMiniportWaveRTStream(MoonshineAudioEndpointType endpointType, uint32_t sampleRate, uint32_t channels, MoonshineAudioSampleFormat format);
    ~CMiniportWaveRTStream();

    int AllocateAudioBuffer(uint32_t requestedSize, void** outBuffer, uint32_t* outActualSize);
    void FreeAudioBuffer();

    int GetPositions(uint32_t* outPlayPosition, uint32_t* outWritePosition);
    int SetState(uint32_t state);
    int SetFormat(uint32_t sampleRate, uint32_t channels, MoonshineAudioSampleFormat format);

    MoonshineAudioEndpointType GetEndpointType() const { return m_endpointType; }
    uint32_t GetSampleRate() const { return m_sampleRate; }
    uint32_t GetChannels() const { return m_channels; }
    MoonshineAudioSampleFormat GetFormat() const { return m_format; }
    uint32_t GetBufferSize() const { return m_bufferSize; }
    bool IsActive() const { return m_isActive; }

private:
    MoonshineAudioEndpointType m_endpointType;
    uint32_t m_sampleRate;
    uint32_t m_channels;
    MoonshineAudioSampleFormat m_format;
    uint8_t* m_dmaBuffer;
    uint32_t m_bufferSize;
    uint32_t m_position;
    bool m_isActive;
};

class CMiniportWaveRT {
public:
    CMiniportWaveRT();
    ~CMiniportWaveRT();

    int Init();
    int NewStream(
        MoonshineAudioEndpointType endpointType,
        uint32_t sampleRate,
        uint32_t channels,
        MoonshineAudioSampleFormat format,
        CMiniportWaveRTStream** outStream
    );

    static bool IsFormatSupported(uint32_t sampleRate, uint32_t channels, MoonshineAudioSampleFormat format);

private:
    bool m_initialized;
};

#endif // MOONSHINE_MINWAVE_HPP
