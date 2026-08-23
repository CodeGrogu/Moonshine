#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cstdlib>

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

int main() {
    std::cout << "[*] Running WASAPI Loopback and Microphone Capture Native Tests..." << std::endl;

    // 1. Test Stereo 48kHz 5ms Capture
    {
        MoonshineAudioCaptureHandle handle = moonshine_audio_capture_create(48000, 2, 5);
        if (handle) {
            std::vector<float> float_buffer(480); // 240 samples * 2 channels
            uint32_t samples_read = 0;
            uint64_t qpc = 0;

            int res = moonshine_audio_capture_read_float(
                handle,
                float_buffer.data(),
                (uint32_t)float_buffer.size(),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            TEST_ASSERT(qpc > 0);
            std::cout << "    [+] Stereo 48kHz Read Float: " << samples_read << " samples" << std::endl;

            std::vector<int16_t> pcm16_buffer(480);
            res = moonshine_audio_capture_read_pcm16(
                handle,
                pcm16_buffer.data(),
                (uint32_t)pcm16_buffer.size(),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            std::cout << "    [+] Stereo 48kHz Read PCM16: " << samples_read << " samples" << std::endl;

            uint64_t frames = 0;
            uint64_t samples = 0;
            uint32_t underruns = 0;
            uint32_t overruns = 0;
            moonshine_audio_capture_get_metrics(handle, &frames, &samples, &underruns, &overruns);
            TEST_ASSERT(frames >= 1);
            std::cout << "    [+] Metrics: " << frames << " frames, " << samples << " channel-samples" << std::endl;

            moonshine_audio_capture_destroy(handle);
        } else {
            std::cout << "    [-] No default WASAPI audio render device available (headless environment)." << std::endl;
        }
    }

    // 2. Test Surround 5.1 48kHz 10ms Capture
    {
        MoonshineAudioCaptureHandle handle = moonshine_audio_capture_create(48000, 6, 10);
        if (handle) {
            std::vector<float> float_buffer(2880); // 480 samples * 6 channels
            uint32_t samples_read = 0;
            uint64_t qpc = 0;

            int res = moonshine_audio_capture_read_float(
                handle,
                float_buffer.data(),
                (uint32_t)float_buffer.size(),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            std::cout << "    [+] Surround 5.1 48kHz Read Float: " << samples_read << " samples" << std::endl;

            moonshine_audio_capture_destroy(handle);
        }
    }

    // 3. Test Surround 7.1 48kHz 5ms Capture
    {
        MoonshineAudioCaptureHandle handle = moonshine_audio_capture_create(48000, 8, 5);
        if (handle) {
            std::vector<int16_t> pcm16_buffer(1920); // 240 samples * 8 channels
            uint32_t samples_read = 0;
            uint64_t qpc = 0;

            int res = moonshine_audio_capture_read_pcm16(
                handle,
                pcm16_buffer.data(),
                (uint32_t)pcm16_buffer.size(),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            std::cout << "    [+] Surround 7.1 48kHz Read PCM16: " << samples_read << " samples" << std::endl;

            moonshine_audio_capture_destroy(handle);
        }
    }

    // 4. Test Microphone Capture Lifecycle and Recovery
    {
        TEST_ASSERT(moonshine_mic_capture_is_active(nullptr) == 0);
        TEST_ASSERT(moonshine_mic_capture_recover(nullptr) == 0);

        MoonshineMicCaptureHandle handle = moonshine_mic_capture_create(48000, 1, 10);
        if (handle) {
            TEST_ASSERT(moonshine_mic_capture_is_active(handle) == 1);

            std::vector<float> buffer(480);
            uint32_t samples_read = 0;
            uint64_t qpc = 0;

            int res = moonshine_mic_capture_read_float(
                handle,
                buffer.data(),
                static_cast<uint32_t>(buffer.size()),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            TEST_ASSERT(samples_read == 480);
            TEST_ASSERT(qpc > 0);

            int recover_res = moonshine_mic_capture_recover(handle);
            TEST_ASSERT(recover_res == 1);
            TEST_ASSERT(moonshine_mic_capture_is_active(handle) == 1);

            res = moonshine_mic_capture_read_float(
                handle,
                buffer.data(),
                static_cast<uint32_t>(buffer.size()),
                &samples_read,
                &qpc
            );
            TEST_ASSERT(res == 1);
            TEST_ASSERT(samples_read == 480);
            TEST_ASSERT(qpc > 0);

            moonshine_mic_capture_destroy(handle);
            std::cout << "    [+] Microphone Capture and Recovery: Verified" << std::endl;
        } else {
            std::cout << "    [-] No default WASAPI microphone capture endpoint available." << std::endl;
        }
    }

    std::cout << "[+] WASAPI Capture Native Tests Passed Successfully!" << std::endl;
    return 0;
}
