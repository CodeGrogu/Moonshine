#ifndef MOONSHINE_MINWAVE_HPP
#define MOONSHINE_MINWAVE_HPP

#ifdef _KERNEL_MODE
#include <portcls.h>
#include <ksdebug.h>
#include <ks.h>
#include <ksmedia.h>
#else
#include <cstdint>
#include <cstddef>
#endif
#include "shared_audio_buffer.h"

#ifdef _KERNEL_MODE

// ============================================================================
// KS Pin and Format Descriptors
// ============================================================================

/// Supported sample rates for the Moonshine virtual audio endpoints.
static constexpr ULONG kMoonshineSampleRates[] = {
    44100, 48000, 88200, 96000, 192000
};

/// Supported channel counts for the Moonshine virtual audio endpoints.
static constexpr ULONG kMoonshineChannelCounts[] = {
    1, 2, 6, 8
};

/// Number of render pins (host process pin + bridge pin).
static constexpr ULONG kRenderPinCount = 2;

/// Number of capture pins (host process pin + bridge pin).
static constexpr ULONG kCapturePinCount = 2;

/// @brief Per-stream object implementing IMiniportWaveRTStream.
///
/// Manages the cyclic audio buffer for a single render or capture stream.
/// For a virtual device, the buffer is allocated from non-paged pool rather
/// than DMA-mapped memory, since there is no physical hardware DMA engine.
class CMiniportWaveRTStreamMoonshine :
    public IMiniportWaveRTStream,
    public CUnknown
{
public:
    DECLARE_STD_UNKNOWN();
    DEFINE_STD_CONSTRUCTOR(CMiniportWaveRTStreamMoonshine);
    ~CMiniportWaveRTStreamMoonshine();

    // IMiniportWaveRTStream
    STDMETHODIMP_(NTSTATUS) AllocateAudioBuffer(
        ULONG RequestedSize,
        PMDL* AudioBufferMdl,
        ULONG* ActualSize,
        ULONG* OffsetFromFirstPage,
        MEMORY_CACHING_TYPE* CacheType
    ) override;

    STDMETHODIMP_(void) FreeAudioBuffer(
        PMDL AudioBufferMdl,
        ULONG BufferSize
    ) override;

    STDMETHODIMP_(void) GetHWLatency(
        KSRTAUDIO_HWLATENCY* Latency
    ) override;

    STDMETHODIMP_(NTSTATUS) GetPosition(
        KSAUDIO_POSITION* Position
    ) override;

    STDMETHODIMP_(NTSTATUS) GetClockRegister(
        PKSRTAUDIO_HWREGISTER Register
    ) override;

    STDMETHODIMP_(NTSTATUS) GetPositionRegister(
        PKSRTAUDIO_HWREGISTER Register
    ) override;

    STDMETHODIMP_(void) SetState(
        KSSTATE State
    ) override;

    // Initialisation
    NTSTATUS Init(
        BOOLEAN Capture,
        ULONG SampleRate,
        ULONG Channels,
        ULONG BitsPerSample,
        PPORTWAVERTSTREAM PortStream
    );

    static NTSTATUS CreateInstance(
        PUNKNOWN* OutUnknown,
        PUNKNOWN OuterUnknown,
        POOL_TYPE PoolType
    );

private:
    PPORTWAVERTSTREAM m_portStream;
    BOOLEAN m_capture;
    ULONG m_sampleRate;
    ULONG m_channels;
    ULONG m_bitsPerSample;

    PVOID m_dmaBuffer;
    ULONG m_dmaBufferSize;
    PMDL m_dmaBufferMdl;

    volatile ULONG m_position;
    KSSTATE m_state;
};

#else // !_KERNEL_MODE

// ============================================================================
// User-mode miniport classes for test harness compilation
// ============================================================================

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

#endif // _KERNEL_MODE

#endif // MOONSHINE_MINWAVE_HPP
