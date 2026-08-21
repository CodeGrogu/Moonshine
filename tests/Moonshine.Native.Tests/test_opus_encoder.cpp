#include <iostream>
#include <vector>
#include <cassert>
#include <cmath>
#include <numeric>
#include "moonshine/audio/opus_audio_encoder.hpp"

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
    assert(encoder.is_initialized());
    assert(encoder.channels() == 2);
    assert(encoder.streams_count() == 1);
    assert(encoder.coupled_count() == 1);

    // 5ms @ 48kHz = 240 samples per channel = 480 float samples
    std::vector<float> pcm(480);
    for (size_t i = 0; i < pcm.size(); ++i) {
        pcm[i] = static_cast<float>(0.5 * std::sin(2.0 * 3.14159265358979323846 * 440.0 * (i / 48000.0)));
    }

    std::vector<uint8_t> payload(1024);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_float(pcm.data(), 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    if (!ok || bytes_written == 0) {
        std::cerr << "Failed to encode float frame" << std::endl;
        std::exit(1);
    }
    // Target for 160kbps @ 5ms is (160000 * 5) / 8000 = 100 bytes
    assert(bytes_written >= 32 && bytes_written <= 500);

    OpusEncoderMetrics metrics{};
    encoder.get_metrics(metrics);
    assert(metrics.total_frames_encoded == 1);
    assert(metrics.total_bytes_encoded == bytes_written);
    assert(metrics.current_bitrate == 160000);

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
    assert(encoder.is_initialized());
    assert(encoder.channels() == 6);
    assert(encoder.streams_count() == 4);
    assert(encoder.coupled_count() == 2);

    // 10ms @ 48kHz = 480 samples per channel = 2880 int16 samples
    std::vector<int16_t> pcm(2880, 1234);
    std::vector<uint8_t> payload(2048);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_pcm16(pcm.data(), 480, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    if (!ok || bytes_written == 0) {
        std::cerr << "Failed to encode pcm16 frame" << std::endl;
        std::exit(1);
    }

    // Test dynamic bitrate scaling
    if (!encoder.set_bitrate(320000) || encoder.bitrate() != 320000) {
        std::cerr << "Failed to set bitrate" << std::endl;
        std::exit(1);
    }

    std::cout << "[PASS] test_opus_encoder_surround51_pcm16 (bytes: " << bytes_written << ")" << std::endl;
}

static void test_opus_encoder_surround71_multistream() {
    OpusEncoderConfig config{};
    config.sample_rate = 48000;
    config.channels = 8;
    config.bitrate = 450000;
    config.frame_duration_ms = 5;

    OpusAudioEncoder encoder(config);
    assert(encoder.is_initialized());
    assert(encoder.channels() == 8);
    assert(encoder.streams_count() == 6);
    assert(encoder.coupled_count() == 2);

    // 5ms @ 48kHz = 240 samples per channel = 1920 float samples
    std::vector<float> pcm(1920, 0.25f);
    std::vector<uint8_t> payload(2048);
    uint32_t bytes_written = 0;

    bool ok = encoder.encode_float(pcm.data(), 240, payload.data(), static_cast<uint32_t>(payload.size()), bytes_written);
    if (!ok || bytes_written == 0) {
        std::cerr << "Failed to encode float frame" << std::endl;
        std::exit(1);
    }

    std::cout << "[PASS] test_opus_encoder_surround71_multistream (bytes: " << bytes_written << ")" << std::endl;
}

int main() {
    std::cout << "--- Running Moonshine Opus Audio Encoder Native Tests ---" << std::endl;
    test_opus_encoder_stereo_float();
    test_opus_encoder_surround51_pcm16();
    test_opus_encoder_surround71_multistream();
    std::cout << "All Opus Audio Encoder native tests passed successfully!" << std::endl;
    return 0;
}
