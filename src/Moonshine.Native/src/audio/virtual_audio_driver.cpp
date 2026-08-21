#include "moonshine/audio/virtual_audio_driver.hpp"
#include <cstring>
#include <algorithm>

#if defined(_WIN32)
#include <windows.h>
#include <mmdeviceapi.h>
#include <functiondiscoverykeys_devpkey.h>
#include <avrt.h>
#endif

namespace moonshine::audio {

VirtualAudioDriverController::VirtualAudioDriverController()
    : m_initialized(false)
    , m_isDriverInstalled(false)
    , m_renderEndpointName("Moonshine Audio")
    , m_captureEndpointName("Moonshine Microphone")
{
}

VirtualAudioDriverController::~VirtualAudioDriverController()
{
    Shutdown();
}

bool VirtualAudioDriverController::Initialize()
{
    m_initialized = true;

#if defined(_WIN32)
    // Check if Moonshine Audio endpoints are present in CoreAudio
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    bool coInitialized = SUCCEEDED(hr);

    IMMDeviceEnumerator* pEnumerator = nullptr;
    hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator),
        reinterpret_cast<void**>(&pEnumerator)
    );

    if (SUCCEEDED(hr) && pEnumerator) {
        IMMDeviceCollection* pCollection = nullptr;
        hr = pEnumerator->EnumAudioEndpoints(eAll, DEVICE_STATE_ACTIVE, &pCollection);
        if (SUCCEEDED(hr) && pCollection) {
            UINT count = 0;
            pCollection->GetCount(&count);
            for (UINT i = 0; i < count; ++i) {
                IMMDevice* pEndpoint = nullptr;
                if (SUCCEEDED(pCollection->Item(i, &pEndpoint)) && pEndpoint) {
                    IPropertyStore* pProps = nullptr;
                    if (SUCCEEDED(pEndpoint->OpenPropertyStore(STGM_READ, &pProps)) && pProps) {
                        PROPVARIANT varName;
                        PropVariantInit(&varName);
                        if (SUCCEEDED(pProps->GetValue(PKEY_Device_FriendlyName, &varName)) && varName.pwszVal) {
                            int len = WideCharToMultiByte(CP_UTF8, 0, varName.pwszVal, -1, nullptr, 0, nullptr, nullptr);
                            if (len > 0) {
                                std::string name(static_cast<size_t>(len - 1), '\0');
                                WideCharToMultiByte(CP_UTF8, 0, varName.pwszVal, -1, &name[0], len, nullptr, nullptr);
                                if (name.find("Moonshine Audio") != std::string::npos ||
                                    name.find("Moonshine Microphone") != std::string::npos) {
                                    m_isDriverInstalled = true;
                                }
                            }
                        }
                        PropVariantClear(&varName);
                        pProps->Release();
                    }
                    pEndpoint->Release();
                }
            }
            pCollection->Release();
        }
        pEnumerator->Release();
    }

    if (coInitialized) {
        CoUninitialize();
    }
#else
    // Cross-platform mock state
    m_isDriverInstalled = true;
#endif

    return true;
}

void VirtualAudioDriverController::Shutdown()
{
    m_initialized = false;
}

bool VirtualAudioDriverController::IsDriverInstalled() const
{
    return m_isDriverInstalled;
}

VirtualAudioDriverStatus VirtualAudioDriverController::GetStatus() const
{
    VirtualAudioDriverStatus status{};
    status.isInstalled = m_isDriverInstalled;
    status.isRenderEndpointPresent = m_isDriverInstalled;
    status.isCaptureEndpointPresent = m_isDriverInstalled;
    status.supportedSampleRatesCount = 5; // 44.1k, 48k, 88.2k, 96k, 192k
    status.supportedChannelsCount = 4;    // 1 (Mono), 2 (Stereo), 6 (5.1), 8 (7.1)
    std::snprintf(status.driverVersion, sizeof(status.driverVersion), "%s", "1.0.0.0");
    return status;
}

std::vector<VirtualAudioEndpointInfo> VirtualAudioDriverController::EnumerateEndpoints() const
{
    std::vector<VirtualAudioEndpointInfo> endpoints;

    VirtualAudioEndpointInfo renderInfo{};
    renderInfo.deviceId = "MOONSHINE_AUDIO_RENDER_ENDPOINT";
    renderInfo.friendlyName = m_renderEndpointName;
    renderInfo.role = AudioEndpointRole::RenderSpeaker;
    renderInfo.defaultSampleRate = 48000;
    renderInfo.defaultChannels = 2;
    renderInfo.isDefault = false;
    renderInfo.isActive = m_isDriverInstalled;
    endpoints.push_back(renderInfo);

    VirtualAudioEndpointInfo captureInfo{};
    captureInfo.deviceId = "MOONSHINE_AUDIO_CAPTURE_ENDPOINT";
    captureInfo.friendlyName = m_captureEndpointName;
    captureInfo.role = AudioEndpointRole::CaptureMicrophone;
    captureInfo.defaultSampleRate = 48000;
    captureInfo.defaultChannels = 1;
    captureInfo.isDefault = false;
    captureInfo.isActive = m_isDriverInstalled;
    endpoints.push_back(captureInfo);

    return endpoints;
}

bool VirtualAudioDriverController::ValidateFormat(uint32_t sampleRate, uint32_t channels, uint32_t formatType) const
{
    // Validate sample rate
    if (sampleRate != 44100 &&
        sampleRate != 48000 &&
        sampleRate != 88200 &&
        sampleRate != 96000 &&
        sampleRate != 192000) {
        return false;
    }

    // Validate channels
    if (channels != 1 && channels != 2 && channels != 6 && channels != 8) {
        return false;
    }

    // Validate format: 1 (PCM16), 2 (PCM24), 3 (PCM32), 4 (Float32)
    if (formatType < 1 || formatType > 4) {
        return false;
    }

    return true;
}

bool VirtualAudioDriverController::EnableMmcssScheduling(void* outTaskHandle)
{
#if defined(_WIN32)
    if (!outTaskHandle) {
        return false;
    }

    DWORD taskIndex = 0;
    HANDLE hTask = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
    if (hTask == nullptr) {
        return false;
    }

    *static_cast<HANDLE*>(outTaskHandle) = hTask;
    return true;
#else
    if (outTaskHandle) {
        *static_cast<void**>(outTaskHandle) = reinterpret_cast<void*>(0x1);
    }
    return true;
#endif
}

bool VirtualAudioDriverController::DisableMmcssScheduling(void* taskHandle)
{
#if defined(_WIN32)
    if (!taskHandle) {
        return false;
    }

    return AvRevertMmThreadCharacteristics(static_cast<HANDLE>(taskHandle)) != FALSE;
#else
    (void)taskHandle;
    return true;
#endif
}

} // namespace moonshine::audio
