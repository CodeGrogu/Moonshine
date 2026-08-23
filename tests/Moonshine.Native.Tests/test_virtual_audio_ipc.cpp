#include <iostream>
#include <vector>
#include <cmath>
#include <cstdlib>
#include "moonshine/audio/virtual_audio_ipc.hpp"

#define REQUIRE(condition) \
    do { \
        if (!(condition)) { \
            std::cerr << "Assertion failed: " #condition " at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::exit(1); \
        } \
    } while (0)

using namespace moonshine::audio;

void Test_ChannelInitialization() {
    std::cout << "[Test] VirtualAudioIpcChannel Initialization..." << std::endl;
    VirtualAudioIpcChannel channel;
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(ok);
    REQUIRE(channel.IsConnected());
    REQUIRE(channel.GetEndpointType() == MOONSHINE_ENDPOINT_RENDER);
    REQUIRE(channel.GetUnderrunCount() == 0);
    REQUIRE(channel.GetOverrunCount() == 0);
    channel.Close();
    REQUIRE(!channel.IsConnected());
    std::cout << "[Pass] VirtualAudioIpcChannel Initialization." << std::endl;
}

void Test_WriteAndReadPcm() {
    std::cout << "[Test] VirtualAudioIpcChannel Write and Read PCM..." << std::endl;
    VirtualAudioIpcChannel producer;
    bool pOk = producer.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(pOk);

#ifdef _WIN32
    VirtualAudioIpcChannel consumer;
    bool cOk = consumer.Initialize(MOONSHINE_ENDPOINT_RENDER, false, 48000, 2);
    REQUIRE(cOk);
#else
    VirtualAudioIpcChannel& consumer = producer;
#endif

    // 480 samples = 10ms of stereo audio (960 floats)
    std::vector<float> src(960);
    for (size_t i = 0; i < src.size(); ++i) {
        src[i] = std::sin(2.0f * 3.14159265f * 440.0f * static_cast<float>(i) / 48000.0f);
    }

    size_t written = producer.WritePcm(src.data(), src.size() * sizeof(float));
    REQUIRE(written == src.size() * sizeof(float));

    std::vector<float> dst(960, 0.0f);
    size_t read = consumer.ReadPcm(dst.data(), dst.size() * sizeof(float));
    REQUIRE(read == dst.size() * sizeof(float));

    for (size_t i = 0; i < src.size(); ++i) {
        REQUIRE(std::fabs(src[i] - dst[i]) < 1e-5f);
    }

    producer.Close();
#ifdef _WIN32
    consumer.Close();
#endif
    std::cout << "[Pass] VirtualAudioIpcChannel Write and Read PCM." << std::endl;
}

void Test_UnderrunHandling() {
    std::cout << "[Test] VirtualAudioIpcChannel Underrun Handling..." << std::endl;
    VirtualAudioIpcChannel channel;
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(ok);

    std::vector<float> dst(480, 1.0f); // Pre-fill with non-zero
    size_t read = channel.ReadPcm(dst.data(), dst.size() * sizeof(float));
    REQUIRE(read == 0);
    REQUIRE(channel.GetUnderrunCount() == 1);

    // Verify silence zero-padding on underrun
    for (float sample : dst) {
        REQUIRE(sample == 0.0f);
    }

    channel.Close();
    std::cout << "[Pass] VirtualAudioIpcChannel Underrun Handling." << std::endl;
}

void Test_OverrunHandling() {
    std::cout << "[Test] VirtualAudioIpcChannel Overrun Handling..." << std::endl;
    VirtualAudioIpcChannel channel;
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2, MOONSHINE_FORMAT_FLOAT_32, 4);
    REQUIRE(ok);

    // Frame size = 480 * 2 * 4 = 3840 bytes. Capacity = 4 * 3840 = 15360 bytes.
    std::vector<float> bigChunk(480 * 2 * 6, 0.5f); // 6 frames (exceeds capacity)
    size_t written = channel.WritePcm(bigChunk.data(), bigChunk.size() * sizeof(float));
    REQUIRE(written > 0);
    REQUIRE(channel.GetOverrunCount() > 0);

    channel.Close();
    std::cout << "[Pass] VirtualAudioIpcChannel Overrun Handling." << std::endl;
}

void Test_BridgeBidirectionalPumping() {
    std::cout << "[Test] VirtualAudioIpcBridge Bidirectional Pumping..." << std::endl;
    // Driver side initializes owner Render channel
    VirtualAudioIpcChannel driverRenderChannel;
    bool dOk = driverRenderChannel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(dOk);

    VirtualAudioIpcBridge hostBridge;
    bool hOk = hostBridge.Initialize(true, 48000, 2);
    REQUIRE(hOk);
    REQUIRE(hostBridge.IsConnected());

    // Test microphone capture write
    std::vector<float> micInput(960, 0.75f);
    size_t micWritten = hostBridge.WriteCapturePcm(micInput.data(), micInput.size());
    REQUIRE(micWritten == micInput.size());

    // Test render read (with partial/simulated frame)
    std::vector<float> renderOutput(960, 0.0f);
    size_t renderRead = hostBridge.ReadRenderPcm(renderOutput.data(), renderOutput.size());
    // In unpumped render ring, underrun returns 0 and pads with silence
    REQUIRE(renderRead == 0);

    VirtualAudioIpcMetrics metrics = hostBridge.GetMetrics();
    REQUIRE(metrics.capturePacketsWritten > 0);
    REQUIRE(metrics.renderUnderruns > 0);
    REQUIRE(metrics.sampleRate == 48000);
    REQUIRE(metrics.channels == 2);
    REQUIRE(metrics.isConnected == 1);

    hostBridge.Shutdown();
    driverRenderChannel.Close();
    std::cout << "[Pass] VirtualAudioIpcBridge Bidirectional Pumping." << std::endl;
}

void Test_MmcssScheduling() {
    std::cout << "[Test] VirtualAudioIpcBridge MMCSS Scheduling..." << std::endl;
    VirtualAudioIpcChannel driverRenderChannel;
    bool dOk = driverRenderChannel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(dOk);

    VirtualAudioIpcBridge bridge;
    bool bOk = bridge.Initialize(true, 48000, 2);
    REQUIRE(bOk);

    bool mmcssOk = bridge.EnableMmcss();
    (void)mmcssOk;
    bridge.RevertMmcss();

    bridge.Shutdown();
    driverRenderChannel.Close();
    std::cout << "[Pass] VirtualAudioIpcBridge MMCSS Scheduling." << std::endl;
}

#ifdef _WIN32
#include <aclapi.h>
#include <sddl.h>

void VerifyKernelObjectDacl(HANDLE handle, const char* objectName) {
    std::cout << "  Verifying DACL on " << objectName << "..." << std::endl;
    REQUIRE(handle != nullptr);

    PSECURITY_DESCRIPTOR pSD = nullptr;
    PACL pDacl = nullptr;
    DWORD res = GetSecurityInfo(
        handle,
        SE_KERNEL_OBJECT,
        DACL_SECURITY_INFORMATION,
        nullptr,
        nullptr,
        &pDacl,
        nullptr,
        &pSD
    );
    REQUIRE(res == ERROR_SUCCESS);
    REQUIRE(pSD != nullptr);
    REQUIRE(pDacl != nullptr);

    std::wstring currentUserSidStr;
    REQUIRE(VirtualAudioIpcChannel::GetCurrentUserSidString(currentUserSidStr));

    PSID pSidSystem = nullptr;
    ConvertStringSidToSidW(L"S-1-5-18", &pSidSystem); // SYSTEM
    PSID pSidAdmins = nullptr;
    ConvertStringSidToSidW(L"S-1-5-32-544", &pSidAdmins); // Builtin Administrators
    PSID pSidCurrentUser = nullptr;
    ConvertStringSidToSidW(currentUserSidStr.c_str(), &pSidCurrentUser); // Current User

    PSID pSidEveryone = nullptr;
    ConvertStringSidToSidW(L"S-1-1-0", &pSidEveryone); // Everyone
    PSID pSidAuthUsers = nullptr;
    ConvertStringSidToSidW(L"S-1-5-11", &pSidAuthUsers); // Authenticated Users
    PSID pSidUsers = nullptr;
    ConvertStringSidToSidW(L"S-1-5-32-545", &pSidUsers); // Users
    PSID pSidAppPackages = nullptr;
    ConvertStringSidToSidW(L"S-1-15-2-1", &pSidAppPackages); // All App Packages

    bool foundSystem = false;
    bool foundAdmins = false;
    bool foundCurrentUser = false;
    DWORD expectedFullAccessMask = std::strcmp(objectName, "FileMapping (Shared Memory)") == 0
        ? SECTION_ALL_ACCESS
        : EVENT_ALL_ACCESS;

    REQUIRE(pDacl->AceCount == 3);

    for (DWORD i = 0; i < pDacl->AceCount; ++i) {
        LPVOID pAce = nullptr;
        REQUIRE(GetAce(pDacl, i, &pAce));
        auto* aceHeader = static_cast<ACE_HEADER*>(pAce);

        // Verify no inheritance
        REQUIRE((aceHeader->AceFlags & INHERITED_ACE) == 0);
        REQUIRE(aceHeader->AceType == ACCESS_ALLOWED_ACE_TYPE);

        auto* allowedAce = static_cast<ACCESS_ALLOWED_ACE*>(pAce);
        auto* aceSid = reinterpret_cast<PSID>(&allowedAce->SidStart);

        // Assert strictly prohibited identities do NOT exist
        REQUIRE(!EqualSid(aceSid, pSidEveryone));
        REQUIRE(!EqualSid(aceSid, pSidAuthUsers));
        REQUIRE(!EqualSid(aceSid, pSidUsers));
        REQUIRE(!EqualSid(aceSid, pSidAppPackages));

        // Windows expands the SDDL GA token into the exact full-control mask for the kernel object type.
        if (EqualSid(aceSid, pSidSystem)) {
            foundSystem = true;
            REQUIRE(allowedAce->Mask == expectedFullAccessMask);
        } else if (EqualSid(aceSid, pSidAdmins)) {
            foundAdmins = true;
            REQUIRE(allowedAce->Mask == expectedFullAccessMask);
        } else if (EqualSid(aceSid, pSidCurrentUser)) {
            foundCurrentUser = true;
            REQUIRE(allowedAce->Mask == expectedFullAccessMask);
        } else {
            // No unrecognized identities allowed in strict Current User + SYSTEM + Builtin Administrators policy
            std::cerr << "Unexpected SID detected in kernel object DACL at ACE " << i << std::endl;
            REQUIRE(false);
        }
    }

    REQUIRE(foundSystem);
    REQUIRE(foundAdmins);
    REQUIRE(foundCurrentUser);

    LocalFree(pSidSystem);
    LocalFree(pSidAdmins);
    LocalFree(pSidCurrentUser);
    LocalFree(pSidEveryone);
    LocalFree(pSidAuthUsers);
    LocalFree(pSidUsers);
    LocalFree(pSidAppPackages);
    LocalFree(pSD);
}

void Test_KernelObjectSecurityDacl() {
    std::cout << "[Test] VirtualAudioIpc Kernel Object DACL Verification (GetSecurityInfo)..." << std::endl;
    VirtualAudioIpcChannel channel;
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2);
    REQUIRE(ok);

    VerifyKernelObjectDacl(channel.GetFileMappingHandle(), "FileMapping (Shared Memory)");
    VerifyKernelObjectDacl(channel.GetSyncEventHandle(), "SyncEvent (Synchronization Event)");

    channel.Close();
    std::cout << "[Pass] VirtualAudioIpc Kernel Object DACL Verification." << std::endl;
}

void Test_NonOwnerMissingObjectFailsDeterministically() {
    std::cout << "[Test] Non-Owner Missing Object Fails Deterministically..." << std::endl;
    VirtualAudioIpcChannel clientChannel;
    // Attempt to open capture endpoint when no owner channel has created it
    bool ok = clientChannel.Initialize(MOONSHINE_ENDPOINT_CAPTURE, false, 48000, 2);
    REQUIRE(!ok);
    REQUIRE(!clientChannel.IsConnected());
    std::cout << "[Pass] Non-Owner Missing Object Fails Deterministically." << std::endl;
}
#endif

void Test_RingBufferWraparound() {
    std::cout << "[Test] VirtualAudioIpcChannel Ring Buffer Wraparound..." << std::endl;
    VirtualAudioIpcChannel channel;
    // Small ring buffer: 4 frames to force wraparound quickly
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, 48000, 2, MOONSHINE_FORMAT_FLOAT_32, 4);
    REQUIRE(ok);

    // Each frame is 10ms of stereo float32 = 480 * 2 * 4 = 3840 bytes
    std::vector<float> src(480 * 2);
    for (size_t i = 0; i < src.size(); ++i) {
        src[i] = static_cast<float>(i) / static_cast<float>(src.size());
    }

    // Write 3 frames, read 3 frames, write 3 more (forces wraparound)
    for (int batch = 0; batch < 2; ++batch) {
        for (int frame = 0; frame < 3; ++frame) {
            size_t written = channel.WritePcm(src.data(), src.size() * sizeof(float));
            REQUIRE(written == src.size() * sizeof(float));
        }

        std::vector<float> dst(480 * 2, 0.0f);
        for (int frame = 0; frame < 3; ++frame) {
            size_t read = channel.ReadPcm(dst.data(), dst.size() * sizeof(float));
            REQUIRE(read == dst.size() * sizeof(float));
            // Verify first and last sample match source
            REQUIRE(std::fabs(dst[0] - src[0]) < 1e-5f);
            REQUIRE(std::fabs(dst[dst.size() - 1] - src[src.size() - 1]) < 1e-5f);
        }
    }

    channel.Close();
    std::cout << "[Pass] VirtualAudioIpcChannel Ring Buffer Wraparound." << std::endl;
}

void Test_MultipleFormatConfigurations() {
    std::cout << "[Test] VirtualAudioIpcChannel Multiple Format Configurations..." << std::endl;

    struct TestConfig {
        uint32_t sampleRate;
        uint32_t channels;
        MoonshineAudioSampleFormat format;
    };

    TestConfig configs[] = {
        { 44100, 1, MOONSHINE_FORMAT_PCM_16 },
        { 48000, 2, MOONSHINE_FORMAT_FLOAT_32 },
        { 96000, 6, MOONSHINE_FORMAT_PCM_32 },
        { 192000, 8, MOONSHINE_FORMAT_PCM_24 },
    };

    for (const auto& cfg : configs) {
        VirtualAudioIpcChannel channel;
        bool ok = channel.Initialize(MOONSHINE_ENDPOINT_RENDER, true, cfg.sampleRate, cfg.channels, cfg.format, 4);
        REQUIRE(ok);
        REQUIRE(channel.IsConnected());
        REQUIRE(channel.GetAvailableReadBytes() == 0);
        REQUIRE(channel.GetAvailableWriteBytes() > 0);
        channel.Close();
    }

    std::cout << "[Pass] VirtualAudioIpcChannel Multiple Format Configurations." << std::endl;
}

void Test_BridgeDisconnectedRenderUnderrunTracking() {
    std::cout << "[Test] VirtualAudioIpcBridge Disconnected Render Underrun Tracking..." << std::endl;
    VirtualAudioIpcBridge bridge;
    bool ok = bridge.Initialize(true, 48000, 2);
    // Bridge may partially connect (capture channel uses heap fallback)
    (void)ok;

    std::vector<float> renderBuf(960, 0.0f);
    // Multiple reads without a render channel owner should count underruns
    for (int i = 0; i < 3; ++i) {
        bridge.ReadRenderPcm(renderBuf.data(), renderBuf.size());
    }

    VirtualAudioIpcMetrics metrics = bridge.GetMetrics();
    REQUIRE(metrics.renderUnderruns >= 3);

    // Verify silence fill
    for (float sample : renderBuf) {
        REQUIRE(sample == 0.0f);
    }

    bridge.Shutdown();
    std::cout << "[Pass] VirtualAudioIpcBridge Disconnected Render Underrun Tracking." << std::endl;
}

void Test_AvailableBytesAccounting() {
    std::cout << "[Test] VirtualAudioIpcChannel Available Bytes Accounting..." << std::endl;
    VirtualAudioIpcChannel channel;
    bool ok = channel.Initialize(MOONSHINE_ENDPOINT_CAPTURE, true, 48000, 2, MOONSHINE_FORMAT_FLOAT_32, 8);
    REQUIRE(ok);

    uint32_t initialWrite = channel.GetAvailableWriteBytes();
    uint32_t initialRead = channel.GetAvailableReadBytes();
    REQUIRE(initialRead == 0);
    REQUIRE(initialWrite > 0);

    // Write one frame
    std::vector<float> frame(480 * 2, 0.25f);
    size_t written = channel.WritePcm(frame.data(), frame.size() * sizeof(float));
    REQUIRE(written == frame.size() * sizeof(float));

    uint32_t afterWriteRead = channel.GetAvailableReadBytes();
    uint32_t afterWriteWrite = channel.GetAvailableWriteBytes();
    REQUIRE(afterWriteRead == static_cast<uint32_t>(frame.size() * sizeof(float)));
    REQUIRE(afterWriteWrite < initialWrite);

    // Read it back
    std::vector<float> readBuf(480 * 2, 0.0f);
    size_t bytesRead = channel.ReadPcm(readBuf.data(), readBuf.size() * sizeof(float));
    REQUIRE(bytesRead == readBuf.size() * sizeof(float));

    uint32_t afterReadRead = channel.GetAvailableReadBytes();
    REQUIRE(afterReadRead == 0);

    // Verify data integrity
    for (size_t i = 0; i < frame.size(); ++i) {
        REQUIRE(std::fabs(readBuf[i] - 0.25f) < 1e-5f);
    }

    channel.Close();
    std::cout << "[Pass] VirtualAudioIpcChannel Available Bytes Accounting." << std::endl;
}

int main() {
    std::cout << "==========================================================" << std::endl;
    std::cout << "Moonshine Virtual Audio Shared Memory IPC Test Suite" << std::endl;
    std::cout << "==========================================================" << std::endl;

    Test_ChannelInitialization();
    Test_WriteAndReadPcm();
    Test_UnderrunHandling();
    Test_OverrunHandling();
    Test_BridgeBidirectionalPumping();
    Test_MmcssScheduling();
#ifdef _WIN32
    Test_KernelObjectSecurityDacl();
    Test_NonOwnerMissingObjectFailsDeterministically();
#endif
    Test_RingBufferWraparound();
    Test_MultipleFormatConfigurations();
    Test_BridgeDisconnectedRenderUnderrunTracking();
    Test_AvailableBytesAccounting();

    std::cout << "[+] All Virtual Audio Shared Memory IPC Tests Passed Successfully!" << std::endl;
    return 0;
}
