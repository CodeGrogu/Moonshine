#pragma once

#include <cstdint>
#include <cstddef>
#include <string>

namespace moonshine::color {

struct Hdr10Metadata {
    uint16_t red_primary[2] = { 35400, 14600 };    // BT.2020 Red (0.708, 0.292)
    uint16_t green_primary[2] = { 8500, 39850 };   // BT.2020 Green (0.170, 0.797)
    uint16_t blue_primary[2] = { 6550, 2300 };     // BT.2020 Blue (0.131, 0.046)
    uint16_t white_point[2] = { 15635, 16450 };    // D65 White (0.3127, 0.3290)
    uint32_t max_mastering_luminance = 10000000;   // 1000 nits (scaled by 10000)
    uint32_t min_mastering_luminance = 10;         // 0.001 nits (scaled by 10000)
    uint16_t max_content_light_level = 1000;       // MaxCLL in nits
    uint16_t max_frame_average_light_level = 400;  // MaxFALL in nits
    uint8_t  hdr_enabled = 0;                      // 1 = HDR10 Active, 0 = SDR
    uint8_t  color_space = 0;                      // 0 = BT.709, 1 = BT.2020
    uint8_t  reserved[2] = { 0, 0 };
};

class HdrMetadataExtractor {
public:
    static bool extract_display_metadata(void* hmonitor, Hdr10Metadata& out_metadata);
    static bool parse_hdr_capabilities(uint32_t color_space_dxgi, Hdr10Metadata& out_metadata);
};

} // namespace moonshine::color
