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

    // Defensive input validation for GPU inventory and diagnostics
    std::cout << "\n=== Testing Defensive Error Handling on Export Boundaries ===" << std::endl;
    REQUIRE(moonshine_qsv_run_diagnostics(nullptr) == -1);
    std::cout << "  [+] moonshine_qsv_run_diagnostics(nullptr) safely failed closed." << std::endl;

    // Direct3D 11 Post-Creation Vendor Invariant Verification
    std::cout << "\n=== Testing Direct3D 11 Device Vendor Invariant Creation ===" << std::endl;
    void* invalid_dev = moonshine_d3d11_create_device(0x9999);
    REQUIRE(invalid_dev == nullptr);
    std::cout << "  [+] Nonexistent vendor ID (0x9999) failed closed with nullptr." << std::endl;

    if (found_intel) {
        void* intel_dev = moonshine_d3d11_create_device(0x8086);
        REQUIRE(intel_dev != nullptr);
        std::cout << "  [+] Intel Direct3D 11 device created and vendor verified." << std::endl;
        moonshine_d3d11_destroy_device(intel_dev);

        void* intel_adapter_dev = moonshine_d3d11_create_device_on_adapter(0x8086, 0);
        REQUIRE(intel_adapter_dev != nullptr);
        std::cout << "  [+] Intel Direct3D 11 device on specific adapter index (0x8086, 0) created successfully." << std::endl;
        moonshine_d3d11_destroy_device(intel_adapter_dev);
    }

    // Verify QSV Diagnostic Export API
    std::cout << "\n=== Running Dedicated Intel QSV Diagnostic Suite ===" << std::endl;
    MoonshineQsvDiagnosticReport report{};
    int diag_res = moonshine_qsv_run_diagnostics(&report);
    std::cout << "  Diagnostic execution result: " << diag_res << std::endl;
    std::cout << "  1.  Intel Adapter Found:         " << (report.adapter_found ? "YES" : "NO") << std::endl;
    std::cout << "  2.  Intel Adapter Description:   " << report.adapter_description << std::endl;
    std::cout << "  3.  D3D11 Device Created:        " << (report.d3d11_device_created ? "YES" : "NO") << std::endl;
    std::cout << "  4.  D3D11 Vendor Verified:       " << (report.d3d11_vendor_verified ? "YES (0x8086)" : "NO") << std::endl;
    std::cout << "  5.  oneVPL DLL Loaded:           " << (report.vpl_dll_loaded ? "YES" : "NO") << " (" << report.vpl_dll_name << ")" << std::endl;
    std::cout << "  6.  oneVPL Config Created:       " << (report.vpl_config_created ? "YES" : "NO") << std::endl;
    std::cout << "  7.  oneVPL Impl Filter Applied:  " << (report.vpl_impl_filter_applied ? "YES" : "NO") << " (status: " << report.impl_filter_status << ")" << std::endl;
    std::cout << "  8.  oneVPL Accel Filter Applied: " << (report.vpl_accel_filter_applied ? "YES" : "NO") << " (status: " << report.accel_filter_status << ")" << std::endl;
    std::cout << "  9.  oneVPL Session Initialised:  " << (report.vpl_session_created ? "YES" : "NO") << std::endl;
    std::cout << "  10. D3D11 Handle Bound:          " << (report.d3d11_handle_bound ? "YES" : "NO") << std::endl;
    std::cout << "  11. H.264 Session Capability:    " << (report.h264_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  12. HEVC Session Capability:     " << (report.hevc_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  13. AV1 Session Capability:      " << (report.av1_supported ? "SUPPORTED" : "UNSUPPORTED") << std::endl;
    std::cout << "  14. Encoder Configured:          " << (report.encoder_configured ? "YES" : "NO") << std::endl;
    std::cout << "  15. Known Frame Encoded:         " << (report.frame_encoded ? "YES (SMPTE 75% Colour Bars)" : "NO") << std::endl;
    std::cout << "  16. Bitstream Valid:             " << (report.bitstream_valid ? "YES" : "NO") << std::endl;
    std::cout << "  17. Decoder Created:             " << (report.decoder_created ? "YES" : "NO") << std::endl;
    std::cout << "  18. Decoder Accepted Frame:      " << (report.decoder_accepted ? "YES" : "NO") << std::endl;
    std::cout << "  19. Decoded Texture Available:   " << (report.decoded_texture_available ? "YES" : "NO") << std::endl;
    std::cout << "  20. Decoder Loopback Passed:     " << (report.decoder_loopback_passed ? "PASSED" : "FAILED/SKIPPED") << std::endl;
    std::cout << "  21. Legacy MFX Fallback Used:    " << (report.legacy_mfx_fallback_used ? "YES" : "NO (Pure Modern oneVPL)") << std::endl;
    std::cout << "  22. First Failed Stage:          " << (report.first_failed_stage[0] != '\0' ? report.first_failed_stage : "NONE") << std::endl;
    std::cout << "  23. Last MFX Status:             " << report.last_mfx_status << std::endl;
    std::cout << "  24. Last HRESULT:                0x" << std::hex << report.last_hresult << std::dec << std::endl;

    if (found_intel) {
        REQUIRE(report.adapter_found == 1);
        REQUIRE(report.adapter_device_id != 0);
        REQUIRE(report.d3d11_device_created == 1);
        REQUIRE(report.d3d11_vendor_verified == 1);
    }

    std::cout << "\n[+] GPU Adapter Inventory and Diagnostic suite passed successfully." << std::endl;
    return 0;
}
