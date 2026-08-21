#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>
#include <cstdlib>

int main() {
    std::cout << "[*] Running Native Desktop Capture Tests..." << std::endl;

    // 1. Test Adapter Enumeration
    uint32_t adapterCount = moonshine_capture_get_adapter_count();
    std::cout << "    [+] Discovered " << adapterCount << " GPU adapters" << std::endl;
    if (adapterCount < 1) {
        std::cerr << "[-] No GPU adapters discovered!" << std::endl;
        return 1;
    }

    for (uint32_t a = 0; a < adapterCount; ++a) {
        MoonshineAdapterInfo adapterInfo = {};
        int res = moonshine_capture_get_adapter_info(a, &adapterInfo);
        if (res != 0) {
            std::cerr << "[-] Failed to get adapter info for adapter " << a << std::endl;
            return 1;
        }
        std::cout << "        Adapter " << a << ": " << adapterInfo.description
                  << " (Hardware: " << (adapterInfo.is_hardware ? "Yes" : "No")
                  << ", VRAM: " << (adapterInfo.dedicated_video_memory / (1024 * 1024)) << " MB)"
                  << std::endl;

        // 2. Test Display Enumeration
        uint32_t displayCount = moonshine_capture_get_display_count(a);
        std::cout << "        Discovered " << displayCount << " displays on adapter " << a << std::endl;
        for (uint32_t d = 0; d < displayCount; ++d) {
            MoonshineDisplayInfo displayInfo = {};
            int dres = moonshine_capture_get_display_info(a, d, &displayInfo);
            if (dres != 0) {
                std::cerr << "[-] Failed to get display info for display " << d << std::endl;
                return 1;
            }
            std::cout << "            Display " << d << ": " << displayInfo.width << "x" << displayInfo.height
                      << " @ " << displayInfo.refresh_rate_num << "/" << displayInfo.refresh_rate_den << "Hz"
                      << " (HDR: " << (displayInfo.is_hdr ? "Yes" : "No")
                      << ", BPC: " << (uint32_t)displayInfo.bits_per_color << ")"
                      << std::endl;
        }
    }

    // 3. Test C-ABI DXGI Desktop Duplication and Frame Lifecycle
    {
        uint32_t width = 0;
        uint32_t height = 0;
        MoonshineCaptureHandle handle = moonshine_capture_create_dxgi(0, 0, &width, &height);
        if (handle) {
            std::cout << "    [+] C-ABI Capture created: " << width << "x" << height << std::endl;

            MoonshineCaptureFrameDesc frameDesc = {};
            int res = moonshine_capture_acquire_frame(handle, 100, &frameDesc);
            std::cout << "    [+] C-ABI Acquire result: " << res << std::endl;
            if (res > 0) {
                moonshine_capture_release_frame(handle);
            }

            int recRes = moonshine_capture_recover(handle);
            std::cout << "    [+] C-ABI Recovery result: " << recRes << std::endl;
            if (recRes != 1) {
                std::cerr << "[-] Failed to recover desktop capture!" << std::endl;
                return 1;
            }

            moonshine_capture_destroy(handle);
        } else {
            std::cout << "    [+] C-ABI Capture not available on headless display (normal for headless CI)" << std::endl;
        }
    }

    std::cout << "[+] Native Desktop Capture Tests Passed Successfully!" << std::endl;
    return 0;
}
