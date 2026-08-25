#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <moonshine/encoder/video_encoder_interface.hpp>

namespace moonshine::bitstream {

/// Result structure of an access unit structural bitstream validation check.
struct BitstreamValidationResult {
    bool is_valid{false};
    bool has_structurally_valid_payload{false};
    bool has_codec_headers{false};
    bool has_random_access_marker{false};
    bool contains_frame_data{false};
    bool is_complete_access_unit{false};
    int32_t nalu_count{0};
    bool has_parameter_sets{false};
    bool has_idr{false};
    bool has_random_access_point{false};
    uint32_t profile_idc{0};
    uint32_t level_idc{0};
    bool has_aud{false};
    bool has_cra{false};
    bool has_vps{false};
    bool has_sps{false};
    bool has_pps{false};
};

/// Decodes an unsigned LEB128 variable-length integer up to 8 bytes per AV1 specification Section 4.10.5.
inline bool decode_leb128(const uint8_t* data, size_t size, uint64_t& value, size_t& bytes_read) noexcept {
    value = 0;
    bytes_read = 0;
    int shift = 0;

    for (size_t i = 0; i < 8 && i < size; ++i) {
        uint8_t b = data[i];
        bytes_read++;
        value |= static_cast<uint64_t>(b & 0x7F) << shift;
        if ((b & 0x80) == 0) {
            return true;
        }
        shift += 7;
    }

    return false;
}

/// Inspects AV1 uncompressed frame header to determine keyframe / intra-only status.
inline void inspect_av1_frame_header(const uint8_t* payload, size_t size, bool& has_key_frame, bool& has_intra_only_frame) noexcept {
    if (size == 0 || payload == nullptr) {
        return;
    }
    uint8_t first_byte = payload[0];
    int show_existing_frame = (first_byte >> 7) & 0x01;
    if (show_existing_frame == 0) {
        int frame_type = (first_byte >> 5) & 0x03;
        if (frame_type == 0) {
            has_key_frame = true;
        } else if (frame_type == 2) {
            has_intra_only_frame = true;
        }
    }
}

/// Validates an AV1 access unit containing one or more standard Open Bitstream Units (OBUs).
inline BitstreamValidationResult validate_av1(const uint8_t* data, size_t size) noexcept {
    BitstreamValidationResult res{};
    if (data == nullptr || size < 2) {
        return res;
    }

    size_t offset = 0;
    bool found_valid_obu = false;
    bool has_seq_header = false;
    bool has_frame_header = false;
    bool has_tile_group = false;
    bool has_frame = false;
    bool has_key_frame = false;
    bool has_intra_only_frame = false;
    bool has_temporal_delimiter = false;
    int32_t obu_count = 0;

    while (offset < size) {
        uint8_t header = data[offset++];
        // Forbidden bit MUST be 0
        if ((header & 0x80) != 0) {
            return res;
        }

        uint8_t obu_type = (header >> 3) & 0x0F;
        // Valid standard OBU types: 1..8 and 15 (Padding)
        if (obu_type < 1 || (obu_type > 8 && obu_type != 15)) {
            return res;
        }

        bool extension_flag = ((header >> 2) & 0x01) != 0;
        bool has_size_field = ((header >> 1) & 0x01) != 0;

        if (extension_flag) {
            if (offset >= size) {
                return res;
            }
            offset++;
        }

        const uint8_t* payload = nullptr;
        size_t payload_size = 0;

        if (has_size_field) {
            uint64_t obu_size = 0;
            size_t leb_bytes = 0;
            if (!decode_leb128(data + offset, size - offset, obu_size, leb_bytes)) {
                return res;
            }
            offset += leb_bytes;

            if (offset + obu_size > size) {
                return res;
            }

            payload = data + offset;
            payload_size = static_cast<size_t>(obu_size);
            offset += static_cast<size_t>(obu_size);
        } else {
            payload = data + offset;
            payload_size = size - offset;
            offset = size;
        }

        found_valid_obu = true;
        obu_count++;

        switch (obu_type) {
            case 1: // Sequence Header
                has_seq_header = true;
                break;
            case 2: // Temporal Delimiter
                has_temporal_delimiter = true;
                break;
            case 3: // Frame Header
                has_frame_header = true;
                inspect_av1_frame_header(payload, payload_size, has_key_frame, has_intra_only_frame);
                break;
            case 4: // Tile Group
                has_tile_group = true;
                break;
            case 5: // Metadata
                break;
            case 6: // Frame
                has_frame = true;
                inspect_av1_frame_header(payload, payload_size, has_key_frame, has_intra_only_frame);
                break;
            case 7: // Redundant Frame Header
                break;
            case 8: // Tile List
                break;
            case 15: // Padding
                break;
            default:
                break;
        }
    }

    if (!found_valid_obu || obu_count == 0) {
        return res;
    }

    bool has_codec_headers = has_seq_header;
    bool has_random_access_marker = has_seq_header || has_key_frame || has_intra_only_frame;
    bool contains_frame_data = has_frame || (has_frame_header && has_tile_group);
    bool is_complete_access_unit = has_codec_headers && contains_frame_data;

    res.is_valid = true;
    res.has_structurally_valid_payload = true;
    res.has_codec_headers = has_codec_headers;
    res.has_random_access_marker = has_random_access_marker;
    res.contains_frame_data = contains_frame_data;
    res.is_complete_access_unit = is_complete_access_unit;
    res.nalu_count = obu_count;
    res.has_parameter_sets = has_codec_headers;
    res.has_idr = has_seq_header || has_key_frame;
    res.has_random_access_point = has_random_access_marker;
    res.has_aud = has_temporal_delimiter;
    res.has_sps = has_seq_header;

    return res;
}

/// Helper function to process an individual H.264 NAL unit payload.
inline bool process_h264_nalu(
    const uint8_t* nalu_data,
    size_t nalu_size,
    bool& has_sps,
    bool& has_pps,
    bool& has_idr,
    bool& has_non_idr,
    bool& has_aud,
    uint32_t& profile_idc,
    uint32_t& level_idc
) noexcept {
    if (nalu_data == nullptr || nalu_size == 0) {
        return false;
    }

    uint8_t header = nalu_data[0];
    if ((header & 0x80) != 0) {
        return false;
    }

    uint8_t nal_ref_idc = (header >> 5) & 0x03;
    uint8_t nal_unit_type = header & 0x1F;

    if (nal_unit_type == 0 || nal_unit_type > 23) {
        return false;
    }

    const uint8_t* rbsp = nalu_data + 1;
    size_t rbsp_size = nalu_size - 1;

    if (nal_unit_type == 7) { // SPS
        has_sps = true;
        if (rbsp_size >= 3) {
            profile_idc = rbsp[0];
            level_idc = rbsp[2];
            if (level_idc == 0) {
                return false;
            }
        }
    } else if (nal_unit_type == 8) { // PPS
        has_pps = true;
    } else if (nal_unit_type == 5) { // IDR
        if (nal_ref_idc == 0) {
            return false;
        }
        has_idr = true;
    } else if (nal_unit_type == 1 || (nal_unit_type >= 2 && nal_unit_type <= 4)) {
        has_non_idr = true;
    } else if (nal_unit_type == 9) { // AUD
        has_aud = true;
    }

    return true;
}

/// Validates an H.264 Annex B access unit.
inline BitstreamValidationResult validate_h264(const uint8_t* data, size_t size) noexcept {
    BitstreamValidationResult res{};
    if (data == nullptr || size < 4) {
        return res;
    }

    bool has_sps = false;
    bool has_pps = false;
    bool has_idr = false;
    bool has_non_idr = false;
    bool has_aud = false;
    uint32_t profile_idc = 0;
    uint32_t level_idc = 0;
    int32_t nalu_count = 0;

    size_t current_offset = 0;
    int64_t nalu_start = -1;

    while (current_offset + 2 < size) {
        size_t sc_len = 0;
        if (data[current_offset] == 0 && data[current_offset + 1] == 0) {
            if (data[current_offset + 2] == 1) {
                sc_len = 3;
            } else if (current_offset + 3 < size && data[current_offset + 2] == 0 && data[current_offset + 3] == 1) {
                sc_len = 4;
            }
        }

        if (sc_len > 0) {
            if (nalu_start < 0) {
                for (size_t z = 0; z < current_offset; ++z) {
                    if (data[z] != 0) {
                        return res;
                    }
                }
            } else {
                size_t nal_payload_start = static_cast<size_t>(nalu_start);
                size_t nal_payload_end = current_offset;

                if (nal_payload_end > nal_payload_start) {
                    if (!process_h264_nalu(data + nal_payload_start, nal_payload_end - nal_payload_start, has_sps, has_pps, has_idr, has_non_idr, has_aud, profile_idc, level_idc)) {
                        return res;
                    }
                    nalu_count++;
                }
            }

            nalu_start = static_cast<int64_t>(current_offset + sc_len);
            current_offset += sc_len;
        } else {
            current_offset++;
        }
    }

    if (nalu_start >= 0) {
        size_t nal_payload_start = static_cast<size_t>(nalu_start);
        size_t nal_payload_end = size;

        if (nal_payload_end > nal_payload_start) {
            if (!process_h264_nalu(data + nal_payload_start, nal_payload_end - nal_payload_start, has_sps, has_pps, has_idr, has_non_idr, has_aud, profile_idc, level_idc)) {
                return res;
            }
            nalu_count++;
        }
    }

    if (nalu_count == 0) {
        return res;
    }

    bool has_codec_headers = has_sps || has_pps;
    bool has_random_access_marker = has_idr || has_sps;
    bool contains_frame_data = has_idr || has_non_idr;
    bool is_complete_access_unit = has_codec_headers && contains_frame_data;

    res.is_valid = true;
    res.has_structurally_valid_payload = true;
    res.has_codec_headers = has_codec_headers;
    res.has_random_access_marker = has_random_access_marker;
    res.contains_frame_data = contains_frame_data;
    res.is_complete_access_unit = is_complete_access_unit;
    res.nalu_count = nalu_count;
    res.has_parameter_sets = has_codec_headers;
    res.has_idr = has_idr;
    res.has_random_access_point = has_random_access_marker;
    res.profile_idc = profile_idc;
    res.level_idc = level_idc;
    res.has_aud = has_aud;
    res.has_sps = has_sps;
    res.has_pps = has_pps;

    return res;
}

/// Helper function to process an individual HEVC NAL unit payload.
inline bool process_hevc_nalu(
    const uint8_t* nalu_data,
    size_t nalu_size,
    bool& has_vps,
    bool& has_sps,
    bool& has_pps,
    bool& has_idr,
    bool& has_cra,
    bool& has_bla,
    bool& has_trail,
    bool& has_aud
) noexcept {
    if (nalu_data == nullptr || nalu_size < 2) {
        return false;
    }

    uint8_t header0 = nalu_data[0];
    uint8_t header1 = nalu_data[1];

    if ((header0 & 0x80) != 0) {
        return false;
    }

    uint8_t nal_unit_type = (header0 >> 1) & 0x3F;
    uint8_t nuh_temporal_id_plus1 = header1 & 0x07;

    if (nuh_temporal_id_plus1 == 0) {
        return false;
    }
    if (nal_unit_type > 63) {
        return false;
    }

    if (nal_unit_type == 32) { // VPS
        has_vps = true;
    } else if (nal_unit_type == 33) { // SPS
        has_sps = true;
    } else if (nal_unit_type == 34) { // PPS
        has_pps = true;
    } else if (nal_unit_type == 35) { // AUD
        has_aud = true;
    } else if (nal_unit_type == 19 || nal_unit_type == 20) { // IDR
        has_idr = true;
    } else if (nal_unit_type == 21) { // CRA
        has_cra = true;
    } else if (nal_unit_type >= 16 && nal_unit_type <= 18) { // BLA
        has_bla = true;
    } else if (nal_unit_type <= 3 || (nal_unit_type >= 4 && nal_unit_type <= 9)) {
        has_trail = true;
    }

    return true;
}

/// Validates an HEVC Annex B access unit.
inline BitstreamValidationResult validate_hevc(const uint8_t* data, size_t size) noexcept {
    BitstreamValidationResult res{};
    if (data == nullptr || size < 5) {
        return res;
    }

    int32_t nalu_count = 0;
    bool has_vps = false;
    bool has_sps = false;
    bool has_pps = false;
    bool has_idr = false;
    bool has_cra = false;
    bool has_bla = false;
    bool has_trail = false;
    bool has_aud = false;

    size_t current_offset = 0;
    int64_t nalu_start = -1;

    while (current_offset + 2 < size) {
        size_t sc_len = 0;
        if (data[current_offset] == 0 && data[current_offset + 1] == 0) {
            if (data[current_offset + 2] == 1) {
                sc_len = 3;
            } else if (current_offset + 3 < size && data[current_offset + 2] == 0 && data[current_offset + 3] == 1) {
                sc_len = 4;
            }
        }

        if (sc_len > 0) {
            if (nalu_start < 0) {
                for (size_t z = 0; z < current_offset; ++z) {
                    if (data[z] != 0) {
                        return res;
                    }
                }
            } else {
                size_t nal_payload_start = static_cast<size_t>(nalu_start);
                size_t nal_payload_end = current_offset;

                if (nal_payload_end > nal_payload_start) {
                    if (!process_hevc_nalu(data + nal_payload_start, nal_payload_end - nal_payload_start, has_vps, has_sps, has_pps, has_idr, has_cra, has_bla, has_trail, has_aud)) {
                        return res;
                    }
                    nalu_count++;
                }
            }

            nalu_start = static_cast<int64_t>(current_offset + sc_len);
            current_offset += sc_len;
        } else {
            current_offset++;
        }
    }

    if (nalu_start >= 0) {
        size_t nal_payload_start = static_cast<size_t>(nalu_start);
        size_t nal_payload_end = size;

        if (nal_payload_end > nal_payload_start) {
            if (!process_hevc_nalu(data + nal_payload_start, nal_payload_end - nal_payload_start, has_vps, has_sps, has_pps, has_idr, has_cra, has_bla, has_trail, has_aud)) {
                return res;
            }
            nalu_count++;
        }
    }

    if (nalu_count == 0) {
        return res;
    }

    bool has_codec_headers = has_vps || has_sps || has_pps;
    bool has_random_access_marker = has_idr || has_cra || has_bla;
    bool contains_frame_data = has_idr || has_cra || has_bla || has_trail;
    bool is_complete_access_unit = has_codec_headers && contains_frame_data;

    res.is_valid = true;
    res.has_structurally_valid_payload = true;
    res.has_codec_headers = has_codec_headers;
    res.has_random_access_marker = has_random_access_marker;
    res.contains_frame_data = contains_frame_data;
    res.is_complete_access_unit = is_complete_access_unit;
    res.nalu_count = nalu_count;
    res.has_parameter_sets = has_codec_headers;
    res.has_idr = has_idr;
    res.has_random_access_point = has_random_access_marker;
    res.has_aud = has_aud;
    res.has_cra = has_cra;
    res.has_vps = has_vps;
    res.has_sps = has_sps;
    res.has_pps = has_pps;

    return res;
}

/// Polymorphic bitstream validation dispatch for H.264, HEVC, and AV1 bitstreams.
inline BitstreamValidationResult validate_access_unit(moonshine::encoder::VideoCodec codec, const uint8_t* data, size_t size) noexcept {
    if (data == nullptr || size == 0) {
        return BitstreamValidationResult{};
    }

    switch (codec) {
        case moonshine::encoder::VideoCodec::H264:
            return validate_h264(data, size);
        case moonshine::encoder::VideoCodec::Hevc:
        case moonshine::encoder::VideoCodec::HevcMain10:
            return validate_hevc(data, size);
        case moonshine::encoder::VideoCodec::Av1:
            return validate_av1(data, size);
        default:
            return BitstreamValidationResult{};
    }
}

/// Convenience function returning whether bitstream is valid and whether it is a random-access keyframe.
inline bool validate_bitstream(moonshine::encoder::VideoCodec codec, const uint8_t* data, size_t size, bool& is_keyframe) noexcept {
    is_keyframe = false;
    if (data == nullptr || size == 0) {
        return false;
    }

    auto res = validate_access_unit(codec, data, size);
    is_keyframe = res.has_codec_headers || res.has_random_access_marker || res.has_parameter_sets || res.has_idr || res.has_random_access_point;
    return res.is_valid;
}

} // namespace moonshine::bitstream
