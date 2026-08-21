#include <iostream>
#include <vector>
#include <cstdlib>
#include <cstring>
#include "moonshine/audio/virtual_audio_driver.hpp"
#include "../../drivers/audio/shared_audio_buffer.h"
#include "../../drivers/audio/minwave.hpp"
#include "../../drivers/audio/mintopo.hpp"
#include "../../drivers/audio/adapter.hpp"

using namespace moonshine::audio;

#define REQUIRE(expr) \
    do { \
        if (!(expr)) { \
            std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::exit(1); \
        } \
    } while (false)

static void test_virtual_audio_driver_init_and_status() {
    VirtualAudioDriverController controller;
    REQUIRE(controller.Initialize());

    VirtualAudioDriverStatus status = controller.GetStatus();
    REQUIRE(status.supportedSampleRatesCount == 5);
    REQUIRE(status.supportedChannelsCount == 4);
    REQUIRE(std::strcmp(status.driverVersion, "1.0.0.0") == 0);

    std::cout << "[PASS] test_virtual_audio_driver_init_and_status" << std::endl;
}

static void test_virtual_audio_driver_format_validation() {
    VirtualAudioDriverController controller;
    REQUIRE(controller.Initialize());

    // Valid sample rates: 44.1k, 48k, 88.2k, 96k, 192k
    REQUIRE(controller.ValidateFormat(44100, 2, 4));  // Stereo Float32
    REQUIRE(controller.ValidateFormat(48000, 2, 4));  // Stereo Float32
    REQUIRE(controller.ValidateFormat(48000, 6, 4));  // 5.1 Float32
    REQUIRE(controller.ValidateFormat(48000, 8, 4));  // 7.1 Float32
    REQUIRE(controller.ValidateFormat(48000, 1, 1));  // Mono PCM16
    REQUIRE(controller.ValidateFormat(96000, 2, 2));  // Stereo PCM24
    REQUIRE(controller.ValidateFormat(192000, 2, 3)); // Stereo PCM32

    // Invalid sample rates / channels / formats
    REQUIRE(!controller.ValidateFormat(32000, 2, 4));  // Unsupported sample rate
    REQUIRE(!controller.ValidateFormat(48000, 3, 4));  // Unsupported 3-channel
    REQUIRE(!controller.ValidateFormat(48000, 2, 0));  // Invalid format enum 0
    REQUIRE(!controller.ValidateFormat(48000, 2, 5));  // Invalid format enum 5

    std::cout << "[PASS] test_virtual_audio_driver_format_validation" << std::endl;
}

static void test_virtual_audio_driver_endpoints() {
    VirtualAudioDriverController controller;
    REQUIRE(controller.Initialize());

    std::vector<VirtualAudioEndpointInfo> endpoints = controller.EnumerateEndpoints();
    REQUIRE(endpoints.size() == 2);

    REQUIRE(endpoints[0].role == AudioEndpointRole::RenderSpeaker);
    REQUIRE(endpoints[0].friendlyName == "Moonshine Audio");
    REQUIRE(endpoints[0].defaultSampleRate == 48000);
    REQUIRE(endpoints[0].defaultChannels == 2);

    REQUIRE(endpoints[1].role == AudioEndpointRole::CaptureMicrophone);
    REQUIRE(endpoints[1].friendlyName == "Moonshine Microphone");
    REQUIRE(endpoints[1].defaultSampleRate == 48000);
    REQUIRE(endpoints[1].defaultChannels == 1);

    std::cout << "[PASS] test_virtual_audio_driver_endpoints" << std::endl;
}

static void test_virtual_audio_driver_mmcss() {
    VirtualAudioDriverController controller;
    REQUIRE(controller.Initialize());

    void* taskHandle = nullptr;
    bool enabled = controller.EnableMmcssScheduling(&taskHandle);
    if (enabled && taskHandle) {
        bool disabled = controller.DisableMmcssScheduling(taskHandle);
        REQUIRE(disabled);
    }

    std::cout << "[PASS] test_virtual_audio_driver_mmcss" << std::endl;
}

static void test_minwave_stream_allocation_and_formats() {
    CMiniportWaveRT waveRt;
    REQUIRE(waveRt.Init() == 0);

    CMiniportWaveRTStream* pStream = nullptr;
    int res = waveRt.NewStream(
        MOONSHINE_ENDPOINT_RENDER,
        48000,
        2,
        MOONSHINE_FORMAT_FLOAT_32,
        &pStream
    );
    REQUIRE(res == 0);
    REQUIRE(pStream != nullptr);
    REQUIRE(pStream->GetSampleRate() == 48000);
    REQUIRE(pStream->GetChannels() == 2);
    REQUIRE(pStream->GetFormat() == MOONSHINE_FORMAT_FLOAT_32);

    void* pBuffer = nullptr;
    uint32_t actualSize = 0;
    REQUIRE(pStream->AllocateAudioBuffer(480 * 2 * sizeof(float), &pBuffer, &actualSize) == 0);
    REQUIRE(pBuffer != nullptr);
    REQUIRE(actualSize >= 480 * 2 * sizeof(float));

    uint32_t playPos = 0, writePos = 0;
    REQUIRE(pStream->GetPositions(&playPos, &writePos) == 0);
    REQUIRE(playPos == 0);
    REQUIRE(writePos == 0);

    REQUIRE(pStream->SetState(1) == 0);
    REQUIRE(pStream->IsActive());
    REQUIRE(pStream->SetState(0) == 0);
    REQUIRE(!pStream->IsActive());

    delete pStream;

    std::cout << "[PASS] test_minwave_stream_allocation_and_formats" << std::endl;
}

static void test_mintopo_and_adapter() {
    CMoonshineAudioAdapter adapter;
    REQUIRE(adapter.Start() == 0);
    REQUIRE(adapter.IsRunning());

    CMiniportTopology* pTopo = adapter.GetTopology();
    REQUIRE(pTopo != nullptr);
    REQUIRE(CMiniportTopology::GetRenderPinCount() == 1);
    REQUIRE(CMiniportTopology::GetCapturePinCount() == 1);

    REQUIRE(adapter.Stop() == 0);
    REQUIRE(!adapter.IsRunning());

    std::cout << "[PASS] test_mintopo_and_adapter" << std::endl;
}

int main() {
    std::cout << "Running Virtual Audio Driver Native Tests..." << std::endl;
    test_virtual_audio_driver_init_and_status();
    test_virtual_audio_driver_format_validation();
    test_virtual_audio_driver_endpoints();
    test_virtual_audio_driver_mmcss();
    test_minwave_stream_allocation_and_formats();
    test_mintopo_and_adapter();
    std::cout << "All Virtual Audio Driver Native Tests Passed Successfully!" << std::endl;
    return 0;
}
