#include <iostream>
#include <vector>
#include <cmath>
#include <cstring>
#include <cstdlib>
#include <numbers>
#include "moonshine/audio/audio_resampler.hpp"
#include "moonshine/audio/channel_converter.hpp"

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

using namespace moonshine::audio;

void TestResamplerPassThrough() {
    std::cout << "[Test] AudioResampler 1:1 Pass-Through..." << std::endl;
    AudioResampler resampler(48000, 48000, 2);

    std::vector<float> input(960 * 2);
    for (size_t i = 0; i < input.size(); ++i) {
        input[i] = static_cast<float>(i % 100) / 100.0f;
    }

    resampler.PushInput(input.data(), 960);
    TEST_ASSERT(resampler.QueuedInputFrames() == 960);
    TEST_ASSERT(resampler.AvailableOutputFrames() == 960);

    std::vector<float> output(960 * 2, 0.0f);
    size_t generated = resampler.Resample(output.data(), 960);
    TEST_ASSERT(generated == 960);
    TEST_ASSERT(resampler.QueuedInputFrames() == 0);
    TEST_ASSERT(std::memcmp(input.data(), output.data(), input.size() * sizeof(float)) == 0);
}

void TestPartialConsumptionBufferPressure() {
    std::cout << "[Test] AudioResampler Partial Consumption under WASAPI Buffer Pressure..." << std::endl;
    AudioResampler resampler(44100, 48000, 2);

    // Generate 4410 frames of 44.1 kHz audio (100 ms)
    std::vector<float> input(4410 * 2);
    for (size_t f = 0; f < 4410; ++f) {
        float val = std::sin(2.0f * std::numbers::pi_v<float> * 440.0f * static_cast<float>(f) / 44100.0f);
        input[f * 2] = val;
        input[f * 2 + 1] = val;
    }

    // Push all input
    resampler.PushInput(input.data(), 4410);

    // Consume in small constrained chunks of 240 frames (simulating 5ms WASAPI buffers)
    std::vector<float> output_full;
    std::vector<float> chunk(240 * 2);

    size_t total_out_frames = 0;
    while (true) {
        size_t gen = resampler.Resample(chunk.data(), 240);
        if (gen == 0) break;
        total_out_frames += gen;
        output_full.insert(output_full.end(), chunk.begin(), chunk.begin() + static_cast<ptrdiff_t>(gen * 2));
    }

    // At 44.1k -> 48k, 4410 frames should yield approx 4800 frames (within boundary filter tap margin)
    TEST_ASSERT(total_out_frames >= 4790 && total_out_frames <= 4810);
    TEST_ASSERT(resampler.QueuedInputFrames() <= AudioResampler::kSincTaps);
}

void TestHarmonicFidelitySineWave() {
    std::cout << "[Test] AudioResampler Sinc Harmonic & Amplitude Preservation (44.1k -> 48k)..." << std::endl;
    AudioResampler resampler(44100, 48000, 2);

    // 1 kHz sine wave at 44.1 kHz
    constexpr size_t in_frames = 44100;
    std::vector<float> input(in_frames * 2);
    for (size_t f = 0; f < in_frames; ++f) {
        float val = 0.8f * std::sin(2.0f * std::numbers::pi_v<float> * 1000.0f * static_cast<float>(f) / 44100.0f);
        input[f * 2] = val;
        input[f * 2 + 1] = val;
    }

    resampler.PushInput(input.data(), in_frames);

    std::vector<float> output(48000 * 2, 0.0f);
    size_t gen = resampler.Resample(output.data(), 48000);
    TEST_ASSERT(gen > 47900);

    // Measure peak amplitude in steady state (skip initial filter ramp)
    float max_val = 0.0f;
    for (size_t f = 1000; f < gen - 1000; ++f) {
        max_val = (std::max)(max_val, std::abs(output[f * 2]));
    }

    // Peak amplitude should remain within 0.78 - 0.82 (+-0.2 dB of 0.80)
    TEST_ASSERT(max_val >= 0.78f && max_val <= 0.82f);
}

void TestChannelConverterConformance() {
    std::cout << "[Test] ChannelConverter ITU-R BS.775 Multi-Channel Matrix Conformance..." << std::endl;

    // 1. Stereo to Mono
    {
        float stereo[4] = {0.8f, 0.4f, -0.6f, 0.2f};
        float mono[2] = {0.0f, 0.0f};
        ChannelConverter::Convert(stereo, 2, 2, mono, 1);
        TEST_ASSERT(std::abs(mono[0] - 0.6f) < 1e-4f);
        TEST_ASSERT(std::abs(mono[1] - (-0.2f)) < 1e-4f);
    }

    // 2. Mono to Stereo
    {
        float mono[2] = {0.75f, -0.5f};
        float stereo[4] = {0.0f};
        ChannelConverter::Convert(mono, 1, 2, stereo, 2);
        TEST_ASSERT(stereo[0] == 0.75f && stereo[1] == 0.75f);
        TEST_ASSERT(stereo[2] == -0.5f && stereo[3] == -0.5f);
    }

    // 3. 5.1 to Stereo (ITU-R BS.775)
    // 5.1 layout: L, R, C, LFE, Ls, Rs
    {
        float src_51[6] = {1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f}; // Left only
        float dst_st[2] = {0.0f, 0.0f};
        ChannelConverter::Convert(src_51, 6, 1, dst_st, 2);
        TEST_ASSERT(std::abs(dst_st[0] - 1.0f) < 1e-4f);
        TEST_ASSERT(std::abs(dst_st[1] - 0.0f) < 1e-4f);

        // Centre only (should split equally into L and R attenuated by 0.7071)
        float src_c[6] = {0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f};
        ChannelConverter::Convert(src_c, 6, 1, dst_st, 2);
        TEST_ASSERT(std::abs(dst_st[0] - ChannelConverter::kSqrt2Inv) < 1e-4f);
        TEST_ASSERT(std::abs(dst_st[1] - ChannelConverter::kSqrt2Inv) < 1e-4f);
    }
}

int main() {
    std::cout << "=== Running Audio Resampler & Channel Converter Test Suite ===" << std::endl;
    TestResamplerPassThrough();
    TestPartialConsumptionBufferPressure();
    TestHarmonicFidelitySineWave();
    TestChannelConverterConformance();
    std::cout << "All Audio Resampler tests passed successfully." << std::endl;
    return 0;
}
