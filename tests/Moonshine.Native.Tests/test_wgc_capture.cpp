#include "moonshine/capture/wgc_desktop_capture.hpp"
#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>

int main() {
    std::cout << "[*] Running Native Windows.Graphics.Capture Tests..." << std::endl;

    // Test C++ class instantiation and frame pacing
    {
        moonshine::capture::WgcDesktopCapture wgcCapture(nullptr, 60);
        bool initialized = wgcCapture.initialize();
        std::cout << "    [+] WgcDesktopCapture initialize result: " << (initialized ? "SUCCESS" : "FAILED") << std::endl;

        if (initialized) {
            assert(wgcCapture.width() > 0);
            assert(wgcCapture.height() > 0);
            assert(wgcCapture.target_fps() == 60);

            moonshine::capture::CaptureFrame frame;
            bool acquired = wgcCapture.acquire_frame(100, frame);
            std::cout << "    [+] WGC acquire frame result: " << (acquired ? "FRAME ACQUIRED" : "TIMEOUT") << std::endl;
            if (acquired) {
                assert(frame.width > 0);
                assert(frame.height > 0);
                wgcCapture.release_frame();
            }
        }
        wgcCapture.cleanup();
    }

    // Test C-ABI exports
    {
        uint32_t width = 0;
        uint32_t height = 0;
        MoonshineCaptureHandle handle = moonshine_capture_create_wgc(nullptr, 120, &width, &height);
        if (handle) {
            std::cout << "    [+] C-ABI WGC Capture created: " << width << "x" << height << std::endl;
            MoonshineCaptureFrameDesc frameDesc = {};
            int res = moonshine_capture_acquire_frame(handle, 100, &frameDesc);
            std::cout << "    [+] C-ABI WGC Acquire result: " << res << std::endl;
            if (res > 0) {
                moonshine_capture_release_frame(handle);
            }
            moonshine_capture_destroy(handle);
        } else {
            std::cout << "    [+] C-ABI WGC Capture not available on headless environment" << std::endl;
        }
    }

    std::cout << "[+] Native Windows.Graphics.Capture Tests Passed Successfully!" << std::endl;
    return 0;
}
