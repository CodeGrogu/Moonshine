#include <iostream>
#include <vector>
#include <cmath>
#include <cstdlib>
#include "moonshine/audio/mic_audio_sink.hpp"

using namespace moonshine::audio;

#define REQUIRE(expr) \
    do { \
        if (!(expr)) { \
            std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::exit(1); \
        } \
    } while (false)

static void test_mic_sink_init_and_push_pull() {
    MicSinkConfig config{};
    config.sample_rate = 48000;
    config.channels = 1;
    config.target_latency_ms = 10;
    config.gain_multiplier = 1.0f;
    config.noise_gate_threshold_db = -60.0f;
    config.is_muted = false;

    MicAudioSink sink(config);
    REQUIRE(sink.is_initialized());
    REQUIRE(sink.sample_rate() == 48000);
    REQUIRE(sink.channels() == 1);
    REQUIRE(!sink.is_muted());

    // 10ms frame payload (e.g. 64 bytes)
    std::vector<uint8_t> payload(64, 180);
    bool pushed = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), 480, 1);
    REQUIRE(pushed);

    // Pull 480 samples (10ms @ 48kHz mono)
    std::vector<float> pcm(480);
    uint32_t samples_read = 0;
    bool pulled = sink.pull_pcm(pcm.data(), static_cast<uint32_t>(pcm.size()), samples_read);
    REQUIRE(pulled);
    REQUIRE(samples_read == 480);

    MicSinkMetrics metrics{};
    sink.get_metrics(metrics);
    REQUIRE(metrics.total_packets_received == 1);
    REQUIRE(metrics.total_samples_rendered == 480);

    std::cout << "[PASS] test_mic_sink_init_and_push_pull (samples: " << samples_read << ")" << std::endl;
}

static void test_mic_sink_gain_and_mute() {
    MicSinkConfig config{};
    config.sample_rate = 48000;
    config.channels = 1;
    config.target_latency_ms = 10;
    config.gain_multiplier = 2.0f;
    config.noise_gate_threshold_db = -80.0f;
    config.is_muted = false;

    MicAudioSink sink(config);

    // Push 1 frame
    std::vector<uint8_t> payload(64, 200);
    bool pushed1 = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), 960, 2);
    REQUIRE(pushed1);

    // Pull with gain 2.0
    std::vector<float> pcm(480);
    uint32_t samples_read = 0;
    bool pulled1 = sink.pull_pcm(pcm.data(), static_cast<uint32_t>(pcm.size()), samples_read);
    REQUIRE(pulled1);
    REQUIRE(samples_read == 480);

    // Now test Mute
    sink.set_mute(true);
    REQUIRE(sink.is_muted());

    bool pushed2 = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), 1440, 3);
    REQUIRE(pushed2);

    bool pulled2 = sink.pull_pcm(pcm.data(), static_cast<uint32_t>(pcm.size()), samples_read);
    REQUIRE(pulled2);
    REQUIRE(samples_read == 480);

    for (float sample : pcm) {
        REQUIRE(sample == 0.0f);
    }

    std::cout << "[PASS] test_mic_sink_gain_and_mute" << std::endl;
}

static void test_mic_sink_packet_loss_concealment() {
    MicSinkConfig config{};
    config.sample_rate = 48000;
    config.channels = 1;
    config.target_latency_ms = 10;

    MicAudioSink sink(config);

    std::vector<uint8_t> payload(64, 150);
    bool pushed1 = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), 480, 10);
    REQUIRE(pushed1);

    // Send packet 13 (missing 11, 12 -> 2 losses)
    bool pushed2 = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), 1920, 13);
    REQUIRE(pushed2);

    MicSinkMetrics metrics{};
    sink.get_metrics(metrics);
    REQUIRE(metrics.loss_count == 2);
    REQUIRE(metrics.total_packets_received == 2);

    std::cout << "[PASS] test_mic_sink_packet_loss_concealment (losses: " << metrics.loss_count << ")" << std::endl;
}

static void test_mic_sink_clock_drift_compensation() {
    MicSinkConfig config{};
    config.sample_rate = 48000;
    config.channels = 1;
    config.target_latency_ms = 10;

    MicAudioSink sink(config);

    std::vector<uint8_t> payload(64, 150);
    // Push 8 packets rapidly (exceeds max_queue_depth of 4)
    for (uint16_t i = 1; i <= 8; ++i) {
        bool pushed = sink.push_opus_packet(payload.data(), static_cast<uint32_t>(payload.size()), i * 480, i);
        REQUIRE(pushed);
    }

    MicSinkMetrics metrics{};
    sink.get_metrics(metrics);
    REQUIRE(metrics.drift_corrections > 0);

    std::cout << "[PASS] test_mic_sink_clock_drift_compensation (drift_corrections: " << metrics.drift_corrections << ")" << std::endl;
}

int main() {
    std::cout << "Running MicAudioSink CTest Suite..." << std::endl;
    test_mic_sink_init_and_push_pull();
    test_mic_sink_gain_and_mute();
    test_mic_sink_packet_loss_concealment();
    test_mic_sink_clock_drift_compensation();
    std::cout << "All MicAudioSink native tests PASSED!" << std::endl;
    return 0;
}
