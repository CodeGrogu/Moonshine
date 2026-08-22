#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
#include <iostream>
#include <vector>

int main() {
    std::cout << "[*] Running WASAPI Loopback Capture Native Tests..." << std::endl;

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
            assert(res == 1);
            assert(qpc > 0);
            (void)res;
            std::cout << "    [+] Stereo 48kHz Read Float: " << samples_read << " samples" << std::endl;

            std::vector<int16_t> pcm16_buffer(480);
            res = moonshine_audio_capture_read_pcm16(
                handle,
                pcm16_buffer.data(),
                (uint32_t)pcm16_buffer.size(),
                &samples_read,
                &qpc
            );
            assert(res == 1);
            (void)res;
            std::cout << "    [+] Stereo 48kHz Read PCM16: " << samples_read << " samples" << std::endl;

            uint64_t frames = 0;
            uint64_t samples = 0;
            uint32_t underruns = 0;
            uint32_t overruns = 0;
            moonshine_audio_capture_get_metrics(handle, &frames, &samples, &underruns, &overruns);
            assert(frames >= 1);
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
            assert(res == 1);
            (void)res;
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
            assert(res == 1);
            (void)res;
            std::cout << "    [+] Surround 7.1 48kHz Read PCM16: " << samples_read << " samples" << std::endl;

            moonshine_audio_capture_destroy(handle);
        }
    }

    std::cout << "[+] WASAPI Loopback Capture Native Tests Passed Successfully!" << std::endl;
    return 0;
}
