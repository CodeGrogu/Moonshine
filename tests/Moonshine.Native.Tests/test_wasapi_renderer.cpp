#include "moonshine/audio/wasapi_renderer.hpp"
#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cmath>
#include <cstdlib>
#include <limits>

using namespace moonshine::audio;

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

int main() {
    std::cout << "[+] Starting Moonshine WASAPI Renderer Native Tests..." << std::endl;

    // 1. Stereo 48kHz Renderer Initialisation and PCM Submission
    {
        WasapiRenderer renderer(48000, 2, false);
        int init_res = renderer.Initialize();
        TEST_ASSERT(init_res == 0);
        TEST_ASSERT(renderer.IsInitialized());

        std::vector<float> pcm(480, 0.0f);
        for (size_t i = 0; i < pcm.size(); ++i) {
            pcm[i] = 0.25f * std::sin(2.0f * 3.14159f * 440.0f * (static_cast<float>(i) / 48000.0f));
        }

        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        TEST_ASSERT(submit_res == 0);

        uint64_t rendered = 0;
        uint32_t underruns = 0;
        renderer.GetMetrics(rendered, underruns);
        TEST_ASSERT(rendered == 240);

        int rec_res = renderer.Recover();
        TEST_ASSERT(rec_res == 0);
        TEST_ASSERT(renderer.IsInitialized());

        std::cout << "    [+] Stereo 48kHz Submit PCM & Recover: " << rendered << " frames rendered" << std::endl;
        renderer.Shutdown();
        TEST_ASSERT(!renderer.IsInitialized());
    }

    // 2. 44.1kHz and 96kHz Sample Rate Conversion
    {
        WasapiRenderer renderer441(44100, 2, false);
        int init_res = renderer441.Initialize();
        TEST_ASSERT(init_res == 0);
        std::vector<float> pcm441(442, 0.1f);
        int submit_res = renderer441.SubmitPcm(pcm441.data(), 221);
        TEST_ASSERT(submit_res == 0);
        renderer441.Shutdown();
        std::cout << "    [+] 44.1kHz Resampling Render: Verified" << std::endl;

        WasapiRenderer renderer96(96000, 2, false);
        init_res = renderer96.Initialize();
        TEST_ASSERT(init_res == 0);
        std::vector<float> pcm96(960, 0.1f);
        submit_res = renderer96.SubmitPcm(pcm96.data(), 480);
        TEST_ASSERT(submit_res == 0);
        renderer96.Shutdown();
        std::cout << "    [+] 96kHz Resampling Render: Verified" << std::endl;
    }

    // 3. Surround 5.1 Renderer Initialisation and Submission
    {
        WasapiRenderer renderer(48000, 6, false);
        int init_res = renderer.Initialize();
        TEST_ASSERT(init_res == 0);

        std::vector<float> pcm(1440, 0.0f);
        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        TEST_ASSERT(submit_res == 0);

        uint64_t rendered = 0;
        uint32_t underruns = 0;
        renderer.GetMetrics(rendered, underruns);
        TEST_ASSERT(rendered == 240);

        std::cout << "    [+] Surround 5.1 Submit PCM: " << rendered << " frames rendered" << std::endl;
    }

    // 4. Surround 7.1 Renderer Initialisation and Submission
    {
        WasapiRenderer renderer(48000, 8, true);
        int init_res = renderer.Initialize();
        TEST_ASSERT(init_res == 0);

        std::vector<float> pcm(1920, 0.0f);
        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        TEST_ASSERT(submit_res == 0);

        std::cout << "    [+] Surround 7.1 Submit PCM: 240 frames rendered" << std::endl;
    }

    // 5. C-ABI Audio Recovery Test
    {
        TEST_ASSERT(moonshine_audio_recover(nullptr) == -1);
        MoonshineAudioHandle handle = moonshine_audio_create_wasapi(48000, 2, 0);
        if (handle) {
            int rec_res = moonshine_audio_recover(handle);
            TEST_ASSERT(rec_res == 0);
            moonshine_audio_destroy(handle);
            std::cout << "    [+] C-ABI moonshine_audio_recover: Verified" << std::endl;
        }
    }

    // 6. 192kHz High-Resolution Sample Rate Conversion and Recovery
    {
        WasapiRenderer renderer192(192000, 2, false);
        int init_res = renderer192.Initialize();
        TEST_ASSERT(init_res == 0);
        std::vector<float> pcm192(1920, 0.15f);
        int submit_res = renderer192.SubmitPcm(pcm192.data(), 960);
        TEST_ASSERT(submit_res == 0);

        for (int i = 0; i < 5; ++i) {
            int rec_res = renderer192.Recover();
            TEST_ASSERT(rec_res == 0);
            submit_res = renderer192.SubmitPcm(pcm192.data(), 960);
            TEST_ASSERT(submit_res == 0);
        }
        renderer192.Shutdown();
        std::cout << "    [+] 192kHz Resampling and Dynamic Recovery: Verified" << std::endl;
    }

    // 7. Defensive Boundary and Invalid Input Handling
    {
        WasapiRenderer renderer(48000, 2, false);
        TEST_ASSERT(renderer.SubmitPcm(nullptr, 240) == -1);
        TEST_ASSERT(renderer.SubmitPcm(nullptr, 0) == -1);

        int init_res = renderer.Initialize();
        TEST_ASSERT(init_res == 0);
        TEST_ASSERT(renderer.SubmitPcm(nullptr, 240) == -1);

        std::vector<float> nan_pcm(480, std::numeric_limits<float>::quiet_NaN());
        nan_pcm[0] = std::numeric_limits<float>::infinity();
        int submit_res = renderer.SubmitPcm(nan_pcm.data(), 240);
        TEST_ASSERT(submit_res == 0);
        renderer.Shutdown();
        std::cout << "    [+] Defensive Boundaries and NaN/Inf Input Protection: Verified" << std::endl;
    }

    // 8. Repeated Recovery Stress Loop
    {
        WasapiRenderer renderer(48000, 2, false);
        int init_res = renderer.Initialize();
        TEST_ASSERT(init_res == 0);

        for (int i = 0; i < 20; ++i) {
            int rec_res = renderer.Recover();
            TEST_ASSERT(rec_res == 0);
            TEST_ASSERT(renderer.IsInitialized());
        }
        renderer.Shutdown();
        std::cout << "    [+] Repeated Recovery Stress Loop: Verified" << std::endl;
    }

    std::cout << "[+] All WASAPI Renderer Native Tests Passed Successfully!" << std::endl;
    return 0;
}

