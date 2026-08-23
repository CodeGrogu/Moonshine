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

/// Driver installation and runtime state.
enum class DriverInstallationState {
    /// Driver package is not registered with the system.
    NotInstalled = 0,
    /// Driver package is installed but endpoints are not yet visible to CoreAudio.
    Installed = 1,
    /// Driver is installed and Moonshine Audio / Moonshine Microphone endpoints are active.
    EndpointsActive = 2,
    /// An error occurred during driver state detection.
    Error = 3
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

    /// Queries the current driver installation and endpoint state.
    DriverInstallationState GetInstallationState() const;

    /// Attempts to install the driver using the INF file at the given path.
    /// Returns true if the installation command succeeded.
    bool InstallDriver(const char* infPath);

    /// Attempts to remove the Moonshine virtual audio device.
    /// Returns true if the removal command succeeded.
    bool RemoveDriver();

    /// Restarts the Moonshine virtual audio device (disable then enable).
    /// Returns true if the restart completed without error.
    bool RestartDriver();

private:
    bool m_initialized;
    bool m_isDriverInstalled;
    std::string m_renderEndpointName;
    std::string m_captureEndpointName;

    /// Checks whether a device with the Moonshine hardware ID exists via SetupDi.
    bool IsDeviceRegisteredViaSetupDi() const;

    /// Checks whether Moonshine endpoints are visible via CoreAudio MMDevice enumeration.
    bool AreEndpointsVisibleViaCoreAudio() const;
};

} // namespace moonshine::audio

#endif // MOONSHINE_VIRTUAL_AUDIO_DRIVER_HPP
