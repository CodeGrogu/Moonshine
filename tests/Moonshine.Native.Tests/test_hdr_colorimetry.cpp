#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <cstdlib>

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

int main() {
    std::cout << "[*] Running Native HDR10 & Colorimetry Tests..." << std::endl;

    // 1. Test C-ABI HDR Metadata Extraction
    {
        MoonshineHdr10Metadata cAbiMeta = {};
        int res = moonshine_hdr_extract_metadata(nullptr, &cAbiMeta);
        std::cout << "    [+] C-ABI HDR Extract result: " << res << std::endl;
        std::cout << "    [+] HDR Enabled: " << (cAbiMeta.hdr_enabled ? "YES" : "NO (SDR Mode)") << std::endl;
        std::cout << "    [+] Color Space: " << (cAbiMeta.color_space == 1 ? "BT.2020" : "BT.709") << std::endl;
    }

    // 2. Test C-ABI HDR Capabilities Parse
    {
        MoonshineHdr10Metadata cAbiCaps = {};
        int parseRes = moonshine_hdr_parse_capabilities(12, &cAbiCaps);
        (void)parseRes;
        TEST_ASSERT(parseRes == 1);
        TEST_ASSERT(cAbiCaps.hdr_enabled == 1);
        TEST_ASSERT(cAbiCaps.color_space == 1);
        TEST_ASSERT(cAbiCaps.max_mastering_luminance == 10000000);
        TEST_ASSERT(cAbiCaps.red_primary[0] == 35400);
        TEST_ASSERT(cAbiCaps.green_primary[1] == 39850);
        TEST_ASSERT(cAbiCaps.blue_primary[0] == 6550);
        TEST_ASSERT(cAbiCaps.white_point[0] == 15635);
        std::cout << "    [+] C-ABI HDR Capabilities parse verified: BT.2020 PQ" << std::endl;
    }

    // 3. Test C-ABI Color Converter Lifecycle
    {
        MoonshineColorConverterHandle convHandle = moonshine_color_converter_create(nullptr, 3840, 2160, 24, 104);
        if (convHandle) {
            std::cout << "    [+] C-ABI Color Converter created: 3840x2160 RGB10A2 -> P010" << std::endl;
            int convRes = moonshine_color_converter_convert(convHandle, nullptr, nullptr);
            (void)convRes;
            TEST_ASSERT(convRes == -1); // Null textures rejected safely
            moonshine_color_converter_destroy(convHandle);
        } else {
            std::cout << "    [+] C-ABI Color Converter not available on headless environment" << std::endl;
        }
    }

    std::cout << "[+] Native HDR10 & Colorimetry Tests Passed Successfully!" << std::endl;
    return 0;
}
