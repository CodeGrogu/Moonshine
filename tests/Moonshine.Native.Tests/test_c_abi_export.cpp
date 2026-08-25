#include <iostream>
#include <vector>
#include <cstring>
#include <cstdlib>
#include "moonshine/export/moonshine_native_api.h"

#ifdef _MSC_VER
#pragma warning(disable: 4127) // conditional expression is constant
#endif

#define TEST_ASSERT(expr) do { \
    if (!(expr)) { \
        std::cerr << "Assertion failed: " #expr " at " << __FILE__ << ":" << __LINE__ << std::endl; \
        std::abort(); \
    } \
} while(0)

void TestExportVectorXor()
{
    std::cout << "[Test] C-ABI moonshine_vector_xor..." << std::endl;
    uint8_t dest[32] = {0xFF};
    uint8_t src[32] = {0x0F};
    moonshine_vector_xor(dest, src, 32);
    TEST_ASSERT(dest[0] == 0xF0);

    // Null safety
    moonshine_vector_xor(nullptr, src, 32);
    moonshine_vector_xor(dest, nullptr, 32);
}

void TestExportSpscLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_spsc lifecycle..." << std::endl;
    MoonshineRingBufferHandle handle = moonshine_spsc_create(128);
    TEST_ASSERT(handle != nullptr);

    TEST_ASSERT(moonshine_spsc_size(handle) == 0);

    MoonshinePacketDesc packet{};
    packet.frame_index = 55;
    packet.packet_index = 1;
    packet.payload_size = 100;

    TEST_ASSERT(moonshine_spsc_enqueue(handle, &packet) == 1);
    TEST_ASSERT(moonshine_spsc_size(handle) == 1);

    MoonshinePacketDesc popped{};
    TEST_ASSERT(moonshine_spsc_dequeue(handle, &popped) == 1);
    TEST_ASSERT(popped.frame_index == 55);
    TEST_ASSERT(popped.packet_index == 1);
    TEST_ASSERT(moonshine_spsc_size(handle) == 0);

    moonshine_spsc_destroy(handle);
}

void TestExportJitterLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_jitter lifecycle..." << std::endl;
    MoonshineJitterBufferHandle handle = moonshine_jitter_create(16);
    TEST_ASSERT(handle != nullptr);

    uint8_t payload[50] = {1, 2, 3};
    MoonshinePacketDesc packet{};
    packet.sequence_number = 1;
    packet.frame_index = 88;
    packet.packet_index = 0;
    packet.total_packets = 1;
    packet.payload_size = sizeof(payload);
    packet.flags = 0x03;
    packet.payload_ptr = payload;

    int push_res = moonshine_jitter_push_packet(handle, &packet);
    TEST_ASSERT(push_res == 1);

    MoonshineFrameDesc frame{};
    int pop_res = moonshine_jitter_pop_frame(handle, &frame);
    TEST_ASSERT(pop_res == 1);
    TEST_ASSERT(frame.frame_index == 88);
    TEST_ASSERT(frame.packet_count == 1);

    moonshine_jitter_destroy(handle);
}

void TestExportVideoCaps()
{
    std::cout << "[Test] C-ABI moonshine_video_query_caps..." << std::endl;
    MoonshineDecoderCaps caps{};
    int res = moonshine_video_query_caps(&caps);
    TEST_ASSERT(res == 0);
    TEST_ASSERT(caps.supports_hevc == 0 || caps.supports_hevc == 1);
    TEST_ASSERT(caps.supports_h264 == 0 || caps.supports_h264 == 1);
    TEST_ASSERT(caps.supports_av1 == 0 || caps.supports_av1 == 1);

    // Null safety
    TEST_ASSERT(moonshine_video_query_caps(nullptr) != 0);
}

void TestExportSlotReturnLifecycle()
{
    std::cout << "[Test] C-ABI moonshine_slot_return lifecycle and robustness..." << std::endl;

    // 1. Null handle robustness
    TEST_ASSERT(moonshine_slot_return_enqueue(nullptr, 42) == 0);
    int32_t out_slot = -999;
    TEST_ASSERT(moonshine_slot_return_dequeue(nullptr, &out_slot) == 0);
    TEST_ASSERT(moonshine_slot_return_size(nullptr) == 0);
    moonshine_slot_return_destroy(nullptr); // Safe no-op

    // 2. Normal lifecycle
    MoonshineRingBufferHandle handle = moonshine_slot_return_create(4);
    TEST_ASSERT(handle != nullptr);
    TEST_ASSERT(moonshine_slot_return_size(handle) == 0);

    // Empty dequeue
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 0);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, nullptr) == 0);

    // Enqueue items
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 101) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 102) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 103) == 1);
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 104) == 1);
    TEST_ASSERT(moonshine_slot_return_size(handle) == 4);

    // Full queue rejection
    TEST_ASSERT(moonshine_slot_return_enqueue(handle, 105) == 0);

    // Dequeue in FIFO order
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 101);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 102);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 103);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 1);
    TEST_ASSERT(out_slot == 104);

    TEST_ASSERT(moonshine_slot_return_size(handle) == 0);
    TEST_ASSERT(moonshine_slot_return_dequeue(handle, &out_slot) == 0);

    moonshine_slot_return_destroy(handle);
}

void TestExportAbiStructLayoutsAndErrorCodes()
{
    std::cout << "[Test] C-ABI struct layouts and error code verification..." << std::endl;

    // Error code verification
    TEST_ASSERT(MOONSHINE_SUCCESS == 0);
    TEST_ASSERT(MOONSHINE_ERR_INVALID_ARGUMENT == -1);
    TEST_ASSERT(MOONSHINE_ERR_OUT_OF_MEMORY == -2);
    TEST_ASSERT(MOONSHINE_ERR_UNSUPPORTED_HARDWARE == -3);
    TEST_ASSERT(MOONSHINE_ERR_DEVICE_LOST == -4);
    TEST_ASSERT(MOONSHINE_ERR_BUFFER_TOO_SMALL == -5);
    TEST_ASSERT(MOONSHINE_ERR_TIMEOUT == -6);
    TEST_ASSERT(MOONSHINE_ERR_TRANSIENT_BUSY == -7);
    TEST_ASSERT(MOONSHINE_ERR_USE_AFTER_FREE == -8);
    TEST_ASSERT(MOONSHINE_ERR_DOUBLE_RELEASE == -9);
    TEST_ASSERT(MOONSHINE_ERR_NOT_INITIALIZED == -10);
    TEST_ASSERT(MOONSHINE_ERR_FATAL == -11);

    // Byte size checks for all 18 C-ABI structs
    TEST_ASSERT(sizeof(MoonshinePacketDesc) == 32);
    TEST_ASSERT(sizeof(MoonshineFrameDesc) == 24);
    TEST_ASSERT(sizeof(MoonshineDecoderCaps) == 20);
    TEST_ASSERT(sizeof(MoonshineCaptureFrameDesc) == 36);
    TEST_ASSERT(sizeof(MoonshineHdr10Metadata) == 32);
    TEST_ASSERT(sizeof(MoonshineEncoderCaps) == 32);
    TEST_ASSERT(sizeof(MoonshineEncoderConfig) == 32);
    TEST_ASSERT(sizeof(MoonshineEncodedPacketDesc) == 24);
    TEST_ASSERT(sizeof(MoonshineVirtualAudioDriverStatusC) == 44);
    TEST_ASSERT(sizeof(MoonshineAudioIpcMetricsC) == 36);
    TEST_ASSERT(sizeof(MoonshineAdapterInfo) == 160);
    TEST_ASSERT(sizeof(MoonshineDisplayInfo) == 36);
    TEST_ASSERT(sizeof(MoonshineDisplayModeDesc) == 32);
    TEST_ASSERT(sizeof(MoonshineDisplayExtendedInfo) == 152);
    TEST_ASSERT(sizeof(MoonshineVirtualDesktopBoundsC) == 16);
    TEST_ASSERT(sizeof(MoonshineSwapchainMetrics) == 24);
    TEST_ASSERT(sizeof(MoonshineGpuAdapter) == 184);
    TEST_ASSERT(sizeof(MoonshineQsvDiagnosticReport) == 384);

    // Field offset checks
    TEST_ASSERT(offsetof(MoonshinePacketDesc, sequence_number) == 0);
    TEST_ASSERT(offsetof(MoonshinePacketDesc, payload_ptr) == 24);

    TEST_ASSERT(offsetof(MoonshineCaptureFrameDesc, texture_handle) == 0);
    TEST_ASSERT(offsetof(MoonshineCaptureFrameDesc, cursor_visible) == 32);

    TEST_ASSERT(offsetof(MoonshineHdr10Metadata, red_primary) == 0);
    TEST_ASSERT(offsetof(MoonshineHdr10Metadata, hdr_enabled) == 28);

    TEST_ASSERT(offsetof(MoonshineDisplayModeDesc, width) == 0);
    TEST_ASSERT(offsetof(MoonshineDisplayModeDesc, reserved) == 29);

    TEST_ASSERT(offsetof(MoonshineDisplayExtendedInfo, display_index) == 0);
    TEST_ASSERT(offsetof(MoonshineDisplayExtendedInfo, reserved) == 136);

    TEST_ASSERT(offsetof(MoonshineGpuAdapter, index) == 0);
    TEST_ASSERT(offsetof(MoonshineGpuAdapter, description) == 56);

    TEST_ASSERT(offsetof(MoonshineQsvDiagnosticReport, adapter_found) == 0);
    TEST_ASSERT(offsetof(MoonshineQsvDiagnosticReport, reserved) == 368);
}

void TestExportWasapiHandleSafety()
{
    std::cout << "[Test] C-ABI WasapiRenderer SafeHandleStore lifecycle and safety..." << std::endl;

    // 1. Invalid / null handle robustness
    TEST_ASSERT(moonshine_audio_submit_pcm(nullptr, nullptr, 0) == -1);
    float samplePcm[64] = {0};
    TEST_ASSERT(moonshine_audio_submit_pcm(nullptr, samplePcm, 64) == -1);
    void* invalidHandle = reinterpret_cast<void*>(static_cast<uintptr_t>(0xDEADBEEFULL));
    TEST_ASSERT(moonshine_audio_submit_pcm(invalidHandle, samplePcm, 64) == -1);

    uint64_t rendered = 999;
    uint32_t underruns = 999;
    moonshine_audio_get_metrics(nullptr, &rendered, &underruns);
    TEST_ASSERT(rendered == 999);
    moonshine_audio_get_metrics(invalidHandle, &rendered, &underruns);
    TEST_ASSERT(rendered == 999);

    moonshine_audio_destroy(nullptr); // Safe no-op
    moonshine_audio_destroy(invalidHandle); // Safe no-op

    // 2. Real handle lifecycle
    MoonshineAudioHandle handle = moonshine_audio_create_wasapi(48000, 2, 0);
    if (handle) {
        float pcm[256] = {0.1f};
        int submitRes = moonshine_audio_submit_pcm(handle, pcm, 128);
        TEST_ASSERT(submitRes == 0);

        uint64_t frames = 0;
        uint32_t under = 0;
        moonshine_audio_get_metrics(handle, &frames, &under);
        TEST_ASSERT(frames == 128);

        moonshine_audio_destroy(handle);

        // Post-destruction use-after-free protection via SafeHandleStore
        TEST_ASSERT(moonshine_audio_submit_pcm(handle, pcm, 128) == -1);
    }
}

int main()
{
    std::cout << "=== Running C-ABI Export Test Suite ===" << std::endl;
    TestExportVectorXor();
    TestExportSpscLifecycle();
    TestExportSlotReturnLifecycle();
    TestExportJitterLifecycle();
    TestExportVideoCaps();
    TestExportAbiStructLayoutsAndErrorCodes();
    TestExportWasapiHandleSafety();
    std::cout << "All C-ABI Export tests passed successfully." << std::endl;
    return 0;
}

