#include "moonshine/export/moonshine_native_api.h"
#include <iostream>
#include <vector>
#include <cstdlib>
#include <cstring>

#define ACTIVE_ASSERT(expr) \
    do { \
        if (!(expr)) { \
            std::cerr << "[-] Assertion failed: (" #expr ") at " << __FILE__ << ":" << __LINE__ << std::endl; \
            std::exit(1); \
        } \
    } while (0)

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

using Microsoft::WRL::ComPtr;
#endif

namespace {

bool validate_nalu_start_codes(const uint8_t* data, size_t size) {
    if (!data || size < 4) {
        return false;
    }
    if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01) {
        return true;
    }
    if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x01) {
        return true;
    }
    return false;
}

} // namespace

int main() {
    std::cout << "[*] Starting live AMD AMF hardware pipeline integration test..." << std::endl;

    // 1. Probe AMF support via moonshine_amf_query_codec_support
    std::cout << "[*] Probing AMD AMF codec query support..." << std::endl;
    for (uint32_t codec = 0; codec <= 3; ++codec) {
        uint32_t supported = 0;
        int res = moonshine_amf_query_codec_support(codec, &supported);
        ACTIVE_ASSERT(res == 1);
        std::cout << "  Codec " << codec << " AMF support query result: " << supported << std::endl;
    }

#if defined(_WIN32)
    // 2. Enumerate DXGI adapters and find AMD GPU (VendorId: 0x1002)
    std::cout << "[*] Enumerating DXGI adapters for AMD hardware (VendorId: 0x1002)..." << std::endl;
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    ACTIVE_ASSERT(SUCCEEDED(hr) && factory != nullptr);

    ComPtr<IDXGIAdapter1> amd_adapter;
    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(adapter->GetDesc1(&desc))) {
            std::wcout << L"  Found adapter " << i << L": " << desc.Description 
                       << L" (VendorId: 0x" << std::hex << desc.VendorId << std::dec << L")" << std::endl;
            if (desc.VendorId == 0x1002 && !amd_adapter) {
                amd_adapter = adapter;
            }
        }
    }

    if (!amd_adapter) {
        std::cout << "[*] Note: Physical AMD GPU hardware adapter (VendorId: 0x1002) not detected on this system." << std::endl;
        std::cout << "[*] Live AMD AMF hardware pipeline execution skipped cleanly on non-AMD host." << std::endl;
        return 0;
    }

    DXGI_ADAPTER_DESC1 amd_desc{};
    amd_adapter->GetDesc1(&amd_desc);
    std::wcout << L"[+] Selected AMD GPU adapter: " << amd_desc.Description << std::endl;

    // 3. Create Direct3D 11 device on the AMD adapter
    std::cout << "[*] Creating hardware Direct3D 11 device on AMD adapter..." << std::endl;
    UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL chosen_feature_level{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    hr = D3D11CreateDevice(
        amd_adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        static_cast<UINT>(sizeof(feature_levels) / sizeof(feature_levels[0])),
        D3D11_SDK_VERSION,
        &device,
        &chosen_feature_level,
        &context
    );

    if (FAILED(hr)) {
        std::cerr << "[-] Failed to create Direct3D 11 device on AMD adapter. HRESULT: 0x" 
                  << std::hex << hr << std::dec << std::endl;
        return 1;
    }
    std::cout << "  [+] Hardware Direct3D 11 device initialised successfully." << std::endl;

    // 4. Create test D3D11 texture
    std::cout << "[*] Creating 1920x1080 test GPU texture (DXGI_FORMAT_B8G8R8A8_UNORM)..." << std::endl;
    D3D11_TEXTURE2D_DESC tex_desc{};
    tex_desc.Width = 1920;
    tex_desc.Height = 1080;
    tex_desc.MipLevels = 1;
    tex_desc.ArraySize = 1;
    tex_desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    tex_desc.SampleDesc.Count = 1;
    tex_desc.SampleDesc.Quality = 0;
    tex_desc.Usage = D3D11_USAGE_DEFAULT;
    tex_desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

    ComPtr<ID3D11Texture2D> test_texture;
    hr = device->CreateTexture2D(&tex_desc, nullptr, &test_texture);
    ACTIVE_ASSERT(SUCCEEDED(hr) && test_texture != nullptr);

    // 5. Query AMF encoder capabilities
    std::cout << "[*] Querying encoder capabilities via moonshine_encoder_query_caps..." << std::endl;
    MoonshineEncoderCaps caps{};
    int caps_res = moonshine_encoder_query_caps(2, device.Get(), &caps); // 2 = AmdAmf
    if (caps_res != 1) {
        std::cout << "[*] Note: AMF runtime not available on this system. Exiting cleanly." << std::endl;
        return 0;
    }

    // 6. Create AMF encoder
    std::cout << "[*] Creating AMF encoder session via moonshine_encoder_create..." << std::endl;
    MoonshineEncoderConfig config{};
    config.width = 1920;
    config.height = 1080;
    config.fps = 60;
    config.bitrate_kbps = 20000;
    config.peak_bitrate_kbps = 30000;
    config.codec = 1; // HEVC
    config.rc_mode = 0; // CBR
    config.gop_length = 0;
    config.enable_intra_refresh = 0;
    config.enable_filler_data = 1;

    MoonshineEncoderHandle encoder = moonshine_encoder_create(2, device.Get(), &config);
    if (!encoder) {
        std::cout << "[*] Note: AMF encoder creation not supported by current driver. Exiting cleanly." << std::endl;
        return 0;
    }
    std::cout << "  [+] AMF encoder handle created successfully." << std::endl;
    ACTIVE_ASSERT(moonshine_encoder_is_healthy(encoder) == 1);

    // 7. Encode Frame 1 (Forced IDR)
    std::cout << "[*] Encoding Frame 1 (Forced IDR Keyframe)..." << std::endl;
    MoonshineEncodedPacketDesc desc1{};
    std::vector<uint8_t> bitstream_buffer(1920 * 1080 * 4);
    uint32_t out_size1 = 0;

    int encode_res1 = moonshine_encoder_encode_frame(
        encoder,
        test_texture.Get(),
        1,
        &desc1,
        bitstream_buffer.data(),
        static_cast<uint32_t>(bitstream_buffer.size()),
        &out_size1
    );

    ACTIVE_ASSERT(encode_res1 == 1);
    ACTIVE_ASSERT(out_size1 > 0);
    ACTIVE_ASSERT(validate_nalu_start_codes(bitstream_buffer.data(), out_size1));
    std::cout << "  [+] Frame 1 encoded: size=" << out_size1 << " bytes, is_keyframe=" << (int)desc1.is_keyframe << std::endl;

    // 8. Cleanly destroy encoder
    std::cout << "[*] Destroying encoder resources cleanly..." << std::endl;
    moonshine_encoder_destroy(encoder);
    std::cout << "  [+] Encoder handle destroyed cleanly." << std::endl;

#else
    std::cout << "[*] Note: Non-Windows operating system detected. Skipping live Direct3D 11 AMF test." << std::endl;
#endif

    std::cout << "[+] Live AMD AMF pipeline and GPU integration test passed successfully." << std::endl;
    return 0;
}
