#include <iostream>
#include <vector>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <moonshine/bitstream/bitstream_parser.hpp>

#define ACTIVE_ASSERT(expr) \
    do { \
        if (!(expr)) { \
            std::cerr << "[-] Assertion failed: (" #expr ") at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::abort(); \
        } \
    } while (0)

using namespace moonshine::bitstream;

void test_leb128_decoding() {
    std::cout << "[*] Running test_leb128_decoding..." << std::endl;

    // 1-byte LEB128: 0
    uint8_t leb0[] = {0x00};
    uint64_t val = 0;
    size_t read = 0;
    ACTIVE_ASSERT(decode_leb128(leb0, sizeof(leb0), val, read));
    ACTIVE_ASSERT(val == 0);
    ACTIVE_ASSERT(read == 1);

    // 1-byte LEB128: 127
    uint8_t leb127[] = {0x7F};
    ACTIVE_ASSERT(decode_leb128(leb127, sizeof(leb127), val, read));
    ACTIVE_ASSERT(val == 127);
    ACTIVE_ASSERT(read == 1);

    // 2-byte LEB128: 128
    uint8_t leb128_bytes[] = {0x80, 0x01};
    ACTIVE_ASSERT(decode_leb128(leb128_bytes, sizeof(leb128_bytes), val, read));
    ACTIVE_ASSERT(val == 128);
    ACTIVE_ASSERT(read == 2);

    // 2-byte LEB128: 16383
    uint8_t leb16383[] = {0xFF, 0x7F};
    ACTIVE_ASSERT(decode_leb128(leb16383, sizeof(leb16383), val, read));
    ACTIVE_ASSERT(val == 16383);
    ACTIVE_ASSERT(read == 2);

    // 3-byte LEB128: 16384
    uint8_t leb16384[] = {0x80, 0x80, 0x01};
    ACTIVE_ASSERT(decode_leb128(leb16384, sizeof(leb16384), val, read));
    ACTIVE_ASSERT(val == 16384);
    ACTIVE_ASSERT(read == 3);

    // 4-byte LEB128: 2097152
    uint8_t leb2m[] = {0x80, 0x80, 0x80, 0x01};
    ACTIVE_ASSERT(decode_leb128(leb2m, sizeof(leb2m), val, read));
    ACTIVE_ASSERT(val == 2097152);
    ACTIVE_ASSERT(read == 4);

    // 8-byte LEB128: valid max length
    uint8_t leb8[] = {0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01};
    ACTIVE_ASSERT(decode_leb128(leb8, sizeof(leb8), val, read));
    ACTIVE_ASSERT(read == 8);
    ACTIVE_ASSERT(val > 0);

    // Invalid: > 8 bytes without stop bit
    uint8_t leb_over8[] = {0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80};
    ACTIVE_ASSERT(!decode_leb128(leb_over8, sizeof(leb_over8), val, read));

    // Truncated LEB128: ends with MSB=1
    uint8_t leb_truncated[] = {0x80, 0x80};
    ACTIVE_ASSERT(!decode_leb128(leb_truncated, sizeof(leb_truncated), val, read));

    // Empty buffer
    ACTIVE_ASSERT(!decode_leb128(nullptr, 0, val, read));

    std::cout << "[+] test_leb128_decoding passed." << std::endl;
}

void test_av1_obu_parsing() {
    std::cout << "[*] Running test_av1_obu_parsing..." << std::endl;

    // Sequence Header (OBU 1): header = (1 << 3) | 0x02 = 0x0A, size = 2
    uint8_t seq_header[] = {0x0A, 0x02, 0x11, 0x22};
    auto seq_res = validate_av1(seq_header, sizeof(seq_header));
    ACTIVE_ASSERT(seq_res.is_valid);
    ACTIVE_ASSERT(seq_res.has_codec_headers);
    ACTIVE_ASSERT(seq_res.has_random_access_marker);
    ACTIVE_ASSERT(seq_res.nalu_count == 1);

    // Frame (OBU 6): KeyFrame (frame_type = 0)
    uint8_t key_frame[] = {0x32, 0x02, 0x00, 0x00};
    auto key_res = validate_av1(key_frame, sizeof(key_frame));
    ACTIVE_ASSERT(key_res.is_valid);
    ACTIVE_ASSERT(key_res.has_random_access_marker);
    ACTIVE_ASSERT(key_res.contains_frame_data);

    // Frame (OBU 6): IntraOnly (frame_type = 2: 0x40)
    uint8_t intra_frame[] = {0x32, 0x02, 0x40, 0x00};
    auto intra_res = validate_av1(intra_frame, sizeof(intra_frame));
    ACTIVE_ASSERT(intra_res.is_valid);
    ACTIVE_ASSERT(intra_res.has_random_access_marker);

    // Frame (OBU 6): InterFrame (frame_type = 1: 0x20)
    uint8_t inter_frame[] = {0x32, 0x02, 0x20, 0x00};
    auto inter_res = validate_av1(inter_frame, sizeof(inter_frame));
    ACTIVE_ASSERT(inter_res.is_valid);
    ACTIVE_ASSERT(!inter_res.has_random_access_marker);

    // Temporal Delimiter (OBU 2)
    uint8_t td[] = {0x12, 0x00};
    auto td_res = validate_av1(td, sizeof(td));
    ACTIVE_ASSERT(td_res.is_valid);
    ACTIVE_ASSERT(td_res.has_aud);

    // Padding OBU (15)
    uint8_t pad[] = {static_cast<uint8_t>((15 << 3) | 0x02), 0x02, 0x00, 0x00};
    auto pad_res = validate_av1(pad, sizeof(pad));
    ACTIVE_ASSERT(pad_res.is_valid);

    // Forbidden bit set
    uint8_t forbidden[] = {0x8A, 0x02, 0x00, 0x00};
    ACTIVE_ASSERT(!validate_av1(forbidden, sizeof(forbidden)).is_valid);

    // Invalid reserved OBU type (0)
    uint8_t invalid_type0[] = {0x02, 0x01, 0x00};
    ACTIVE_ASSERT(!validate_av1(invalid_type0, sizeof(invalid_type0)).is_valid);

    // Invalid reserved OBU type (9)
    uint8_t invalid_type9[] = {0x4A, 0x01, 0x00};
    ACTIVE_ASSERT(!validate_av1(invalid_type9, sizeof(invalid_type9)).is_valid);

    // Truncated / payload overflow
    uint8_t overflow[] = {0x0A, 0x64, 0x00, 0x00};
    ACTIVE_ASSERT(!validate_av1(overflow, sizeof(overflow)).is_valid);

    std::cout << "[+] test_av1_obu_parsing passed." << std::endl;
}

void test_h264_nalu_parsing() {
    std::cout << "[*] Running test_h264_nalu_parsing..." << std::endl;

    // SPS + PPS + IDR access unit
    uint8_t h264_keyframe[] = {
        0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28, // SPS (profile 0x42, level 0x28)
        0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x38, 0x80, // PPS
        0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00        // IDR
    };
    auto res = validate_h264(h264_keyframe, sizeof(h264_keyframe));
    ACTIVE_ASSERT(res.is_valid);
    ACTIVE_ASSERT(res.has_codec_headers);
    ACTIVE_ASSERT(res.has_random_access_marker);
    ACTIVE_ASSERT(res.contains_frame_data);
    ACTIVE_ASSERT(res.is_complete_access_unit);
    ACTIVE_ASSERT(res.nalu_count == 3);
    ACTIVE_ASSERT(res.profile_idc == 0x42);
    ACTIVE_ASSERT(res.level_idc == 0x28);
    ACTIVE_ASSERT(res.has_sps);
    ACTIVE_ASSERT(res.has_pps);
    ACTIVE_ASSERT(res.has_idr);

    // AUD (type 9)
    uint8_t h264_aud[] = {0x00, 0x00, 0x00, 0x01, 0x09, 0x10};
    auto aud_res = validate_h264(h264_aud, sizeof(h264_aud));
    ACTIVE_ASSERT(aud_res.is_valid);
    ACTIVE_ASSERT(aud_res.has_aud);

    // IDR slice with nal_ref_idc == 0 (must be rejected)
    uint8_t invalid_idr[] = {0x00, 0x00, 0x00, 0x01, 0x05, 0x88, 0x84};
    ACTIVE_ASSERT(!validate_h264(invalid_idr, sizeof(invalid_idr)).is_valid);

    // SPS with level_idc == 0 (must be rejected)
    uint8_t invalid_sps[] = {0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x00};
    ACTIVE_ASSERT(!validate_h264(invalid_sps, sizeof(invalid_sps)).is_valid);

    // Forbidden zero bit set
    uint8_t forbidden[] = {0x00, 0x00, 0x00, 0x01, 0xE7, 0x42, 0xC0, 0x28};
    ACTIVE_ASSERT(!validate_h264(forbidden, sizeof(forbidden)).is_valid);

    // Corrupted non-zero prefix
    uint8_t corrupt_prefix[] = {0xAA, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0xC0, 0x28};
    ACTIVE_ASSERT(!validate_h264(corrupt_prefix, sizeof(corrupt_prefix)).is_valid);

    std::cout << "[+] test_h264_nalu_parsing passed." << std::endl;
}

void test_hevc_nalu_parsing() {
    std::cout << "[*] Running test_hevc_nalu_parsing..." << std::endl;

    // VPS + SPS + PPS + IDR access unit
    uint8_t hevc_keyframe[] = {
        0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, // VPS (32)
        0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01, 0x01, // SPS (33)
        0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xC0, 0xF0, // PPS (34)
        0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAF, 0xFE  // IDR (19)
    };
    auto res = validate_hevc(hevc_keyframe, sizeof(hevc_keyframe));
    ACTIVE_ASSERT(res.is_valid);
    ACTIVE_ASSERT(res.has_codec_headers);
    ACTIVE_ASSERT(res.has_random_access_marker);
    ACTIVE_ASSERT(res.contains_frame_data);
    ACTIVE_ASSERT(res.is_complete_access_unit);
    ACTIVE_ASSERT(res.nalu_count == 4);
    ACTIVE_ASSERT(res.has_vps);
    ACTIVE_ASSERT(res.has_sps);
    ACTIVE_ASSERT(res.has_pps);
    ACTIVE_ASSERT(res.has_idr);

    // CRA (21)
    uint8_t hevc_cra[] = {0x00, 0x00, 0x00, 0x01, 0x2A, 0x01, 0x11, 0x22};
    auto cra_res = validate_hevc(hevc_cra, sizeof(hevc_cra));
    ACTIVE_ASSERT(cra_res.is_valid);
    ACTIVE_ASSERT(cra_res.has_cra);
    ACTIVE_ASSERT(cra_res.has_random_access_point);

    // AUD (35)
    uint8_t hevc_aud[] = {0x00, 0x00, 0x00, 0x01, 0x46, 0x01, 0x10};
    auto aud_res = validate_hevc(hevc_aud, sizeof(hevc_aud));
    ACTIVE_ASSERT(aud_res.is_valid);
    ACTIVE_ASSERT(aud_res.has_aud);

    // Temporal ID Plus 1 == 0 (must be rejected)
    uint8_t invalid_temporal[] = {0x00, 0x00, 0x00, 0x01, 0x26, 0x00, 0xAF, 0xFE};
    ACTIVE_ASSERT(!validate_hevc(invalid_temporal, sizeof(invalid_temporal)).is_valid);

    // Forbidden zero bit set
    uint8_t forbidden[] = {0x00, 0x00, 0x00, 0x01, 0xA6, 0x01, 0xAF, 0xFE};
    ACTIVE_ASSERT(!validate_hevc(forbidden, sizeof(forbidden)).is_valid);

    // Corrupted non-zero prefix
    uint8_t corrupt_prefix[] = {0xAA, 0xBB, 0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01};
    ACTIVE_ASSERT(!validate_hevc(corrupt_prefix, sizeof(corrupt_prefix)).is_valid);

    std::cout << "[+] test_hevc_nalu_parsing passed." << std::endl;
}

int main() {
    std::cout << "=== Moonshine Native Bitstream Parser Conformance Suite ===" << std::endl;
    test_leb128_decoding();
    test_av1_obu_parsing();
    test_h264_nalu_parsing();
    test_hevc_nalu_parsing();
    std::cout << "[+] ALL BITSTREAM PARSER TESTS PASSED SUCCESSFULLY!" << std::endl;
    return 0;
}
