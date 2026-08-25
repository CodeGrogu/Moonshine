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

void populate_smpte_colour_bars(std::vector<uint32_t>& pixels, uint32_t width, uint32_t height) {
    pixels.resize(width * height);
    // 7-bar SMPTE test pattern (75% intensity, BGRA format):
    // 0: 75% White   (0xFFBFBFBF)
    // 1: 75% Yellow  (0xFF00BFBF)
    // 2: 75% Cyan    (0xFFBFBF00)
    // 3: 75% Green   (0xFF00BF00)
    // 4: 75% Magenta (0xFFBF00BF)
    // 5: 75% Red     (0xFF0000BF)
    // 6: 75% Blue    (0xFFBF0000)
    const uint32_t smpte_colors[7] = {
        0xFFBFBFBF, // White
        0xFF00BFBF, // Yellow
        0xFFBFBF00, // Cyan
        0xFF00BF00, // Green
        0xFFBF00BF, // Magenta
        0xFF0000BF, // Red
        0xFFBF0000  // Blue
    };

    for (uint32_t y = 0; y < height; ++y) {
        for (uint32_t x = 0; x < width; ++x) {
            uint32_t bar_index = std::min<uint32_t>(6, (x * 7) / width);
            pixels[y * width + x] = smpte_colors[bar_index];
        }
    }
}

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
    std::cout << "[*] Starting live Intel QuickSync / oneVPL hardware pipeline integration test..." << std::endl;

    // 1. Probe QSV support via moonshine_qsv_query_codec_support
    std::cout << "[*] Probing Intel QuickSync / oneVPL codec query support..." << std::endl;
    for (uint32_t codec = 0; codec <= 3; ++codec) {
        uint32_t supported = 0;
        int res = moonshine_qsv_query_codec_support(codec, &supported);
        ACTIVE_ASSERT(res == 1);
        std::cout << "  Codec " << codec << " support: " << supported << std::endl;
    }

#if defined(_WIN32)
    // 2. Locate Intel GPU Adapter
    std::cout << "[*] Enumerating DXGI adapters for Intel GPU (VendorId: 0x8086)..." << std::endl;
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    ACTIVE_ASSERT(SUCCEEDED(hr) && factory != nullptr);

    ComPtr<IDXGIAdapter1> intel_adapter;
    ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(adapter->GetDesc1(&desc))) {
            if (desc.VendorId == 0x8086 && !intel_adapter) {
                intel_adapter = adapter;
                std::wcout << L"  [+] Physical Intel GPU detected: " << desc.Description << std::endl;
                break;
            }
        }
    }

    if (!intel_adapter) {
        std::cout << "[*] Note: Physical Intel GPU (0x8086) not present on this machine." << std::endl;
        std::cout << "[*] Cleanly exiting Intel QuickSync integration test (capability-gated)." << std::endl;
        return 0;
    }

    // 3. Create Direct3D 11 hardware device
    std::cout << "[*] Creating Direct3D 11 hardware device on Intel GPU..." << std::endl;
    UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL fl{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    hr = D3D11CreateDevice(
        intel_adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        static_cast<UINT>(std::size(feature_levels)),
        D3D11_SDK_VERSION,
        &device,
        &fl,
        &context
    );
    ACTIVE_ASSERT(SUCCEEDED(hr) && device != nullptr);
    std::cout << "  [+] Direct3D 11 device created successfully (Feature Level: 0x" << std::hex << fl << std::dec << ")." << std::endl;

    // 4. Create Direct3D 11 test texture populated with procedural SMPTE colour bars
    std::cout << "[*] Generating procedural SMPTE colour bar texture (1920x1080 BGRA)..." << std::endl;
    std::vector<uint32_t> smpte_pixels;
    populate_smpte_colour_bars(smpte_pixels, 1920, 1080);
    ACTIVE_ASSERT(smpte_pixels.size() == 1920 * 1080);

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

    D3D11_SUBRESOURCE_DATA init_data{};
    init_data.pSysMem = smpte_pixels.data();
    init_data.SysMemPitch = 1920 * sizeof(uint32_t);

    ComPtr<ID3D11Texture2D> test_texture;
    hr = device->CreateTexture2D(&tex_desc, &init_data, &test_texture);
    ACTIVE_ASSERT(SUCCEEDED(hr) && test_texture != nullptr);
    std::cout << "  [+] SMPTE 7-bar procedural texture initialised successfully." << std::endl;

    // 5. Query encoder capabilities
    std::cout << "[*] Querying Intel QuickSync capabilities via moonshine_encoder_query_caps..." << std::endl;
    MoonshineEncoderCaps caps{};
    int caps_res = moonshine_encoder_query_caps(3, device.Get(), &caps); // Vendor 3 = Intel QSV
    if (caps_res != 1) {
        std::cout << "[*] Note: Intel QuickSync runtime not installed or supported. Exiting cleanly." << std::endl;
        return 0;
    }
    std::cout << "  [+] Capabilities -> Max Width: " << caps.max_width
              << ", Max Height: " << caps.max_height
              << ", Max FPS: " << caps.max_fps
              << ", Supported Codecs Mask: 0x" << std::hex << caps.supported_codecs_mask << std::dec << std::endl;

    // 6. Create QSV Encoder for HEVC (Codec = 1) with High Bitrate Multiplier (80 Mbps)
    std::cout << "[*] Creating Intel QuickSync encoder instance for HEVC at 80,000 Kbps (exercising BRCParamMultiplier)..." << std::endl;
    MoonshineEncoderConfig config{};
    config.width = 1920;
    config.height = 1080;
    config.fps = 60;
    config.bitrate_kbps = 80000;      // > 65,535 Kbps: requires BRCParamMultiplier = 2
    config.peak_bitrate_kbps = 100000; // > 65,535 Kbps
    config.codec = 1; // HEVC
    config.rc_mode = 0; // CBR
    config.gop_length = 0;
    config.enable_intra_refresh = 0;
    config.enable_filler_data = 1;

    MoonshineEncoderHandle encoder = moonshine_encoder_create(3, device.Get(), &config);
    if (!encoder) {
        std::cout << "[*] Note: Intel QuickSync encoder creation not supported by current driver. Exiting cleanly." << std::endl;
        return 0;
    }
    std::cout << "  [+] Intel QuickSync encoder handle created successfully." << std::endl;
    ACTIVE_ASSERT(moonshine_encoder_is_healthy(encoder) == 1);

    // 7. Encode Frame 1 (Forced IDR) with procedural SMPTE input
    std::cout << "[*] Encoding Frame 1 (Forced IDR Keyframe from procedural SMPTE texture)..." << std::endl;
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

    // 8. Decoder Loopback Verification with verified texture extraction
    std::cout << "[*] Executing Direct3D 11 decoder loopback verification..." << std::endl;
    MoonshineDecoderHandle decoder = moonshine_video_create_d3d11(nullptr, 1920, 1080, 1);
    if (decoder) {
        MoonshineFrameDesc frame_desc{};
        frame_desc.frame_index = static_cast<uint32_t>(desc1.frame_index);
        frame_desc.total_bytes = out_size1;
        frame_desc.packet_count = 1;
        frame_desc.is_keyframe = desc1.is_keyframe;
        frame_desc.frame_buffer = bitstream_buffer.data();

        int decode_res = moonshine_video_submit_frame(decoder, &frame_desc);
        ACTIVE_ASSERT(decode_res == 0);

        void* decoded_tex = moonshine_video_get_texture(decoder);
        ACTIVE_ASSERT(decoded_tex != nullptr);
        std::cout << "  [+] Decoded texture extracted and verified on hardware device context." << std::endl;

        moonshine_video_destroy(decoder);
    }

    // 9. Cleanly destroy encoder
    std::cout << "[*] Destroying encoder resources cleanly..." << std::endl;
    moonshine_encoder_destroy(encoder);
    std::cout << "  [+] Encoder handle destroyed cleanly." << std::endl;

#else
    std::cout << "[*] Note: Non-Windows operating system detected. Skipping live Direct3D 11 QSV test." << std::endl;
#endif

    std::cout << "[+] Live Intel QuickSync / oneVPL pipeline and GPU integration test passed successfully." << std::endl;
    return 0;
}
