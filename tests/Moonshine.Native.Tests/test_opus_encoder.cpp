#include <iostream>
#include <vector>
#include <cmath>
#include <numeric>
#include <cstdlib>
#include "moonshine/audio/opus_audio_encoder.hpp"

#define REQUIRE(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::exit(1); \
    } \
} while(0)

using namespace moonshine::audio;

static void test_opus_encoder_stereo_float() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 2;
    config.bitrate = 160000;
    config.frame_duration_ms = 5;
    config.complexity = 8;
    config.use_vbr = true;

    OpusAudioEncoder encoder(config);
    REQUIRE(encoder.is_initialized());
    REQUIRE(encoder.channels() == 2);
    REQUIRE(encoder.streams_count() == 1);
    REQUIRE(encoder.coupled_count() == 1);

    // 5ms @ 48kHz = 240 samples per channel = 480 float samples
    std::vector<float> pcm(480);
    for (size_t i = 0; i < pcm.size(); ++i) {
        pcm[i] = static_cast<float>(0.5 * std::sin(2.0 * 3.14159265358979323846 * 440.0 * (i / 48000.0)));
    }

    std::vector<uint8_t> payload(1024);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_float(pcm.data(), 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(ok);
    REQUIRE(bytes_written > 0);
    // Target for 160kbps @ 5ms is (160000 * 5) / 8000 = 100 bytes
    REQUIRE(bytes_written >= 32 && bytes_written <= 500);

    OpusEncoderMetrics metrics{};
    encoder.get_metrics(metrics);
    REQUIRE(metrics.total_frames_encoded == 1);
    REQUIRE(metrics.total_bytes_encoded == bytes_written);
    REQUIRE(metrics.current_bitrate == 160000);

    std::cout << "[PASS] test_opus_encoder_stereo_float (bytes: " << bytes_written
              << ", avg_us: " << metrics.avg_encode_time_us << ")" << std::endl;
}

static void test_opus_encoder_surround51_pcm16() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 6;
    config.bitrate = 256000;
    config.frame_duration_ms = 10;
    config.complexity = 8;

    OpusAudioEncoder encoder(config);
    REQUIRE(encoder.is_initialized());
    REQUIRE(encoder.channels() == 6);
    REQUIRE(encoder.streams_count() == 4);
    REQUIRE(encoder.coupled_count() == 2);

    // 10ms @ 48kHz = 480 samples per channel = 2880 int16 samples
    std::vector<int16_t> pcm(2880, 1234);
    std::vector<uint8_t> payload(2048);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_pcm16(pcm.data(), 480, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(ok);
    REQUIRE(bytes_written > 0);

    // Test dynamic bitrate scaling
    REQUIRE(encoder.set_bitrate(320000));
    REQUIRE(encoder.bitrate() == 320000);

    std::cout << "[PASS] test_opus_encoder_surround51_pcm16 (bytes: " << bytes_written << ")" << std::endl;
}

static void test_opus_encoder_surround71_multistream() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 8;
    config.bitrate = 450000;
    config.frame_duration_ms = 5;

    OpusAudioEncoder encoder(config);
    REQUIRE(encoder.is_initialized());
    REQUIRE(encoder.channels() == 8);
    REQUIRE(encoder.streams_count() == 5);
    REQUIRE(encoder.coupled_count() == 3);

    // 5ms @ 48kHz = 240 samples per channel = 1920 float samples
    std::vector<float> pcm(1920, 0.25f);
    std::vector<uint8_t> payload(2048);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_float(pcm.data(), 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(ok);
    REQUIRE(bytes_written > 0);

    std::cout << "[PASS] test_opus_encoder_surround71_multistream (bytes: " << bytes_written << ")" << std::endl;
}

static void test_opus_encoder_invalid_frame_size_rejection() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 2;
    config.bitrate = 160000;

    OpusAudioEncoder encoder(config);
    REQUIRE(encoder.is_initialized());

    std::vector<float> pcm(960, 0.1f);
    std::vector<uint8_t> payload(1024);
    uint32_t bytes_written = 999;

    // 256 is an invalid frame size at 48kHz (not a permitted Opus frame duration)
    bool ok = encoder.encode_float(pcm.data(), 256, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(!ok);
    REQUIRE(bytes_written == 0);

    // 220 is an invalid frame size at 48kHz (44.1kHz non-standard size)
    ok = encoder.encode_float(pcm.data(), 220, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(!ok);
    REQUIRE(bytes_written == 0);

    // 0 frame_samples is rejected
    ok = encoder.encode_float(pcm.data(), 0, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(!ok);
    REQUIRE(bytes_written == 0);

    // 240 (5ms @ 48kHz) is valid and succeeds
    ok = encoder.encode_float(pcm.data(), 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    REQUIRE(ok);
    REQUIRE(bytes_written > 0);

    std::cout << "[PASS] test_opus_encoder_invalid_frame_size_rejection" << std::endl;
}

static void test_opus_encoder_null_pointers_rejection() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 2;

    OpusAudioEncoder encoder(config);
    REQUIRE(encoder.is_initialized());

    std::vector<float> pcm(480, 0.1f);
    std::vector<uint8_t> payload(1024);
    uint32_t bytes_written = 999;

    // Null PCM pointer
    REQUIRE(!encoder.encode_float(nullptr, 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written));
    REQUIRE(bytes_written == 0);

    // Null output payload pointer
    REQUIRE(!encoder.encode_float(pcm.data(), 240, nullptr, static_cast<uint32_t>(payload.size()), bytes_written));
    REQUIRE(bytes_written == 0);

    // Zero max payload bytes
    REQUIRE(!encoder.encode_float(pcm.data(), 240, payload.data(), 0, bytes_written));
    REQUIRE(bytes_written == 0);

    std::cout << "[PASS] test_opus_encoder_null_pointers_rejection" << std::endl;
}

int main() {
    std::cout << "--- Running Moonshine Opus Audio Encoder Native Tests ---" << std::endl;
    test_opus_encoder_stereo_float();
    test_opus_encoder_surround51_pcm16();
    test_opus_encoder_surround71_multistream();
    test_opus_encoder_invalid_frame_size_rejection();
    test_opus_encoder_null_pointers_rejection();
    std::cout << "All Opus Audio Encoder native tests passed successfully!" << std::endl;
    return 0;
}
