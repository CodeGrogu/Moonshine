#ifndef MOONSHINE_VIRTUAL_AUDIO_DRIVER_HPP
#define MOONSHINE_VIRTUAL_AUDIO_DRIVER_HPP

#include <cstdint>
#include <string>
#include <vector>

namespace moonshine::audio {

enum class AudioEndpointRole {
    RenderSpeaker = 0,
    CaptureMicrophone = 1
};

struct VirtualAudioEndpointInfo {
    std::string deviceId;
    std::string friendlyName;
    AudioEndpointRole role;
    uint32_t defaultSampleRate;
    uint32_t defaultChannels;
    bool isDefault;
    bool isActive;
};

struct VirtualAudioDriverStatus {
    bool isInstalled;
    bool isRenderEndpointPresent;
    bool isCaptureEndpointPresent;
    uint32_t supportedSampleRatesCount;
    uint32_t supportedChannelsCount;
    char driverVersion[32];
};

class VirtualAudioDriverController {
public:
    VirtualAudioDriverController();
    ~VirtualAudioDriverController();

    bool Initialize();
    void Shutdown();

    bool IsDriverInstalled() const;
    VirtualAudioDriverStatus GetStatus() const;
    std::vector<VirtualAudioEndpointInfo> EnumerateEndpoints() const;

    bool ValidateFormat(uint32_t sampleRate, uint32_t channels, uint32_t formatType) const;

    bool EnableMmcssScheduling(void* outTaskHandle);
    bool DisableMmcssScheduling(void* taskHandle);

    const char* GetRenderEndpointName() const { return m_renderEndpointName.c_str(); }
    const char* GetCaptureEndpointName() const { return m_captureEndpointName.c_str(); }

private:
    bool m_initialized;
    bool m_isDriverInstalled;
    std::string m_renderEndpointName;
    std::string m_captureEndpointName;
};

} // namespace moonshine::audio

#endif // MOONSHINE_VIRTUAL_AUDIO_DRIVER_HPP
