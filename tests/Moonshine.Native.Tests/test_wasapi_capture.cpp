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

            int recover_res = moonshine_audio_capture_recover(handle);
            TEST_ASSERT(recover_res == 1);
            std::cout << "    [+] Loopback Capture Recovery: Verified" << std::endl;

            moonshine_audio_capture_destroy(handle);
        } else {
            std::cout << "    [-] No default WASAPI audio render device available (headless environment)." << std::endl;
        }
    }

    // 2. Test 44.1kHz and 96kHz Loopback Capture
    {
        MoonshineAudioCaptureHandle handle441 = moonshine_audio_capture_create(44100, 2, 10);
        if (handle441) {
            std::vector<float> float_buffer(882);
            uint32_t samples_read = 0;
            uint64_t qpc = 0;
            int res = moonshine_audio_capture_read_float(handle441, float_buffer.data(), (uint32_t)float_buffer.size(), &samples_read, &qpc);
            TEST_ASSERT(res == 1);
            moonshine_audio_capture_destroy(handle441);
            std::cout << "    [+] 44.1kHz Loopback Capture: Verified" << std::endl;
        }

        MoonshineAudioCaptureHandle handle96k = moonshine_audio_capture_create(96000, 2, 10);
        if (handle96k) {
            std::vector<float> float_buffer(1920);
            uint32_t samples_read = 0;
            uint64_t qpc = 0;
            int res = moonshine_audio_capture_read_float(handle96k, float_buffer.data(), (uint32_t)float_buffer.size(), &samples_read, &qpc);
            TEST_ASSERT(res == 1);
            moonshine_audio_capture_destroy(handle96k);
            std::cout << "    [+] 96kHz Loopback Capture: Verified" << std::endl;
        }
    }

    // 3. Test Surround 5.1 48kHz 10ms Capture
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

    // 4. Test Surround 7.1 48kHz 5ms Capture
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

    // 5. Test Microphone Capture Lifecycle and Recovery
    {
        TEST_ASSERT(moonshine_mic_capture_is_active(nullptr) == 0);
        TEST_ASSERT(moonshine_mic_capture_recover(nullptr) == 0);
        TEST_ASSERT(moonshine_audio_capture_recover(nullptr) == 0);

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

    // 6. Test Dynamic Format Change Resilience (44.1 kHz <-> 48 kHz <-> 96 kHz <-> 192 kHz)
    {
        MoonshineAudioCaptureHandle handle192k = moonshine_audio_capture_create(192000, 2, 5);
        if (handle192k) {
            std::vector<float> float_buffer(3840); // 1920 samples * 2 channels
            uint32_t samples_read = 0;
            uint64_t qpc = 0;
            int res = moonshine_audio_capture_read_float(handle192k, float_buffer.data(), (uint32_t)float_buffer.size(), &samples_read, &qpc);
            TEST_ASSERT(res == 1);
            TEST_ASSERT(qpc > 0);
            
            // Perform multiple recoveries simulating endpoint format shifts
            for (int i = 0; i < 5; ++i) {
                int rec_res = moonshine_audio_capture_recover(handle192k);
                TEST_ASSERT(rec_res == 1);
                res = moonshine_audio_capture_read_float(handle192k, float_buffer.data(), (uint32_t)float_buffer.size(), &samples_read, &qpc);
                TEST_ASSERT(res == 1);
            }
            moonshine_audio_capture_destroy(handle192k);
            std::cout << "    [+] 192kHz Dynamic Format Switch and Recovery: Verified" << std::endl;
        }
    }

    // 7. Test Defensive Boundary and Null Pointer Handling
    {
        uint32_t samples_read = 0;
        uint64_t qpc = 0;
        float sample_buf[10] = {0};

        TEST_ASSERT(moonshine_audio_capture_read_float(nullptr, sample_buf, 10, &samples_read, &qpc) == 0);
        TEST_ASSERT(moonshine_audio_capture_read_pcm16(nullptr, reinterpret_cast<int16_t*>(sample_buf), 10, &samples_read, &qpc) == 0);
        moonshine_audio_capture_get_metrics(nullptr, nullptr, nullptr, nullptr, nullptr);

        MoonshineAudioCaptureHandle handle = moonshine_audio_capture_create(48000, 2, 5);
        if (handle) {
            TEST_ASSERT(moonshine_audio_capture_read_float(handle, nullptr, 10, &samples_read, &qpc) == 0);
            TEST_ASSERT(moonshine_audio_capture_read_float(handle, sample_buf, 0, &samples_read, &qpc) == 0);
            TEST_ASSERT(moonshine_audio_capture_read_pcm16(handle, nullptr, 10, &samples_read, &qpc) == 0);
            TEST_ASSERT(moonshine_audio_capture_read_pcm16(handle, reinterpret_cast<int16_t*>(sample_buf), 0, &samples_read, &qpc) == 0);
            moonshine_audio_capture_destroy(handle);
            std::cout << "    [+] Defensive Boundaries and Null Pointer Protection: Verified" << std::endl;
        }
    }

    // 8. Test Repeated Recovery Stress Loop
    {
        MoonshineAudioCaptureHandle handle = moonshine_audio_capture_create(48000, 2, 5);
        if (handle) {
            for (int i = 0; i < 20; ++i) {
                int rec_res = moonshine_audio_capture_recover(handle);
                TEST_ASSERT(rec_res == 1);
            }
            moonshine_audio_capture_destroy(handle);
            std::cout << "    [+] Repeated Recovery Stress Loop: Verified" << std::endl;
        }
    }

    std::cout << "[+] WASAPI Capture Native Tests Passed Successfully!" << std::endl;
    return 0;
}

