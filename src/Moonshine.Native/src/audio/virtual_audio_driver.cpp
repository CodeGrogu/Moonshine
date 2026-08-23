#include "moonshine/audio/virtual_audio_driver.hpp"
#include <cstring>
#include <algorithm>

#if defined(_WIN32)
#include <windows.h>
#include <mmdeviceapi.h>
#include <functiondiscoverykeys_devpkey.h>
#include <avrt.h>
#include <setupapi.h>
#pragma comment(lib, "setupapi.lib")
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

DriverInstallationState VirtualAudioDriverController::GetInstallationState() const
{
#if defined(_WIN32)
    bool deviceRegistered = IsDeviceRegisteredViaSetupDi();
    if (!deviceRegistered) {
        return DriverInstallationState::NotInstalled;
    }

    bool endpointsVisible = AreEndpointsVisibleViaCoreAudio();
    if (endpointsVisible) {
        return DriverInstallationState::EndpointsActive;
    }

    return DriverInstallationState::Installed;
#else
    return DriverInstallationState::EndpointsActive;
#endif
}

bool VirtualAudioDriverController::InstallDriver(const char* infPath)
{
#if defined(_WIN32)
    if (!infPath || infPath[0] == '\0') {
        return false;
    }

    // Build the devcon-style installation command
    std::string cmd = "pnputil /add-driver \"";
    cmd += infPath;
    cmd += "\" /install";

    int result = std::system(cmd.c_str());
    if (result == 0) {
        m_isDriverInstalled = true;
    }
    return result == 0;
#else
    (void)infPath;
    m_isDriverInstalled = true;
    return true;
#endif
}

bool VirtualAudioDriverController::RemoveDriver()
{
#if defined(_WIN32)
    // Use devcon to remove the Moonshine virtual audio device
    int result = std::system("pnputil /remove-device ROOT\\MoonshineAudio /subtree");
    if (result == 0) {
        m_isDriverInstalled = false;
    }
    return result == 0;
#else
    m_isDriverInstalled = false;
    return true;
#endif
}

bool VirtualAudioDriverController::RestartDriver()
{
#if defined(_WIN32)
    // Disable then re-enable the device to trigger a full PnP restart
    int disableResult = std::system("pnputil /disable-device ROOT\\MoonshineAudio");
    if (disableResult != 0) {
        return false;
    }

    int enableResult = std::system("pnputil /enable-device ROOT\\MoonshineAudio");
    return enableResult == 0;
#else
    return true;
#endif
}

bool VirtualAudioDriverController::IsDeviceRegisteredViaSetupDi() const
{
#if defined(_WIN32)
    // Query the system for devices matching the Moonshine hardware ID.
    // Uses SetupDiGetClassDevs with DIGCF_ALLCLASSES to enumerate all devices,
    // then checks each device's hardware ID list for "ROOT\\MoonshineAudio".
    // This detects whether the driver is installed even before CoreAudio
    // enumerates the endpoints.

    HDEVINFO deviceInfoSet = SetupDiGetClassDevsW(
        nullptr,             // All classes
        L"ROOT",             // Enumerator filter
        nullptr,             // hwndParent
        DIGCF_ALLCLASSES     // All device classes
    );

    if (deviceInfoSet == INVALID_HANDLE_VALUE) {
        return false;
    }

    SP_DEVINFO_DATA devInfoData{};
    devInfoData.cbSize = sizeof(SP_DEVINFO_DATA);

    bool found = false;
    for (DWORD i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, &devInfoData); ++i) {
        wchar_t hardwareId[256] = {};
        if (SetupDiGetDeviceRegistryPropertyW(
                deviceInfoSet,
                &devInfoData,
                SPDRP_HARDWAREID,
                nullptr,
                reinterpret_cast<PBYTE>(hardwareId),
                sizeof(hardwareId),
                nullptr)) {
            if (wcsstr(hardwareId, L"MoonshineAudio") != nullptr ||
                wcsstr(hardwareId, L"MSHNAUD") != nullptr) {
                found = true;
                break;
            }
        }
    }

    SetupDiDestroyDeviceInfoList(deviceInfoSet);
    return found;
#else
    return true;
#endif
}

bool VirtualAudioDriverController::AreEndpointsVisibleViaCoreAudio() const
{
#if defined(_WIN32)
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    bool coInitialized = SUCCEEDED(hr);

    bool endpointsFound = false;

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
                                    endpointsFound = true;
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

    return endpointsFound;
#else
    return true;
#endif
}

} // namespace moonshine::audio

