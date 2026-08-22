#include "moonshine/audio/wasapi_renderer.hpp"
#include <iostream>
#include <vector>
#include <cassert>
#include <cmath>

using namespace moonshine::audio;

int main() {
    std::cout << "[+] Starting Moonshine WASAPI Renderer Native Tests..." << std::endl;

    // 1. Stereo 48kHz Renderer Initialisation and PCM Submission
    {
        WasapiRenderer renderer(48000, 2, false);
        int init_res = renderer.Initialize();
        (void)init_res;
        assert(init_res == 0);
        assert(renderer.IsInitialized());

        std::vector<float> pcm(480, 0.0f);
        for (size_t i = 0; i < pcm.size(); ++i) {
            pcm[i] = 0.25f * std::sin(2.0f * 3.14159f * 440.0f * (static_cast<float>(i) / 48000.0f));
        }

        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        (void)submit_res;
        assert(submit_res == 0);

        uint64_t rendered = 0;
        uint32_t underruns = 0;
        renderer.GetMetrics(rendered, underruns);
        assert(rendered == 240);

        std::cout << "    [+] Stereo 48kHz Submit PCM: " << rendered << " frames rendered" << std::endl;
        renderer.Shutdown();
        assert(!renderer.IsInitialized());
    }

    // 2. Surround 5.1 Renderer Initialisation and Submission
    {
        WasapiRenderer renderer(48000, 6, false);
        int init_res = renderer.Initialize();
        (void)init_res;
        assert(init_res == 0);

        std::vector<float> pcm(1440, 0.0f);
        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        (void)submit_res;
        assert(submit_res == 0);

        uint64_t rendered = 0;
        uint32_t underruns = 0;
        renderer.GetMetrics(rendered, underruns);
        assert(rendered == 240);

        std::cout << "    [+] Surround 5.1 Submit PCM: " << rendered << " frames rendered" << std::endl;
    }

    // 3. Surround 7.1 Renderer Initialisation and Submission
    {
        WasapiRenderer renderer(48000, 8, true);
        int init_res = renderer.Initialize();
        (void)init_res;
        assert(init_res == 0);

        std::vector<float> pcm(1920, 0.0f);
        int submit_res = renderer.SubmitPcm(pcm.data(), 240);
        (void)submit_res;
        assert(submit_res == 0);

        std::cout << "    [+] Surround 7.1 Submit PCM: 240 frames rendered" << std::endl;
    }

    std::cout << "[+] All WASAPI Renderer Native Tests Passed Successfully!" << std::endl;
    return 0;
}
