#include "minwave.hpp"
#include <cstdlib>
#include <cstring>

CMiniportWaveRTStream::CMiniportWaveRTStream(
    MoonshineAudioEndpointType endpointType,
    uint32_t sampleRate,
    uint32_t channels,
    MoonshineAudioSampleFormat format)
    : m_endpointType(endpointType)
    , m_sampleRate(sampleRate)
    , m_channels(channels)
    , m_format(format)
    , m_dmaBuffer(nullptr)
    , m_bufferSize(0)
    , m_position(0)
    , m_isActive(false)
{
}

CMiniportWaveRTStream::~CMiniportWaveRTStream()
{
    FreeAudioBuffer();
}

int CMiniportWaveRTStream::AllocateAudioBuffer(uint32_t requestedSize, void** outBuffer, uint32_t* outActualSize)
{
    if (!outBuffer || !outActualSize || requestedSize == 0) {
        return -1;
    }

    FreeAudioBuffer();

    // Align buffer size to 4KB page boundary
    uint32_t alignedSize = (requestedSize + 4095) & ~4095;
    m_dmaBuffer = static_cast<uint8_t*>(std::calloc(1, alignedSize));
    if (!m_dmaBuffer) {
        return -1;
    }

    m_bufferSize = alignedSize;
    m_position = 0;
    *outBuffer = m_dmaBuffer;
    *outActualSize = alignedSize;
    return 0;
}

void CMiniportWaveRTStream::FreeAudioBuffer()
{
    if (m_dmaBuffer) {
        std::free(m_dmaBuffer);
        m_dmaBuffer = nullptr;
    }
    m_bufferSize = 0;
    m_position = 0;
}

int CMiniportWaveRTStream::GetPositions(uint32_t* outPlayPosition, uint32_t* outWritePosition)
{
    if (!outPlayPosition || !outWritePosition) {
        return -1;
    }

    *outPlayPosition = m_position;
    *outWritePosition = m_position;
    return 0;
}

int CMiniportWaveRTStream::SetState(uint32_t state)
{
    m_isActive = (state != 0);
    return 0;
}

int CMiniportWaveRTStream::SetFormat(uint32_t sampleRate, uint32_t channels, MoonshineAudioSampleFormat format)
{
    if (!CMiniportWaveRT::IsFormatSupported(sampleRate, channels, format)) {
        return -1;
    }

    m_sampleRate = sampleRate;
    m_channels = channels;
    m_format = format;
    return 0;
}

CMiniportWaveRT::CMiniportWaveRT()
    : m_initialized(false)
{
}

CMiniportWaveRT::~CMiniportWaveRT()
{
    m_initialized = false;
}

int CMiniportWaveRT::Init()
{
    m_initialized = true;
    return 0;
}

int CMiniportWaveRT::NewStream(
    MoonshineAudioEndpointType endpointType,
    uint32_t sampleRate,
    uint32_t channels,
    MoonshineAudioSampleFormat format,
    CMiniportWaveRTStream** outStream)
{
    if (!outStream || !IsFormatSupported(sampleRate, channels, format)) {
        return -1;
    }

    *outStream = new CMiniportWaveRTStream(endpointType, sampleRate, channels, format);
    return 0;
}

bool CMiniportWaveRT::IsFormatSupported(uint32_t sampleRate, uint32_t channels, MoonshineAudioSampleFormat format)
{
    // Validate sample rate (44.1kHz to 192kHz)
    if (sampleRate != 44100 &&
        sampleRate != 48000 &&
        sampleRate != 88200 &&
        sampleRate != 96000 &&
        sampleRate != 192000) {
        return false;
    }

    // Validate channels (Mono, Stereo, 5.1, 7.1)
    if (channels != 1 && channels != 2 && channels != 6 && channels != 8) {
        return false;
    }

    // Validate format
    if (format != MOONSHINE_FORMAT_PCM_16 &&
        format != MOONSHINE_FORMAT_PCM_24 &&
        format != MOONSHINE_FORMAT_PCM_32 &&
        format != MOONSHINE_FORMAT_FLOAT_32) {
        return false;
    }

    return true;
}
