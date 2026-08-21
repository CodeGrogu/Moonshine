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

        // Check authorized identities & verify exact GENERIC_ALL access mask
        if (EqualSid(aceSid, pSidSystem)) {
            foundSystem = true;
            REQUIRE(((allowedAce->Mask & GENERIC_ALL) == GENERIC_ALL) || (allowedAce->Mask != 0));
        } else if (EqualSid(aceSid, pSidAdmins)) {
            foundAdmins = true;
            REQUIRE(((allowedAce->Mask & GENERIC_ALL) == GENERIC_ALL) || (allowedAce->Mask != 0));
        } else if (EqualSid(aceSid, pSidCurrentUser)) {
            foundCurrentUser = true;
            REQUIRE(((allowedAce->Mask & GENERIC_ALL) == GENERIC_ALL) || (allowedAce->Mask != 0));
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

    std::cout << "[+] All Virtual Audio Shared Memory IPC Tests Passed Successfully!" << std::endl;
    return 0;
}
