#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include <cstring>
#include <chrono>

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder {

NvencVideoEncoder::NvencVideoEncoder() = default;

NvencVideoEncoder::~NvencVideoEncoder() {
    cleanup();
}

bool NvencVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();
    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;

    // Cache simulated NAL parameter sets according to codec
    _header_cache.clear();
    if (_config.codec == static_cast<uint32_t>(VideoCodec::H264)) {
        // H.264 SPS / PPS Annex B prefix (0x00, 0x00, 0x00, 0x01, 0x67..., 0x00, 0x00, 0x00, 0x01, 0x68...)
        uint8_t h264_sps_pps[] = {
            0x00, 0x00, 0x00, 0x01, 0x67, 0x64, 0x00, 0x28, 0xAC, 0xD9, 0x40, 0x78, 0x02, 0x27, 0xE5, 0x84,
            0x00, 0x00, 0x00, 0x01, 0x68, 0xEB, 0xE3, 0xCB, 0x22, 0xC0
        };
        _header_cache.assign(std::begin(h264_sps_pps), std::end(h264_sps_pps));
    } else if (_config.codec == static_cast<uint32_t>(VideoCodec::Hevc) ||
               _config.codec == static_cast<uint32_t>(VideoCodec::HevcMain10)) {
        // HEVC VPS / SPS / PPS Annex B prefix (0x40, 0x42, 0x44)
        uint8_t hevc_headers[] = {
            0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, 0xFF, 0xFF, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
            0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF7, 0xC0, 0xCC, 0x90
        };
        _header_cache.assign(std::begin(hevc_headers), std::end(hevc_headers));
    } else if (_config.codec == static_cast<uint32_t>(VideoCodec::Av1)) {
        // AV1 Sequence Header OBU (0x12)
        uint8_t av1_headers[] = {
            0x0A, 0x0E, 0x00, 0x00, 0x00, 0x24, 0xC7, 0xAB, 0xBF, 0xF3, 0x00, 0x10, 0x00, 0x00
        };
        _header_cache.assign(std::begin(av1_headers), std::end(av1_headers));
    }

    _initialized = true;
    return true;
}

bool NvencVideoEncoder::encode_frame(
    void* /*d3d_texture*/,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    if (!_initialized || !out_bitstream || max_buffer_size < 128) {
        return false;
    }

    bool is_key = force_idr || _force_keyframe.exchange(false) || (_frame_counter == 0);
    uint32_t header_len = is_key ? static_cast<uint32_t>(_header_cache.size()) : 0;

    // Simulated compressed slice data size based on bitrate / fps
    uint32_t target_slice_bytes = (_config.bitrate_kbps * 1000) / (_config.fps * 8);
    if (target_slice_bytes < 64) target_slice_bytes = 64;
    if (is_key) target_slice_bytes = target_slice_bytes * 3 / 2; // Keyframes ~1.5x average frame

    uint32_t total_payload = header_len + target_slice_bytes;
    if (total_payload > max_buffer_size) {
        total_payload = max_buffer_size;
    }

    // Write NAL headers if keyframe
    if (header_len > 0 && header_len <= total_payload) {
        std::memcpy(out_bitstream, _header_cache.data(), header_len);
    }

    // Write NAL slice payload
    uint32_t slice_offset = header_len;
    if (slice_offset + 4 <= total_payload) {
        // Annex B start code for slice
        out_bitstream[slice_offset] = 0x00;
        out_bitstream[slice_offset + 1] = 0x00;
        out_bitstream[slice_offset + 2] = 0x00;
        out_bitstream[slice_offset + 3] = 0x01;
        slice_offset += 4;
    }

    // Set slice header byte based on codec and keyframe state
    if (slice_offset < total_payload) {
        if (_config.codec == static_cast<uint32_t>(VideoCodec::H264)) {
            out_bitstream[slice_offset] = is_key ? 0x65 : 0x41; // IDR vs non-IDR slice
        } else if (_config.codec == static_cast<uint32_t>(VideoCodec::Hevc) ||
                   _config.codec == static_cast<uint32_t>(VideoCodec::HevcMain10)) {
            out_bitstream[slice_offset] = is_key ? 0x26 : 0x02; // IDR_W_RADL vs TRAIL_R
        } else {
            out_bitstream[slice_offset] = 0x30; // AV1 Frame Header OBU
        }
        slice_offset++;
    }

    // Fill remaining bytes deterministically
    if (slice_offset < total_payload) {
        std::memset(out_bitstream + slice_offset, 0xAA, total_payload - slice_offset);
    }

    out_written_size = total_payload;

    out_desc.frame_index = _frame_counter++;
    auto now_ticks = std::chrono::high_resolution_clock::now().time_since_epoch().count();
    out_desc.timestamp_qpc = now_ticks;
    out_desc.payload_size = out_written_size;
    out_desc.is_keyframe = is_key ? 1 : 0;
    out_desc.is_header_packet = (header_len > 0) ? 1 : 0;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    return true;
}

bool NvencVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config.bitrate_kbps = new_config.bitrate_kbps;
    _config.peak_bitrate_kbps = new_config.peak_bitrate_kbps;
    _config.fps = new_config.fps;
    return true;
}

void NvencVideoEncoder::request_keyframe() {
    _force_keyframe.store(true);
}

void NvencVideoEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _header_cache.clear();
}

bool NvencVideoEncoder::query_capabilities(void* /*d3d_device*/, EncoderCaps& out_caps) {
    out_caps = {};
    out_caps.supported_codecs_mask = 0x0F; // H264 | HEVC | HEVC Main10 | AV1
    out_caps.max_width = 8192;
    out_caps.max_height = 8192;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = 1;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::NvidiaNvenc);
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 200000;
    return true;
}

} // namespace moonshine::encoder
