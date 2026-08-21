#pragma once

#include <cstdint>
#include <cstddef>
#include <atomic>
#include <string>
#include <memory>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <avrt.h>
#else
using HANDLE = void*;
#endif

#include "shared_audio_buffer.h"

namespace moonshine::audio {

struct VirtualAudioIpcMetrics {
    uint32_t renderPacketsRead{0};
    uint32_t renderUnderruns{0};
    uint32_t renderOverruns{0};
    uint32_t capturePacketsWritten{0};
    uint32_t captureUnderruns{0};
    uint32_t captureOverruns{0};
    uint32_t sampleRate{48000};
    uint32_t channels{2};
    uint32_t isConnected{0};
};

class alignas(64) VirtualAudioIpcChannel {
public:
    VirtualAudioIpcChannel();
    ~VirtualAudioIpcChannel();

    // Disable copy semantics
    VirtualAudioIpcChannel(const VirtualAudioIpcChannel&) = delete;
    VirtualAudioIpcChannel& operator=(const VirtualAudioIpcChannel&) = delete;

    // Enable move semantics
    VirtualAudioIpcChannel(VirtualAudioIpcChannel&& other) noexcept;
    VirtualAudioIpcChannel& operator=(VirtualAudioIpcChannel&& other) noexcept;

    bool Initialize(
        MoonshineAudioEndpointType endpointType,
        bool isOwner,
        uint32_t sampleRate = MOONSHINE_AUDIO_DEFAULT_SAMPLE_RATE,
        uint32_t channels = MOONSHINE_LAYOUT_STEREO,
        MoonshineAudioSampleFormat format = MOONSHINE_FORMAT_FLOAT_32,
        uint32_t frameCount = MOONSHINE_AUDIO_RING_BUFFER_FRAMES
    );

    void Close();

    [[nodiscard]] bool IsConnected() const noexcept;
    [[nodiscard]] MoonshineAudioEndpointType GetEndpointType() const noexcept { return m_endpointType; }

    size_t WritePcm(const void* srcBuffer, size_t bytesToWrite);
    size_t ReadPcm(void* destBuffer, size_t bytesToRead, bool waitEvent = false, uint32_t timeoutMs = 15);
    bool WaitEvent(uint32_t timeoutMs = 15);

    [[nodiscard]] uint32_t GetAvailableReadBytes() const noexcept;
    [[nodiscard]] uint32_t GetAvailableWriteBytes() const noexcept;

    [[nodiscard]] uint32_t GetUnderrunCount() const noexcept;
    [[nodiscard]] uint32_t GetOverrunCount() const noexcept;
    [[nodiscard]] uint32_t GetPacketCount() const noexcept;

private:
    HANDLE m_fileMapping{nullptr};
    MoonshineSharedAudioRing* m_sharedRing{nullptr};
    uint8_t* m_dataBuffer{nullptr};
    HANDLE m_syncEvent{nullptr};
    bool m_isOwner{false};
    MoonshineAudioEndpointType m_endpointType{MOONSHINE_ENDPOINT_RENDER};
    uint32_t m_bufferCapacityBytes{0};
    uint32_t m_frameSizeBytes{0};
    uint8_t* m_localBackingMemory{nullptr};

    void SetupSecurityDescriptor(void* pSecurityAttributes);
};

class VirtualAudioIpcBridge {
public:
    VirtualAudioIpcBridge();
    ~VirtualAudioIpcBridge();

    // Disable copy semantics
    VirtualAudioIpcBridge(const VirtualAudioIpcBridge&) = delete;
    VirtualAudioIpcBridge& operator=(const VirtualAudioIpcBridge&) = delete;

    bool Initialize(
        bool isHostServer,
        uint32_t sampleRate = MOONSHINE_AUDIO_DEFAULT_SAMPLE_RATE,
        uint32_t channels = MOONSHINE_LAYOUT_STEREO
    );

    void Shutdown();

    [[nodiscard]] bool IsConnected() const noexcept;

    // Capture (Microphone) Channel: Host writes decoded client mic PCM into driver capture ring
    size_t WriteCapturePcm(const float* pcmSamples, size_t sampleCount);

    // Render (Speaker) Channel: Host reads rendered game audio PCM from driver render ring
    size_t ReadRenderPcm(float* outPcmSamples, size_t maxSamples, bool waitEvent = false, uint32_t timeoutMs = 15);

    bool WaitRenderEvent(uint32_t timeoutMs = 15);

    [[nodiscard]] VirtualAudioIpcMetrics GetMetrics() const noexcept;

    bool EnableMmcss();
    void RevertMmcss();

private:
    VirtualAudioIpcChannel m_renderChannel;
    VirtualAudioIpcChannel m_captureChannel;
    bool m_isHostServer{true};
    uint32_t m_sampleRate{48000};
    uint32_t m_channels{2};
    HANDLE m_mmcssHandle{nullptr};
    DWORD m_mmcssTaskIndex{0};
};

} // namespace moonshine::audio
