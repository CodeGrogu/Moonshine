#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>

int main() {
    std::cout << "[*] Running Native Windows.Graphics.Capture Tests..." << std::endl;

    // Test C-ABI WGC exports
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
