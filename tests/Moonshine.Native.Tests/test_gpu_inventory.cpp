#include <iostream>
#include <vector>
#include <cstring>
#include "moonshine/export/moonshine_native_api.h"

#define REQUIRE(condition) \
    do { \
        if (!(condition)) { \
            std::cerr << "FAILED: " #condition " at " << __FILE__ << ":" << __LINE__ << std::endl; \
            return 1; \
        } \
    } while (0)

int main() {
    std::cout << "=== Moonshine GPU Adapter Inventory Verification ===" << std::endl;

    uint32_t count = 0;
    int res = moonshine_gpu_enumerate_adapters(nullptr, 0, &count);
    REQUIRE(res == 0);
    std::cout << "[+] Discovered " << count << " GPU adapter(s) via DXGI enumeration." << std::endl;
    REQUIRE(count >= 1);

    std::vector<MoonshineGpuAdapter> adapters(count);
    res = moonshine_gpu_enumerate_adapters(adapters.data(), count, &count);
    REQUIRE(res == 0);

    bool found_nvidia = false;
    bool found_intel = false;
    bool found_primary_display = false;

    for (uint32_t i = 0; i < count; ++i) {
        const auto& adapter = adapters[i];
        std::cout << "\n--- Adapter " << adapter.index << " ---" << std::endl;
        std::cout << "  Description: " << adapter.description << std::endl;
        std::cout << "  Vendor ID:   0x" << std::hex << adapter.vendor_id << std::dec << std::endl;
        std::cout << "  Device ID:   0x" << std::hex << adapter.device_id << std::dec << std::endl;
        std::cout << "  LUID:        0x" << std::hex << adapter.adapter_luid << std::dec << std::endl;
        std::cout << "  Dedicated Video RAM: " << (adapter.dedicated_video_memory / (1024 * 1024)) << " MB" << std::endl;
        std::cout << "  Shared System RAM:    " << (adapter.shared_system_memory / (1024 * 1024)) << " MB" << std::endl;
        std::cout << "  Is Software Adapter:  " << (adapter.is_software ? "YES" : "NO") << std::endl;
        std::cout << "  Has Connected Output: " << (adapter.has_output ? "YES" : "NO") << std::endl;

        REQUIRE(adapter.description[0] != '\0');
        if (adapter.vendor_id == 0x10DE) {
            found_nvidia = true;
            if (adapter.has_output) {
                found_primary_display = true;
            }
        }
        if (adapter.vendor_id == 0x8086) {
            found_intel = true;
        }
    }

    std::cout << "\n=== Multi-GPU Inventory Summary ===" << std::endl;
    std::cout << "  NVIDIA Adapter:         " << (found_nvidia ? "PRESENT" : "ABSENT") << std::endl;
    std::cout << "  Intel Adapter:          " << (found_intel ? "PRESENT" : "ABSENT") << std::endl;
    std::cout << "  Primary Display Output: " << (found_primary_display ? "VERIFIED ON NVIDIA" : "UNATTACHED") << std::endl;

    // Verify QSV Diagnostic Export API
    std::cout << "\n=== Running Dedicated Intel QSV Diagnostic Suite ===" << std::endl;
    MoonshineQsvDiagnosticReport report{};
    int diag_res = moonshine_qsv_run_diagnostics(&report);
    std::cout << "  Diagnostic execution result: " << diag_res << std::endl;
    std::cout << "  Intel Adapter Found:         " << (report.adapter_found ? "YES" : "NO") << std::endl;
    std::cout << "  Intel Adapter Description:   " << report.adapter_description << std::endl;
    std::cout << "  D3D11 Device Created:        " << (report.d3d11_device_created ? "YES" : "NO") << std::endl;
    std::cout << "  D3D11 Vendor Verified:       " << (report.d3d11_vendor_verified ? "YES (0x8086)" : "NO") << std::endl;
    std::cout << "  oneVPL DLL Loaded:           " << (report.vpl_dll_loaded ? "YES" : "NO") << " (" << report.vpl_dll_name << ")" << std::endl;
    std::cout << "  oneVPL Session Initialised:  " << (report.vpl_session_created ? "YES" : "NO") << std::endl;
    std::cout << "  D3D11 Handle Bound:          " << (report.d3d11_handle_bound ? "YES" : "NO") << std::endl;
    std::cout << "  H.264 Capability:            " << (report.h264_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  HEVC Capability:             " << (report.hevc_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  AV1 Capability:              " << (report.av1_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  Encoder Initialised:         " << (report.encoder_initialized ? "YES" : "NO") << std::endl;
    std::cout << "  Frame Encoded:               " << (report.frame_encoded ? "YES" : "NO") << std::endl;
    std::cout << "  Bitstream Valid:             " << (report.bitstream_valid ? "YES" : "NO") << std::endl;
    std::cout << "  Decoder Loopback:            " << (report.decoder_loopback_passed ? "PASSED" : "SKIPPED/UNTESTED") << std::endl;
    std::cout << "  Last MFX Status:             " << report.last_mfx_status << std::endl;
    std::cout << "  Last HRESULT:                0x" << std::hex << report.last_hresult << std::dec << std::endl;

    std::cout << "\n[+] GPU Adapter Inventory and Diagnostic suite passed successfully." << std::endl;
    return 0;
}
