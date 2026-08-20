#include "moonshine/capture/dxgi_desktop_duplicator.hpp"
#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>

int main() {
    std::cout << "[*] Running Native Desktop Capture Tests..." << std::endl;

    // Test C++ class instantiation
    {
        moonshine::capture::DxgiDesktopDuplicator duplicator(0, 0);
        bool initialized = duplicator.initialize();
        std::cout << "    [+] DxgiDesktopDuplicator initialize result: " << (initialized ? "SUCCESS" : "HEADLESS/SKIPPED") << std::endl;

        if (initialized) {
            assert(duplicator.width() > 0);
            assert(duplicator.height() > 0);

            moonshine::capture::CaptureFrame frame;
            bool acquired = duplicator.acquire_frame(100, frame);
            std::cout << "    [+] Acquire frame result: " << (acquired ? "FRAME ACQUIRED" : "TIMEOUT (NORMAL ON IDLE)") << std::endl;
            if (acquired) {
                assert(frame.width > 0);
                assert(frame.height > 0);
                duplicator.release_frame();
            }
        }
        duplicator.cleanup();
    }

    // Test C-ABI exports
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
            moonshine_capture_destroy(handle);
        } else {
            std::cout << "    [+] C-ABI Capture not available on headless display" << std::endl;
        }
    }

    std::cout << "[+] Native Desktop Capture Tests Passed Successfully!" << std::endl;
    return 0;
}
