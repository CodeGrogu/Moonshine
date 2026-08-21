#include "moonshine/audio/virtual_audio_ipc.hpp"

#include <cstring>
#include <algorithm>
#include <cmath>

#ifdef _WIN32
#include <sddl.h>
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "avrt.lib")
#endif

namespace moonshine::audio {

// ============================================================================
// VirtualAudioIpcChannel Implementation
// ============================================================================

VirtualAudioIpcChannel::VirtualAudioIpcChannel() = default;

VirtualAudioIpcChannel::~VirtualAudioIpcChannel() {
    Close();
}

VirtualAudioIpcChannel::VirtualAudioIpcChannel(VirtualAudioIpcChannel&& other) noexcept
#ifdef _WIN32
    : m_fileMapping(other.m_fileMapping),
      m_syncEvent(other.m_syncEvent),
#else
    :
#endif
      m_sharedRing(other.m_sharedRing),
      m_dataBuffer(other.m_dataBuffer),
      m_isOwner(other.m_isOwner),
      m_endpointType(other.m_endpointType),
      m_bufferCapacityBytes(other.m_bufferCapacityBytes),
      m_frameSizeBytes(other.m_frameSizeBytes),
      m_localBackingMemory(other.m_localBackingMemory) {
#ifdef _WIN32
    other.m_fileMapping = nullptr;
    other.m_syncEvent = nullptr;
#endif
    other.m_sharedRing = nullptr;
    other.m_dataBuffer = nullptr;
    other.m_isOwner = false;
    other.m_localBackingMemory = nullptr;
}

VirtualAudioIpcChannel& VirtualAudioIpcChannel::operator=(VirtualAudioIpcChannel&& other) noexcept {
    if (this != &other) {
        Close();

#ifdef _WIN32
        m_fileMapping = other.m_fileMapping;
        m_syncEvent = other.m_syncEvent;
        other.m_fileMapping = nullptr;
        other.m_syncEvent = nullptr;
#endif
        m_sharedRing = other.m_sharedRing;
        m_dataBuffer = other.m_dataBuffer;
        m_isOwner = other.m_isOwner;
        m_endpointType = other.m_endpointType;
        m_bufferCapacityBytes = other.m_bufferCapacityBytes;
        m_frameSizeBytes = other.m_frameSizeBytes;
        m_localBackingMemory = other.m_localBackingMemory;

        other.m_sharedRing = nullptr;
        other.m_dataBuffer = nullptr;
        other.m_isOwner = false;
        other.m_localBackingMemory = nullptr;
    }
    return *this;
}

#ifdef _WIN32
void VirtualAudioIpcChannel::SetupSecurityDescriptor(void* pSecurityAttributes) {
    if (!pSecurityAttributes) return;
    auto* sa = static_cast<SECURITY_ATTRIBUTES*>(pSecurityAttributes);
    sa->nLength = sizeof(SECURITY_ATTRIBUTES);
    sa->bInheritHandle = FALSE;
    sa->lpSecurityDescriptor = nullptr;

    // DACL allowing Low Integrity, Application Containers, and Standard/Admin Users
    const wchar_t* sddl = L"D:(A;;GA;;;WD)(A;;GA;;;AC)(A;;GA;;;S-1-15-2-1)";
    ConvertStringSecurityDescriptorToSecurityDescriptorW(
        sddl,
        SDDL_REVISION_1,
        &(sa->lpSecurityDescriptor),
        nullptr
    );
}
#endif

bool VirtualAudioIpcChannel::Initialize(
    MoonshineAudioEndpointType endpointType,
    bool isOwner,
    uint32_t sampleRate,
    uint32_t channels,
    MoonshineAudioSampleFormat format,
    uint32_t frameCount
) {
    Close();

    m_endpointType = endpointType;
    m_isOwner = isOwner;

    uint32_t bytesPerSample = (format == MOONSHINE_FORMAT_FLOAT_32 || format == MOONSHINE_FORMAT_PCM_32)
        ? 4
        : (format == MOONSHINE_FORMAT_PCM_24 ? 3 : 2);

    // Frame size for 10ms of audio
    m_frameSizeBytes = (sampleRate * MOONSHINE_AUDIO_DEFAULT_FRAME_MS / 1000) * channels * bytesPerSample;
    m_bufferCapacityBytes = m_frameSizeBytes * frameCount;

    // Ensure 64-byte aligned capacity
    m_bufferCapacityBytes = (m_bufferCapacityBytes + 63) & ~63;

    size_t totalMappingSize = sizeof(MoonshineSharedAudioRing) + m_bufferCapacityBytes;

#ifdef _WIN32
    const wchar_t* globalMemName = (endpointType == MOONSHINE_ENDPOINT_RENDER)
        ? MOONSHINE_SHARED_MEM_RENDER_NAME
        : MOONSHINE_SHARED_MEM_CAPTURE_NAME;
    const wchar_t* localMemName = globalMemName + 7; // Skip "Global\\"

    const wchar_t* globalEventName = (endpointType == MOONSHINE_ENDPOINT_RENDER)
        ? MOONSHINE_SHARED_EVENT_RENDER_NAME
        : MOONSHINE_SHARED_EVENT_CAPTURE_NAME;
    const wchar_t* localEventName = globalEventName + 7;

    SECURITY_ATTRIBUTES sa;
    SetupSecurityDescriptor(&sa);

    if (isOwner) {
        m_fileMapping = CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            &sa,
            PAGE_READWRITE,
            0,
            static_cast<DWORD>(totalMappingSize),
            globalMemName
        );
        if (!m_fileMapping) {
            m_fileMapping = CreateFileMappingW(
                INVALID_HANDLE_VALUE,
                &sa,
                PAGE_READWRITE,
                0,
                static_cast<DWORD>(totalMappingSize),
                localMemName
            );
        }

        m_syncEvent = CreateEventExW(&sa, globalEventName, 0, EVENT_ALL_ACCESS);
        if (!m_syncEvent) {
            m_syncEvent = CreateEventExW(&sa, localEventName, 0, EVENT_ALL_ACCESS);
        }
    } else {
        m_fileMapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, globalMemName);
        if (!m_fileMapping) {
            m_fileMapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, localMemName);
        }

        m_syncEvent = OpenEventW(EVENT_ALL_ACCESS, FALSE, globalEventName);
        if (!m_syncEvent) {
            m_syncEvent = OpenEventW(EVENT_ALL_ACCESS, FALSE, localEventName);
        }

        // If shared memory does not exist, initialize fallback mapping
        if (!m_fileMapping) {
            m_fileMapping = CreateFileMappingW(
                INVALID_HANDLE_VALUE,
                &sa,
                PAGE_READWRITE,
                0,
                static_cast<DWORD>(totalMappingSize),
                globalMemName
            );
            if (!m_fileMapping) {
                m_fileMapping = CreateFileMappingW(
                    INVALID_HANDLE_VALUE,
                    &sa,
                    PAGE_READWRITE,
                    0,
                    static_cast<DWORD>(totalMappingSize),
                    localMemName
                );
            }
        }
        if (!m_syncEvent) {
            m_syncEvent = CreateEventExW(&sa, globalEventName, 0, EVENT_ALL_ACCESS);
            if (!m_syncEvent) {
                m_syncEvent = CreateEventExW(&sa, localEventName, 0, EVENT_ALL_ACCESS);
            }
        }
    }

    if (sa.lpSecurityDescriptor) {
        LocalFree(sa.lpSecurityDescriptor);
    }

    if (m_fileMapping) {
        void* view = MapViewOfFile(m_fileMapping, FILE_MAP_ALL_ACCESS, 0, 0, totalMappingSize);
        if (view) {
            m_sharedRing = static_cast<MoonshineSharedAudioRing*>(view);
            m_dataBuffer = reinterpret_cast<uint8_t*>(m_sharedRing) + sizeof(MoonshineSharedAudioRing);
        }
    }
#endif

    // Fallback heap allocation if OS mapping was unavailable
    if (!m_sharedRing) {
        m_localBackingMemory = new uint8_t[totalMappingSize]();
        m_sharedRing = reinterpret_cast<MoonshineSharedAudioRing*>(m_localBackingMemory);
        m_dataBuffer = m_localBackingMemory + sizeof(MoonshineSharedAudioRing);
    }

    if (isOwner || m_sharedRing->magic != MOONSHINE_AUDIO_MAGIC) {
        m_sharedRing->magic = MOONSHINE_AUDIO_MAGIC;
        m_sharedRing->version = MOONSHINE_AUDIO_VERSION;
        m_sharedRing->endpoint_type = static_cast<uint32_t>(endpointType);
        m_sharedRing->write_position_bytes = 0;
        m_sharedRing->write_packet_count = 0;
        m_sharedRing->read_position_bytes = 0;
        m_sharedRing->read_packet_count = 0;
        m_sharedRing->underrun_count = 0;
        m_sharedRing->overrun_count = 0;
        m_sharedRing->sample_rate = sampleRate;
        m_sharedRing->channels = channels;
        m_sharedRing->sample_format = static_cast<uint32_t>(format);
        m_sharedRing->bytes_per_sample = bytesPerSample;
        m_sharedRing->frame_size_bytes = m_frameSizeBytes;
        m_sharedRing->buffer_capacity_bytes = m_bufferCapacityBytes;
        m_sharedRing->latency_ms = MOONSHINE_AUDIO_DEFAULT_FRAME_MS;
        m_sharedRing->is_active = 1;
        m_sharedRing->is_muted = 0;
        m_sharedRing->volume_scalar = 1.0f;
    }

    return true;
}

void VirtualAudioIpcChannel::Close() {
#ifdef _WIN32
    if (m_sharedRing && !m_localBackingMemory) {
        UnmapViewOfFile(m_sharedRing);
    }
    if (m_fileMapping) {
        CloseHandle(m_fileMapping);
        m_fileMapping = nullptr;
    }
    if (m_syncEvent) {
        CloseHandle(m_syncEvent);
        m_syncEvent = nullptr;
    }
#endif

    if (m_localBackingMemory) {
        delete[] m_localBackingMemory;
        m_localBackingMemory = nullptr;
    }

    m_sharedRing = nullptr;
    m_dataBuffer = nullptr;
    m_isOwner = false;
    m_bufferCapacityBytes = 0;
    m_frameSizeBytes = 0;
}

bool VirtualAudioIpcChannel::IsConnected() const noexcept {
    return (m_sharedRing != nullptr) && (m_sharedRing->magic == MOONSHINE_AUDIO_MAGIC);
}

size_t VirtualAudioIpcChannel::WritePcm(const void* srcBuffer, size_t bytesToWrite) {
    if (!IsConnected() || !srcBuffer || bytesToWrite == 0 || m_bufferCapacityBytes == 0) {
        return 0;
    }

    uint32_t writePos = m_sharedRing->write_position_bytes;
    uint32_t readPos = m_sharedRing->read_position_bytes;

    uint32_t used = (writePos >= readPos)
        ? (writePos - readPos)
        : (m_bufferCapacityBytes - (readPos - writePos));

    uint32_t freeSpace = (m_bufferCapacityBytes > used + 1)
        ? (m_bufferCapacityBytes - used - 1)
        : 0;

    // Overrun handling: if insufficient free space, advance read pointer to drop oldest frame
    if (bytesToWrite > freeSpace) {
        m_sharedRing->overrun_count = m_sharedRing->overrun_count + 1;
        uint32_t needed = static_cast<uint32_t>(bytesToWrite) - freeSpace;
        m_sharedRing->read_position_bytes = (readPos + needed) % m_bufferCapacityBytes;
    }

    const auto* src = static_cast<const uint8_t*>(srcBuffer);
    if (writePos + bytesToWrite <= m_bufferCapacityBytes) {
        std::memcpy(m_dataBuffer + writePos, src, bytesToWrite);
    } else {
        size_t firstChunk = m_bufferCapacityBytes - writePos;
        std::memcpy(m_dataBuffer + writePos, src, firstChunk);
        std::memcpy(m_dataBuffer, src + firstChunk, bytesToWrite - firstChunk);
    }

    std::atomic_thread_fence(std::memory_order_release);
    m_sharedRing->write_position_bytes = (writePos + static_cast<uint32_t>(bytesToWrite)) % m_bufferCapacityBytes;
    m_sharedRing->write_packet_count = m_sharedRing->write_packet_count + 1;

#ifdef _WIN32
    if (m_syncEvent) {
        SetEvent(m_syncEvent);
    }
#endif

    return bytesToWrite;
}

size_t VirtualAudioIpcChannel::ReadPcm(void* destBuffer, size_t bytesToRead, bool waitEvent, uint32_t timeoutMs) {
    if (!IsConnected() || !destBuffer || bytesToRead == 0 || m_bufferCapacityBytes == 0) {
        return 0;
    }

    if (waitEvent) {
        WaitEvent(timeoutMs);
    }

    std::atomic_thread_fence(std::memory_order_acquire);
    uint32_t writePos = m_sharedRing->write_position_bytes;
    uint32_t readPos = m_sharedRing->read_position_bytes;

    uint32_t available = (writePos >= readPos)
        ? (writePos - readPos)
        : (m_bufferCapacityBytes - (readPos - writePos));

    auto* dst = static_cast<uint8_t*>(destBuffer);

    // Underrun handling: if no audio is available, zero pad to prevent audible glitching
    if (available == 0) {
        m_sharedRing->underrun_count = m_sharedRing->underrun_count + 1;
        std::memset(dst, 0, bytesToRead);
        return 0;
    }

    size_t copyBytes = (std::min)(static_cast<size_t>(available), bytesToRead);

    if (readPos + copyBytes <= m_bufferCapacityBytes) {
        std::memcpy(dst, m_dataBuffer + readPos, copyBytes);
    } else {
        size_t firstChunk = m_bufferCapacityBytes - readPos;
        std::memcpy(dst, m_dataBuffer + readPos, firstChunk);
        std::memcpy(dst + firstChunk, m_dataBuffer, copyBytes - firstChunk);
    }

    // Partial underrun: pad remaining requested bytes with silence
    if (copyBytes < bytesToRead) {
        m_sharedRing->underrun_count = m_sharedRing->underrun_count + 1;
        std::memset(dst + copyBytes, 0, bytesToRead - copyBytes);
    }

    std::atomic_thread_fence(std::memory_order_release);
    m_sharedRing->read_position_bytes = (readPos + static_cast<uint32_t>(copyBytes)) % m_bufferCapacityBytes;
    m_sharedRing->read_packet_count = m_sharedRing->read_packet_count + 1;

    return copyBytes;
}

bool VirtualAudioIpcChannel::WaitEvent(uint32_t timeoutMs) {
#ifdef _WIN32
    if (!m_syncEvent) return false;
    DWORD res = WaitForSingleObject(m_syncEvent, timeoutMs);
    return (res == WAIT_OBJECT_0);
#else
    (void)timeoutMs;
    return true;
#endif
}

uint32_t VirtualAudioIpcChannel::GetAvailableReadBytes() const noexcept {
    if (!IsConnected() || m_bufferCapacityBytes == 0) return 0;
    uint32_t writePos = m_sharedRing->write_position_bytes;
    uint32_t readPos = m_sharedRing->read_position_bytes;
    return (writePos >= readPos) ? (writePos - readPos) : (m_bufferCapacityBytes - (readPos - writePos));
}

uint32_t VirtualAudioIpcChannel::GetAvailableWriteBytes() const noexcept {
    if (!IsConnected() || m_bufferCapacityBytes == 0) return 0;
    uint32_t used = GetAvailableReadBytes();
    return (m_bufferCapacityBytes > used + 1) ? (m_bufferCapacityBytes - used - 1) : 0;
}

uint32_t VirtualAudioIpcChannel::GetUnderrunCount() const noexcept {
    return m_sharedRing ? m_sharedRing->underrun_count : 0;
}

uint32_t VirtualAudioIpcChannel::GetOverrunCount() const noexcept {
    return m_sharedRing ? m_sharedRing->overrun_count : 0;
}

uint32_t VirtualAudioIpcChannel::GetPacketCount() const noexcept {
    return m_sharedRing ? m_sharedRing->write_packet_count : 0;
}

// ============================================================================
// VirtualAudioIpcBridge Implementation
// ============================================================================

VirtualAudioIpcBridge::VirtualAudioIpcBridge() = default;

VirtualAudioIpcBridge::~VirtualAudioIpcBridge() {
    Shutdown();
}

bool VirtualAudioIpcBridge::Initialize(
    bool isHostServer,
    uint32_t sampleRate,
    uint32_t channels
) {
    Shutdown();

    m_isHostServer = isHostServer;
    m_sampleRate = sampleRate;
    m_channels = channels;

    // Render channel: Driver is owner/producer, Host is consumer (or vice-versa in test harness)
    bool renderOk = m_renderChannel.Initialize(
        MOONSHINE_ENDPOINT_RENDER,
        !isHostServer,
        sampleRate,
        channels,
        MOONSHINE_FORMAT_FLOAT_32,
        MOONSHINE_AUDIO_RING_BUFFER_FRAMES
    );

    // Capture channel: Host is producer, Driver is consumer
    bool captureOk = m_captureChannel.Initialize(
        MOONSHINE_ENDPOINT_CAPTURE,
        isHostServer,
        sampleRate,
        channels,
        MOONSHINE_FORMAT_FLOAT_32,
        MOONSHINE_AUDIO_RING_BUFFER_FRAMES
    );

    return renderOk && captureOk;
}

void VirtualAudioIpcBridge::Shutdown() {
    RevertMmcss();
    m_renderChannel.Close();
    m_captureChannel.Close();
}

bool VirtualAudioIpcBridge::IsConnected() const noexcept {
    return m_renderChannel.IsConnected() && m_captureChannel.IsConnected();
}

size_t VirtualAudioIpcBridge::WriteCapturePcm(const float* pcmSamples, size_t sampleCount) {
    if (!pcmSamples || sampleCount == 0) return 0;
    size_t byteCount = sampleCount * sizeof(float);
    size_t bytesWritten = m_captureChannel.WritePcm(pcmSamples, byteCount);
    return bytesWritten / sizeof(float);
}

size_t VirtualAudioIpcBridge::ReadRenderPcm(float* outPcmSamples, size_t maxSamples, bool waitEvent, uint32_t timeoutMs) {
    if (!outPcmSamples || maxSamples == 0) return 0;
    size_t byteCount = maxSamples * sizeof(float);
    size_t bytesRead = m_renderChannel.ReadPcm(outPcmSamples, byteCount, waitEvent, timeoutMs);
    return bytesRead / sizeof(float);
}

bool VirtualAudioIpcBridge::WaitRenderEvent(uint32_t timeoutMs) {
    return m_renderChannel.WaitEvent(timeoutMs);
}

VirtualAudioIpcMetrics VirtualAudioIpcBridge::GetMetrics() const noexcept {
    VirtualAudioIpcMetrics metrics{};
    metrics.renderPacketsRead = m_renderChannel.GetPacketCount();
    metrics.renderUnderruns = m_renderChannel.GetUnderrunCount();
    metrics.renderOverruns = m_renderChannel.GetOverrunCount();
    metrics.capturePacketsWritten = m_captureChannel.GetPacketCount();
    metrics.captureUnderruns = m_captureChannel.GetUnderrunCount();
    metrics.captureOverruns = m_captureChannel.GetOverrunCount();
    metrics.sampleRate = m_sampleRate;
    metrics.channels = m_channels;
    metrics.isConnected = IsConnected() ? 1 : 0;
    return metrics;
}

bool VirtualAudioIpcBridge::EnableMmcss() {
#ifdef _WIN32
    if (m_mmcssHandle) return true;
    m_mmcssTaskIndex = 0;
    m_mmcssHandle = AvSetMmThreadCharacteristicsW(L"Pro Audio", &m_mmcssTaskIndex);
    return (m_mmcssHandle != nullptr);
#else
    return true;
#endif
}

void VirtualAudioIpcBridge::RevertMmcss() {
#ifdef _WIN32
    if (m_mmcssHandle) {
        AvRevertMmThreadCharacteristics(m_mmcssHandle);
        m_mmcssHandle = nullptr;
    }
#endif
}

} // namespace moonshine::audio
