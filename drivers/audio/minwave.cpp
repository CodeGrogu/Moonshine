#include "minwave.hpp"
#include <cstdlib>
#include <cstring>

#ifdef _KERNEL_MODE

// ============================================================================
// CMiniportWaveRTStreamMoonshine: Kernel-mode per-stream implementation
// ============================================================================

CMiniportWaveRTStreamMoonshine::CMiniportWaveRTStreamMoonshine(PUNKNOWN OuterUnknown)
    : CUnknown(OuterUnknown)
    , m_portStream(nullptr)
    , m_capture(FALSE)
    , m_sampleRate(MOONSHINE_AUDIO_DEFAULT_SAMPLE_RATE)
    , m_channels(MOONSHINE_LAYOUT_STEREO)
    , m_bitsPerSample(32)
    , m_dmaBuffer(nullptr)
    , m_dmaBufferSize(0)
    , m_dmaBufferMdl(nullptr)
    , m_position(0)
    , m_state(KSSTATE_STOP)
{
}

CMiniportWaveRTStreamMoonshine::~CMiniportWaveRTStreamMoonshine()
{
    if (m_portStream)
    {
        m_portStream->Release();
        m_portStream = nullptr;
    }

    // Buffer is freed via FreeAudioBuffer by PortCls during stream teardown.
    // Do not free it here to avoid double-free.
}

NTSTATUS CMiniportWaveRTStreamMoonshine::CreateInstance(
    PUNKNOWN* OutUnknown,
    PUNKNOWN OuterUnknown,
    POOL_TYPE PoolType
)
{
    if (!OutUnknown)
    {
        return STATUS_INVALID_PARAMETER;
    }

    auto* instance = new(PoolType, 'strC') CMiniportWaveRTStreamMoonshine(OuterUnknown);
    if (!instance)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    instance->AddRef();
    *OutUnknown = reinterpret_cast<PUNKNOWN>(instance);
    return STATUS_SUCCESS;
}

NTSTATUS CMiniportWaveRTStreamMoonshine::Init(
    BOOLEAN Capture,
    ULONG SampleRate,
    ULONG Channels,
    ULONG BitsPerSample,
    PPORTWAVERTSTREAM PortStream
)
{
    m_capture = Capture;
    m_sampleRate = SampleRate;
    m_channels = Channels;
    m_bitsPerSample = BitsPerSample;
    m_position = 0;
    m_state = KSSTATE_STOP;

    if (PortStream)
    {
        m_portStream = PortStream;
        m_portStream->AddRef();
    }

    return STATUS_SUCCESS;
}

/// @brief Allocates the cyclic audio buffer for WaveRT shared memory access.
///
/// For a virtual audio device there is no physical DMA engine, so the buffer
/// is allocated from non-paged pool and mapped to user-mode via an MDL.
/// The buffer size is aligned to a 4KB page boundary for efficient MDL mapping.
NTSTATUS CMiniportWaveRTStreamMoonshine::AllocateAudioBuffer(
    ULONG RequestedSize,
    PMDL* AudioBufferMdl,
    ULONG* ActualSize,
    ULONG* OffsetFromFirstPage,
    MEMORY_CACHING_TYPE* CacheType
)
{
    if (!AudioBufferMdl || !ActualSize || !OffsetFromFirstPage || !CacheType)
    {
        return STATUS_INVALID_PARAMETER;
    }

    if (RequestedSize == 0)
    {
        return STATUS_INVALID_PARAMETER;
    }

    // Align to 4KB page boundary
    ULONG alignedSize = (RequestedSize + PAGE_SIZE - 1) & ~(PAGE_SIZE - 1);

    m_dmaBuffer = ExAllocatePool2(POOL_FLAG_NON_PAGED, alignedSize, 'abuf');
    if (!m_dmaBuffer)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlZeroMemory(m_dmaBuffer, alignedSize);
    m_dmaBufferSize = alignedSize;

    m_dmaBufferMdl = IoAllocateMdl(
        m_dmaBuffer,
        alignedSize,
        FALSE,
        FALSE,
        nullptr
    );
    if (!m_dmaBufferMdl)
    {
        ExFreePoolWithTag(m_dmaBuffer, 'abuf');
        m_dmaBuffer = nullptr;
        m_dmaBufferSize = 0;
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    MmBuildMdlForNonPagedPool(m_dmaBufferMdl);

    *AudioBufferMdl = m_dmaBufferMdl;
    *ActualSize = alignedSize;
    *OffsetFromFirstPage = 0;
    *CacheType = MmCached;

    return STATUS_SUCCESS;
}

/// @brief Frees the previously allocated cyclic audio buffer.
void CMiniportWaveRTStreamMoonshine::FreeAudioBuffer(
    PMDL AudioBufferMdl,
    ULONG BufferSize
)
{
    UNREFERENCED_PARAMETER(BufferSize);

    if (AudioBufferMdl)
    {
        IoFreeMdl(AudioBufferMdl);
    }

    if (m_dmaBuffer)
    {
        ExFreePoolWithTag(m_dmaBuffer, 'abuf');
        m_dmaBuffer = nullptr;
    }

    m_dmaBufferMdl = nullptr;
    m_dmaBufferSize = 0;
}

/// @brief Reports the hardware latency contribution.
///
/// A virtual device adds zero hardware latency since there is no physical
/// codec or DMA path. The reported latency covers only the software ring
/// buffer period.
void CMiniportWaveRTStreamMoonshine::GetHWLatency(
    KSRTAUDIO_HWLATENCY* Latency
)
{
    if (Latency)
    {
        Latency->ChipsetDelay = 0;
        Latency->CodecDelay = 0;
        Latency->FifoSize = 0;
    }
}

/// @brief Returns the current stream position in the cyclic buffer.
///
/// For a virtual device, the position is maintained in software. The audio
/// engine reads this value to determine how much data has been consumed
/// (render) or produced (capture).
NTSTATUS CMiniportWaveRTStreamMoonshine::GetPosition(
    KSAUDIO_POSITION* Position
)
{
    if (!Position)
    {
        return STATUS_INVALID_PARAMETER;
    }

    Position->PlayOffset = m_position;
    Position->WriteOffset = m_position;
    return STATUS_SUCCESS;
}

/// @brief Returns a memory-mapped hardware clock register.
///
/// Virtual devices do not have hardware clock registers. Returning
/// STATUS_NOT_IMPLEMENTED causes the audio engine to fall back to
/// GetPosition() polling.
NTSTATUS CMiniportWaveRTStreamMoonshine::GetClockRegister(
    PKSRTAUDIO_HWREGISTER Register
)
{
    UNREFERENCED_PARAMETER(Register);
    return STATUS_NOT_IMPLEMENTED;
}

/// @brief Returns a memory-mapped hardware position register.
///
/// Virtual devices do not have hardware position registers. Returning
/// STATUS_NOT_IMPLEMENTED causes the audio engine to fall back to
/// GetPosition() polling.
NTSTATUS CMiniportWaveRTStreamMoonshine::GetPositionRegister(
    PKSRTAUDIO_HWREGISTER Register
)
{
    UNREFERENCED_PARAMETER(Register);
    return STATUS_NOT_IMPLEMENTED;
}

/// @brief Transitions the stream between KS states.
///
/// KSSTATE_RUN activates the stream; all other states pause or stop it.
/// For a virtual device, this controls the logical "active" flag.
void CMiniportWaveRTStreamMoonshine::SetState(
    KSSTATE State
)
{
    m_state = State;

    if (State == KSSTATE_STOP)
    {
        m_position = 0;
    }
}

STDMETHODIMP_(NTSTATUS) CMiniportWaveRTStreamMoonshine::QueryInterface(
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
        *Object = static_cast<IUnknown*>(static_cast<IMiniportWaveRTStream*>(this));
    }
    else if (IsEqualGUIDAligned(Interface, IID_IMiniportWaveRTStream))
    {
        *Object = static_cast<IMiniportWaveRTStream*>(this);
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
// User-mode miniport classes for test harness
// ============================================================================

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
    if (!m_isActive) {
        m_position = 0;
    }
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

#endif // _KERNEL_MODE
