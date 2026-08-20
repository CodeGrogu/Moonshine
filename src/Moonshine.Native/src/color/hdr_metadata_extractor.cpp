#include "moonshine/color/hdr_metadata_extractor.hpp"

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#pragma comment(lib, "dxgi.lib")
#endif

namespace moonshine::color {

bool HdrMetadataExtractor::extract_display_metadata(void* hmonitor, Hdr10Metadata& out_metadata) {
#if defined(_WIN32)
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr)) {
        return parse_hdr_capabilities(0, out_metadata);
    }

    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        ComPtr<IDXGIOutput> output;
        for (UINT j = 0; adapter->EnumOutputs(j, &output) != DXGI_ERROR_NOT_FOUND; ++j) {
            DXGI_OUTPUT_DESC outDesc = {};
            output->GetDesc(&outDesc);

            // If hmonitor provided, match it; otherwise use first attached desktop monitor
            if (hmonitor != nullptr && outDesc.Monitor != static_cast<HMONITOR>(hmonitor)) {
                continue;
            }

            ComPtr<IDXGIOutput6> output6;
            hr = output.As(&output6);
            if (SUCCEEDED(hr)) {
                DXGI_OUTPUT_DESC1 desc1 = {};
                hr = output6->GetDesc1(&desc1);
                if (SUCCEEDED(hr)) {
                    if (desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                        desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020 ||
                        desc1.ColorSpace == DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_TOPLEFT_P2020) {
                        out_metadata.hdr_enabled = 1;
                        out_metadata.color_space = 1; // BT.2020
                        out_metadata.red_primary[0] = static_cast<uint16_t>(desc1.RedPrimary[0] * 50000.0f);
                        out_metadata.red_primary[1] = static_cast<uint16_t>(desc1.RedPrimary[1] * 50000.0f);
                        out_metadata.green_primary[0] = static_cast<uint16_t>(desc1.GreenPrimary[0] * 50000.0f);
                        out_metadata.green_primary[1] = static_cast<uint16_t>(desc1.GreenPrimary[1] * 50000.0f);
                        out_metadata.blue_primary[0] = static_cast<uint16_t>(desc1.BluePrimary[0] * 50000.0f);
                        out_metadata.blue_primary[1] = static_cast<uint16_t>(desc1.BluePrimary[1] * 50000.0f);
                        out_metadata.white_point[0] = static_cast<uint16_t>(desc1.WhitePoint[0] * 50000.0f);
                        out_metadata.white_point[1] = static_cast<uint16_t>(desc1.WhitePoint[1] * 50000.0f);
                        out_metadata.max_mastering_luminance = static_cast<uint32_t>(desc1.MaxLuminance * 10000.0f);
                        out_metadata.min_mastering_luminance = static_cast<uint32_t>(desc1.MinLuminance * 10000.0f);
                        out_metadata.max_content_light_level = static_cast<uint16_t>(desc1.MaxFullFrameLuminance > 0.0f ? desc1.MaxFullFrameLuminance : desc1.MaxLuminance);
                        out_metadata.max_frame_average_light_level = static_cast<uint16_t>(desc1.MaxFullFrameLuminance > 0.0f ? desc1.MaxFullFrameLuminance : 400);
                        return true;
                    }
                }
            }

            // Fallback to SDR if output found but HDR is disabled
            out_metadata.hdr_enabled = 0;
            out_metadata.color_space = 0; // BT.709
            return true;
        }
    }

    return parse_hdr_capabilities(0, out_metadata);
#else
    (void)hmonitor;
    out_metadata.hdr_enabled = 0;
    out_metadata.color_space = 0;
    return true;
#endif
}

bool HdrMetadataExtractor::parse_hdr_capabilities(uint32_t color_space_dxgi, Hdr10Metadata& out_metadata) {
    if (color_space_dxgi == 12 || // DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020
        color_space_dxgi == 13 || // DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020
        color_space_dxgi == 14)   // DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_TOPLEFT_P2020
    {
        out_metadata.hdr_enabled = 1;
        out_metadata.color_space = 1;
        out_metadata.red_primary[0] = 35400;
        out_metadata.red_primary[1] = 14600;
        out_metadata.green_primary[0] = 8500;
        out_metadata.green_primary[1] = 39850;
        out_metadata.blue_primary[0] = 6550;
        out_metadata.blue_primary[1] = 2300;
        out_metadata.white_point[0] = 15635;
        out_metadata.white_point[1] = 16450;
        out_metadata.max_mastering_luminance = 10000000;
        out_metadata.min_mastering_luminance = 10;
        out_metadata.max_content_light_level = 1000;
        out_metadata.max_frame_average_light_level = 400;
    } else {
        out_metadata.hdr_enabled = 0;
        out_metadata.color_space = 0;
    }
    return true;
}

} // namespace moonshine::color
