#include "moonshine/protocol/moonshine_protocol.hpp"
#include <iostream>
#include <vector>
#include <array>
#include <cstdlib>

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::protocol;

static void test_struct_sizes_and_alignment() {
    std::cout << "[+] Testing protocol struct sizes and memory alignments..." << std::endl;

    static_assert(sizeof(MoonshinePacketHeader) == 32);
    static_assert(sizeof(MoonshineHelloPayload) == 32);
    static_assert(sizeof(MoonshineHelloResponsePayload) == 48);
    static_assert(sizeof(MoonshineSessionSetupPayload) == 40);
    static_assert(sizeof(MoonshineSessionSetupResponsePayload) == 32);
    static_assert(sizeof(MoonshineVideoPacketHeader) == 32);
    static_assert(sizeof(MoonshineAudioPacketHeader) == 24);
    static_assert(sizeof(MoonshineMicPacketHeader) == 20);
    static_assert(sizeof(MoonshineFeedbackLossStatsPayload) == 36);
    static_assert(sizeof(MoonshineIdrRequestPayload) == 16);
    static_assert(sizeof(MoonshineInputKeyboardPayload) == 12);
    static_assert(sizeof(MoonshineInputMousePayload) == 20);
    static_assert(sizeof(MoonshineInputGamepadPayload) == 24);
    static_assert(sizeof(MoonshineTelemetryReportPayload) == 32);
}

static void test_header_serialization_and_validation() {
    std::cout << "[+] Testing packet header big-endian serialization and validation..." << std::endl;

    MoonshinePacketHeader original{};
    original.magic = MOONSHINE_MAGIC;
    original.version = MOONSHINE_VERSION_1_0;
    original.message_type = static_cast<uint16_t>(MoonshineMessageType::VideoPacket);
    original.payload_size = 64;
    original.sequence_number = 1024;
    original.session_id = 0x0123456789ABCDEFULL;
    original.timestamp_us = 1700000000123456ULL;

    std::array<uint8_t, sizeof(MoonshinePacketHeader) + 64> buffer{};
    bool write_ok = write_header(original, buffer);
    TEST_ASSERT(write_ok);

    // Verify raw big-endian byte pattern for Magic 'MSHN' (0x4D53484E)
    TEST_ASSERT(buffer[0] == 0x4D);
    TEST_ASSERT(buffer[1] == 0x53);
    TEST_ASSERT(buffer[2] == 0x48);
    TEST_ASSERT(buffer[3] == 0x4E);

    // Verify Version 0x0001
    TEST_ASSERT(buffer[4] == 0x00);
    TEST_ASSERT(buffer[5] == 0x01);

    // Verify MessageType 0x0201 (VideoPacket)
    TEST_ASSERT(buffer[6] == 0x02);
    TEST_ASSERT(buffer[7] == 0x01);

    // Verify PayloadSize 64 (0x00000040)
    TEST_ASSERT(buffer[8] == 0x00);
    TEST_ASSERT(buffer[9] == 0x00);
    TEST_ASSERT(buffer[10] == 0x00);
    TEST_ASSERT(buffer[11] == 0x40);

    // Verify SequenceNumber 1024 (0x00000400)
    TEST_ASSERT(buffer[12] == 0x00);
    TEST_ASSERT(buffer[13] == 0x00);
    TEST_ASSERT(buffer[14] == 0x04);
    TEST_ASSERT(buffer[15] == 0x00);

    // Verify SessionId 0x0123456789ABCDEF
    TEST_ASSERT(buffer[16] == 0x01);
    TEST_ASSERT(buffer[17] == 0x23);
    TEST_ASSERT(buffer[18] == 0x45);
    TEST_ASSERT(buffer[19] == 0x67);
    TEST_ASSERT(buffer[20] == 0x89);
    TEST_ASSERT(buffer[21] == 0xAB);
    TEST_ASSERT(buffer[22] == 0xCD);
    TEST_ASSERT(buffer[23] == 0xEF);

    // Read back and assert full field identity
    MoonshinePacketHeader decoded{};
    MoonshineErrorCode read_err = read_header(buffer, decoded);
    TEST_ASSERT(read_err == MoonshineErrorCode::Success);
    TEST_ASSERT(decoded.magic == original.magic);
    TEST_ASSERT(decoded.version == original.version);
    TEST_ASSERT(decoded.message_type == original.message_type);
    TEST_ASSERT(decoded.payload_size == original.payload_size);
    TEST_ASSERT(decoded.sequence_number == original.sequence_number);
    TEST_ASSERT(decoded.session_id == original.session_id);
    TEST_ASSERT(decoded.timestamp_us == original.timestamp_us);
}

static void test_header_error_conditions() {
    std::cout << "[+] Testing header rejection on corruption, truncation, and version mismatch..." << std::endl;

    std::array<uint8_t, 32> buffer{};
    MoonshinePacketHeader hdr{};
    hdr.magic = MOONSHINE_MAGIC;
    hdr.version = MOONSHINE_VERSION_1_0;
    hdr.message_type = static_cast<uint16_t>(MoonshineMessageType::Hello);
    hdr.payload_size = 32;
    hdr.sequence_number = 1;
    hdr.session_id = 42;
    hdr.timestamp_us = 1000;

    write_header(hdr, buffer);

    // 1. Buffer too small
    MoonshinePacketHeader out_hdr{};
    TEST_ASSERT(read_header(std::span<const uint8_t>(buffer.data(), 16), out_hdr) == MoonshineErrorCode::BufferTooSmall);

    // 2. Payload truncated (declared 32 bytes payload but only 32 total bytes supplied)
    TEST_ASSERT(read_header(std::span<const uint8_t>(buffer.data(), 32), out_hdr) == MoonshineErrorCode::PayloadTruncated);

    // 3. Invalid Magic
    buffer[0] = 0xFF;
    std::array<uint8_t, 64> full_buffer{};
    std::memcpy(full_buffer.data(), buffer.data(), 32);
    TEST_ASSERT(read_header(full_buffer, out_hdr) == MoonshineErrorCode::InvalidMagic);

    // 4. Unsupported Version
    write_header(hdr, buffer);
    buffer[5] = 0x99; // Version 0x0099
    std::memcpy(full_buffer.data(), buffer.data(), 32);
    TEST_ASSERT(read_header(full_buffer, out_hdr) == MoonshineErrorCode::UnsupportedVersion);
}

static void test_all_message_family_types() {
    std::cout << "[+] Testing all message family enum codes..." << std::endl;

    static_assert(static_cast<uint16_t>(MoonshineMessageType::Hello) == 0x0101);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::HelloResponse) == 0x0102);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::SessionSetup) == 0x0103);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::SessionSetupResponse) == 0x0104);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::KeepAlive) == 0x0105);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::KeepAliveAck) == 0x0106);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::Teardown) == 0x0107);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::VideoPacket) == 0x0201);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::AudioPacket) == 0x0301);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::MicPacket) == 0x0401);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::FeedbackLossStats) == 0x0501);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::IdrRequest) == 0x0502);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::InputKeyboard) == 0x0601);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::InputMouse) == 0x0602);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::InputGamepad) == 0x0603);
    static_assert(static_cast<uint16_t>(MoonshineMessageType::TelemetryReport) == 0x0701);
}

int main() {
    std::cout << "==========================================================" << std::endl;
    std::cout << "Moonshine Protocol Native Contract Test Suite" << std::endl;
    std::cout << "==========================================================" << std::endl;

    test_struct_sizes_and_alignment();
    test_header_serialization_and_validation();
    test_header_error_conditions();
    test_all_message_family_types();

    std::cout << "[+] All Moonshine protocol wire contract tests passed." << std::endl;
    return 0;
}
