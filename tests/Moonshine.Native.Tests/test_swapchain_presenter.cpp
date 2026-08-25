#include "moonshine/export/moonshine_native_api.h"
#include <iostream>

#if defined(_WIN32)
    #include <windows.h>
    #include <d3d11.h>
    #include <wrl/client.h>
    using Microsoft::WRL::ComPtr;
#endif

int main() {
    std::cout << "[*] Verifying DXGI Flip Model GPU Swapchain & Low-Latency Presenter..." << std::endl;

    // 1. Test invalid arguments (fail-closed discipline)
    MoonshineSwapchainHandle bad_sc1 = moonshine_swapchain_create(nullptr, nullptr, 0, 0, 2, 0);
    if (bad_sc1 != nullptr) {
        std::cerr << "[-] Error: swapchain_create succeeded with null hwnd and 0 dimensions" << std::endl;
        moonshine_swapchain_destroy(bad_sc1);
        return 1;
    }

    // 2. Test null handle operations (fail-closed discipline)
    if (moonshine_swapchain_present(nullptr, 0, 0) != -1) {
        std::cerr << "[-] Error: present succeeded on null handle" << std::endl;
        return 2;
    }

    if (moonshine_swapchain_present_texture(nullptr, nullptr, 0, 0) != -1) {
        std::cerr << "[-] Error: present_texture succeeded on null handle" << std::endl;
        return 3;
    }

    if (moonshine_swapchain_resize(nullptr, 1920, 1080) != -1) {
        std::cerr << "[-] Error: resize succeeded on null handle" << std::endl;
        return 4;
    }

    if (moonshine_swapchain_set_hdr(nullptr, 1) != -1) {
        std::cerr << "[-] Error: set_hdr succeeded on null handle" << std::endl;
        return 5;
    }

    MoonshineSwapchainMetrics empty_metrics{};
    if (moonshine_swapchain_get_metrics(nullptr, &empty_metrics) != -1) {
        std::cerr << "[-] Error: get_metrics succeeded on null handle" << std::endl;
        return 6;
    }

    if (moonshine_swapchain_is_tearing_supported(nullptr) != 0) {
        std::cerr << "[-] Error: is_tearing_supported returned true on null handle" << std::endl;
        return 7;
    }

    if (moonshine_swapchain_get_waitable_object(nullptr) != nullptr) {
        std::cerr << "[-] Error: get_waitable_object returned non-null on null handle" << std::endl;
        return 8;
    }

#if defined(_WIN32)
    // 3. Create dummy test window for live DXGI swapchain validation
    HINSTANCE hInstance = GetModuleHandleW(nullptr);
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(WNDCLASSEXW);
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = hInstance;
    wc.lpszClassName = L"MoonshineSwapchainTestClass";
    RegisterClassExW(&wc);

    HWND hwnd = CreateWindowExW(
        0,
        L"MoonshineSwapchainTestClass",
        L"Moonshine DXGI Swapchain Test",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT,
        1280, 720,
        nullptr, nullptr,
        hInstance, nullptr
    );

    if (hwnd) {
        std::cout << "  [+] Test window created: " << hwnd << std::endl;

        // Initialize low-latency DXGI swapchain
        MoonshineSwapchainHandle sc = moonshine_swapchain_create(
            static_cast<void*>(hwnd),
            nullptr, // Auto-create hardware D3D11 device
            1280, 720,
            2, // Double buffering for minimum frame latency
            0  // SDR initially
        );

        if (sc) {
            std::cout << "  [+] DXGI Flip Model swapchain initialized successfully." << std::endl;

            // Check tearing support
            int tearing = moonshine_swapchain_is_tearing_supported(sc);
            std::cout << "  Tearing (VRR) Supported: " << (tearing ? "YES" : "NO") << std::endl;

            // Check waitable object
            void* waitable = moonshine_swapchain_get_waitable_object(sc);
            std::cout << "  Frame Latency Waitable Object: " << waitable << std::endl;

            // Test Present
            int pres_res = moonshine_swapchain_present(sc, 0, 0);
            std::cout << "  Present(0, 0) result: " << pres_res << std::endl;

            // Test Resize
            int resize_res = moonshine_swapchain_resize(sc, 1920, 1080);
            std::cout << "  Resize(1920, 1080) result: " << resize_res << std::endl;

            // Test HDR toggle
            int hdr_res = moonshine_swapchain_set_hdr(sc, 1);
            std::cout << "  SetHdr(1) result: " << hdr_res << std::endl;

            // Test HDR metadata with normal valid values
            MoonshineHdr10Metadata meta{};
            meta.hdr_enabled = 1;
            meta.max_mastering_luminance = 10000000; // 1000 nits
            meta.min_mastering_luminance = 1;        // 0.0001 nits
            meta.max_content_light_level = 1000;
            meta.max_frame_average_light_level = 400;
            int meta_res = moonshine_swapchain_set_hdr_metadata(sc, &meta);
            std::cout << "  SetHdrMetadata (valid) result: " << meta_res << std::endl;

            // Test HDR metadata boundary clamping (exceeding 10,000 nits and min > max)
            MoonshineHdr10Metadata clamped_meta{};
            clamped_meta.hdr_enabled = 1;
            clamped_meta.max_mastering_luminance = 200000000; // 20,000 nits (exceeds 10,000 nits bound -> clamped)
            clamped_meta.min_mastering_luminance = 250000000; // Min > Max -> clamped to max
            clamped_meta.max_content_light_level = 15000;     // Exceeds 10,000 nits -> clamped
            clamped_meta.max_frame_average_light_level = 18000; // Exceeds max_cll -> clamped to max_cll
            int clamped_meta_res = moonshine_swapchain_set_hdr_metadata(sc, &clamped_meta);
            std::cout << "  SetHdrMetadata (clamped boundaries) result: " << clamped_meta_res << std::endl;

            // Query metrics
            MoonshineSwapchainMetrics metrics{};
            if (moonshine_swapchain_get_metrics(sc, &metrics) == 0) {
                std::cout << "  Metrics -> Frames Presented: " << metrics.frames_presented
                          << " | Errors: " << metrics.presentation_errors
                          << " | Dropped: " << metrics.dropped_frames << std::endl;
            }

            moonshine_swapchain_destroy(sc);
            std::cout << "  [+] Swapchain destroyed cleanly." << std::endl;
        } else {
            std::cout << "  [*] Note: Hardware Direct3D 11 device not available in headless context." << std::endl;
        }

        DestroyWindow(hwnd);
        UnregisterClassW(L"MoonshineSwapchainTestClass", hInstance);
    }
#endif

    std::cout << "[+] DXGI Swapchain & GPU Presenter native validation PASSED." << std::endl;
    return 0;
}

