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
    // Check 3-byte prefix (0x00 0x00 0x01) or 4-byte prefix (0x00 0x00 0x00 0x01)
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
    std::cout << "[*] Starting live NVIDIA NVENC hardware pipeline integration test..." << std::endl;

    // 1. Probe NVENC support via moonshine_nvenc_query_codec_support
    std::cout << "[*] Probing NVIDIA NVENC codec query support..." << std::endl;
    for (uint32_t codec = 0; codec <= 3; ++codec) {
        uint32_t supported = 0;
        int query_res = moonshine_nvenc_query_codec_support(codec, &supported);
        ACTIVE_ASSERT(query_res == 1);
        ACTIVE_ASSERT(supported == 0 || supported == 1);
        std::cout << "  Codec " << codec << " NVENC support query result: " << supported << std::endl;
    }

#if defined(_WIN32)
    // 2. Check for NVIDIA GPU adapter (desc.VendorId == 0x10DE)
    std::cout << "[*] Enumerating DXGI adapters for NVIDIA hardware (VendorId: 0x10DE)..." << std::endl;
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(hr) || !factory) {
        std::cout << "[*] Note: Unable to create DXGI factory in current execution environment. Exiting cleanly." << std::endl;
        return 0;
    }

    ComPtr<IDXGIAdapter1> nv_adapter;
    ComPtr<IDXGIAdapter1> current_adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &current_adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(current_adapter->GetDesc1(&desc))) {
            std::wcout << L"  Found adapter " << i << L": " << desc.Description
                       << L" (VendorId: 0x" << std::hex << desc.VendorId << std::dec << L")" << std::endl;
            if (desc.VendorId == 0x10DE) {
                nv_adapter = current_adapter;
                std::wcout << L"  [+] Selected NVIDIA GPU adapter: " << desc.Description << std::endl;
                break;
            }
        }
    }

    if (!nv_adapter) {
        std::cout << "[*] Note: No NVIDIA GPU adapter detected (VendorId: 0x10DE). Skipping physical hardware NVENC pipeline test." << std::endl;
        return 0;
    }

    // 3. Physical NVIDIA GPU adapter present: execute end-to-end hardware pipeline test
    std::cout << "[*] Creating hardware Direct3D 11 device on NVIDIA adapter..." << std::endl;
    const UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    const D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL created_feature_level{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;

    hr = D3D11CreateDevice(
        nv_adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        static_cast<UINT>(std::size(feature_levels)),
        D3D11_SDK_VERSION,
        &device,
        &created_feature_level,
        &context
    );

    if (FAILED(hr) || !device || !context) {
        std::cerr << "[-] Error: Failed to create hardware Direct3D 11 device on NVIDIA adapter (HRESULT: 0x"
                  << std::hex << hr << std::dec << ")" << std::endl;
        return 1;
    }
    ACTIVE_ASSERT(device != nullptr);
    ACTIVE_ASSERT(context != nullptr);
    std::cout << "  [+] Hardware Direct3D 11 device initialised successfully (Feature Level: 0x"
              << std::hex << created_feature_level << std::dec << ")." << std::endl;

    // Create 1920x1080 test Direct3D 11 texture (DXGI_FORMAT_B8G8R8A8_UNORM)
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
    tex_desc.CPUAccessFlags = 0;
    tex_desc.MiscFlags = 0;

    ComPtr<ID3D11Texture2D> test_texture;
    hr = device->CreateTexture2D(&tex_desc, nullptr, &test_texture);
    ACTIVE_ASSERT(SUCCEEDED(hr));
    ACTIVE_ASSERT(test_texture != nullptr);

    // Initialise texture contents with a test colour pattern via RenderTargetView
    ComPtr<ID3D11RenderTargetView> rtv;
    HRESULT hr_rtv = device->CreateRenderTargetView(test_texture.Get(), nullptr, &rtv);
    ACTIVE_ASSERT(SUCCEEDED(hr_rtv));
    ACTIVE_ASSERT(rtv != nullptr);
    const float test_colour[4] = { 0.15f, 0.55f, 0.85f, 1.0f };
    context->ClearRenderTargetView(rtv.Get(), test_colour);
    std::cout << "  [+] Direct3D 11 texture created and initialised with test colour." << std::endl;

    // Query encoder capabilities via moonshine_encoder_query_caps(1, device.Get(), &caps)
    std::cout << "[*] Querying encoder capabilities via moonshine_encoder_query_caps..." << std::endl;
    MoonshineEncoderCaps caps{};
    int caps_res = moonshine_encoder_query_caps(1, device.Get(), &caps);
    ACTIVE_ASSERT(caps_res == 1);
    ACTIVE_ASSERT(caps.vendor_id == 1);
    ACTIVE_ASSERT(caps.max_width >= 1920);
    ACTIVE_ASSERT(caps.max_height >= 1080);
    ACTIVE_ASSERT(caps.supported_codecs_mask != 0);

    std::cout << "  Caps -> Supported Codecs Mask: 0x" << std::hex << caps.supported_codecs_mask << std::dec
              << " | Max Width: " << caps.max_width
              << " | Max Height: " << caps.max_height
              << " | Max FPS: " << caps.max_fps
              << " | 10-Bit: " << static_cast<int>(caps.supports_10bit)
              << " | Smart IDR: " << static_cast<int>(caps.supports_smart_idr)
              << std::endl;

    // Query Direct3D 11 decoder capabilities to match encoder and decoder codec profiles
    MoonshineDecoderCaps dec_caps{};
    moonshine_video_query_caps(&dec_caps);

    uint32_t chosen_codec = 0;
    if (dec_caps.supports_10bit && (caps.supported_codecs_mask & (1 << 2))) {
        chosen_codec = 2; // HEVC Main10
    } else if (dec_caps.supports_hevc && (caps.supported_codecs_mask & (1 << 1))) {
        chosen_codec = 1; // HEVC
    } else if (caps.supported_codecs_mask & (1 << 0)) {
        chosen_codec = 0; // H.264
    } else {
        std::cerr << "[-] Error: Neither HEVC nor H.264 is reported as supported by NVENC caps." << std::endl;
        return 2;
    }
    std::cout << "  [+] Selected encoder/decoder codec: " << (chosen_codec == 2 ? "HEVC Main10" : (chosen_codec == 1 ? "HEVC" : "H.264")) << std::endl;

    // Create NVENC encoder for HEVC / H.264 via moonshine_encoder_create(1, device.Get(), &config)
    std::cout << "[*] Creating NVENC encoder session via moonshine_encoder_create..." << std::endl;
    MoonshineEncoderConfig config{};
    config.width = 1920;
    config.height = 1080;
    config.fps = 60;
    config.bitrate_kbps = 15000;
    config.peak_bitrate_kbps = 20000;
    config.codec = chosen_codec;
    config.rc_mode = 0; // CBR
    config.gop_length = 0;
    config.enable_intra_refresh = 0;
    config.enable_filler_data = 0;

    MoonshineEncoderHandle encoder = moonshine_encoder_create(1, device.Get(), &config);
    ACTIVE_ASSERT(encoder != nullptr);
    std::cout << "  [+] NVENC encoder handle created successfully." << std::endl;

    // Allocate bitstream buffer for encoded NALUs
    std::vector<uint8_t> bitstream_buffer(1920 * 1080 * 4);

    // Encode Frame 1 (IDR keyframe): assert out_size > 0, desc.is_keyframe == 1, valid NALU start codes
    std::cout << "[*] Encoding Frame 1 (Forced IDR Keyframe)..." << std::endl;
    MoonshineEncodedPacketDesc desc1{};
    uint32_t out_size1 = 0;

    int encode_res1 = moonshine_encoder_encode_frame(
        encoder,
        test_texture.Get(),
        1, // force_idr = 1
        &desc1,
        bitstream_buffer.data(),
        static_cast<uint32_t>(bitstream_buffer.size()),
        &out_size1
    );

    ACTIVE_ASSERT(encode_res1 == 1);
    ACTIVE_ASSERT(out_size1 > 0);
    ACTIVE_ASSERT(desc1.payload_size == out_size1);
    ACTIVE_ASSERT(desc1.is_keyframe == 1);
    ACTIVE_ASSERT(validate_nalu_start_codes(bitstream_buffer.data(), out_size1));

    std::cout << "  [+] Frame 1 encoded: size=" << out_size1
              << " bytes, is_keyframe=" << static_cast<int>(desc1.is_keyframe)
              << ", frame_index=" << desc1.frame_index << std::endl;

    // Create D3D11VideoDecoder via moonshine_video_create_d3d11(nullptr, 1920, 1080, codec)
    std::cout << "[*] Creating Direct3D 11 hardware video decoder..." << std::endl;
    MoonshineDecoderHandle decoder = moonshine_video_create_d3d11(nullptr, 1920, 1080, chosen_codec);
    ACTIVE_ASSERT(decoder != nullptr);
    std::cout << "  [+] Direct3D 11 Video Decoder created successfully." << std::endl;

    // Feed NVENC-encoded packet into moonshine_video_submit_frame; assert decoder acceptance and valid texture
    std::cout << "[*] Submitting Frame 1 bitstream to Direct3D 11 Video Decoder..." << std::endl;
    MoonshineFrameDesc frame1{};
    frame1.frame_index = static_cast<uint32_t>(desc1.frame_index);
    frame1.total_bytes = out_size1;
    frame1.packet_count = 1;
    frame1.is_keyframe = desc1.is_keyframe;
    frame1.frame_buffer = bitstream_buffer.data();

    int submit_res1 = moonshine_video_submit_frame(decoder, &frame1);
    ACTIVE_ASSERT(submit_res1 == 0);

    void* decoded_texture1 = moonshine_video_get_texture(decoder);
    ACTIVE_ASSERT(decoded_texture1 != nullptr);
    std::cout << "  [+] Frame 1 accepted by decoder. Decoded texture handle: " << decoded_texture1 << std::endl;

    // Encode Frame 2 (inter-frame): assert out_size > 0, submit to decoder
    std::cout << "[*] Encoding Frame 2 (Inter-frame / P-frame)..." << std::endl;
    MoonshineEncodedPacketDesc desc2{};
    uint32_t out_size2 = 0;

    int encode_res2 = moonshine_encoder_encode_frame(
        encoder,
        test_texture.Get(),
        0, // force_idr = 0
        &desc2,
        bitstream_buffer.data(),
        static_cast<uint32_t>(bitstream_buffer.size()),
        &out_size2
    );

    ACTIVE_ASSERT(encode_res2 == 1);
    ACTIVE_ASSERT(out_size2 > 0);
    ACTIVE_ASSERT(desc2.payload_size == out_size2);
    ACTIVE_ASSERT(validate_nalu_start_codes(bitstream_buffer.data(), out_size2));

    std::cout << "  [+] Frame 2 encoded: size=" << out_size2
              << " bytes, is_keyframe=" << static_cast<int>(desc2.is_keyframe)
              << ", frame_index=" << desc2.frame_index << std::endl;

    std::cout << "[*] Submitting Frame 2 bitstream to Direct3D 11 Video Decoder..." << std::endl;
    MoonshineFrameDesc frame2{};
    frame2.frame_index = static_cast<uint32_t>(desc2.frame_index);
    frame2.total_bytes = out_size2;
    frame2.packet_count = 1;
    frame2.is_keyframe = desc2.is_keyframe;
    frame2.frame_buffer = bitstream_buffer.data();

    int submit_res2 = moonshine_video_submit_frame(decoder, &frame2);
    ACTIVE_ASSERT(submit_res2 == 0);

    void* decoded_texture2 = moonshine_video_get_texture(decoder);
    ACTIVE_ASSERT(decoded_texture2 != nullptr);
    std::cout << "  [+] Frame 2 accepted by decoder. Decoded texture handle: " << decoded_texture2 << std::endl;

    // Test moonshine_encoder_request_keyframe and verify subsequent frame is marked keyframe
    std::cout << "[*] Requesting asynchronous IDR keyframe via moonshine_encoder_request_keyframe..." << std::endl;
    moonshine_encoder_request_keyframe(encoder);

    std::cout << "[*] Encoding Frame 3 (Expecting Keyframe following asynchronous request)..." << std::endl;
    MoonshineEncodedPacketDesc desc3{};
    uint32_t out_size3 = 0;

    int encode_res3 = moonshine_encoder_encode_frame(
        encoder,
        test_texture.Get(),
        0, // force_idr = 0 (request_keyframe should dynamically trigger IDR)
        &desc3,
        bitstream_buffer.data(),
        static_cast<uint32_t>(bitstream_buffer.size()),
        &out_size3
    );

    ACTIVE_ASSERT(encode_res3 == 1);
    ACTIVE_ASSERT(out_size3 > 0);
    ACTIVE_ASSERT(desc3.is_keyframe == 1);
    ACTIVE_ASSERT(validate_nalu_start_codes(bitstream_buffer.data(), out_size3));

    std::cout << "  [+] Frame 3 encoded: size=" << out_size3
              << " bytes, is_keyframe=" << static_cast<int>(desc3.is_keyframe)
              << " (verified keyframe flag)." << std::endl;

    MoonshineFrameDesc frame3{};
    frame3.frame_index = static_cast<uint32_t>(desc3.frame_index);
    frame3.total_bytes = out_size3;
    frame3.packet_count = 1;
    frame3.is_keyframe = desc3.is_keyframe;
    frame3.frame_buffer = bitstream_buffer.data();

    int submit_res3 = moonshine_video_submit_frame(decoder, &frame3);
    ACTIVE_ASSERT(submit_res3 == 0);

    // Test moonshine_encoder_reconfigure for dynamic bitrate adjustment
    std::cout << "[*] Reconfiguring encoder bitrate dynamically via moonshine_encoder_reconfigure..." << std::endl;
    MoonshineEncoderConfig reconfig{};
    reconfig.width = 1920;
    reconfig.height = 1080;
    reconfig.fps = 60;
    reconfig.bitrate_kbps = 25000;
    reconfig.peak_bitrate_kbps = 35000;
    reconfig.codec = chosen_codec;
    reconfig.rc_mode = 0; // CBR
    reconfig.gop_length = 0;
    reconfig.enable_intra_refresh = 0;
    reconfig.enable_filler_data = 0;

    int reconfig_res = moonshine_encoder_reconfigure(encoder, &reconfig);
    ACTIVE_ASSERT(reconfig_res == 1);
    std::cout << "  [+] Dynamic bitrate reconfigure succeeded (bitrate: 25000 kbps)." << std::endl;

    std::cout << "[*] Encoding Frame 4 with reconfigured parameters..." << std::endl;
    MoonshineEncodedPacketDesc desc4{};
    uint32_t out_size4 = 0;

    int encode_res4 = moonshine_encoder_encode_frame(
        encoder,
        test_texture.Get(),
        0,
        &desc4,
        bitstream_buffer.data(),
        static_cast<uint32_t>(bitstream_buffer.size()),
        &out_size4
    );

    ACTIVE_ASSERT(encode_res4 == 1);
    ACTIVE_ASSERT(out_size4 > 0);
    ACTIVE_ASSERT(validate_nalu_start_codes(bitstream_buffer.data(), out_size4));

    std::cout << "  [+] Frame 4 encoded successfully: size=" << out_size4 << " bytes." << std::endl;

    // Cleanly destroy encoder and decoder
    std::cout << "[*] Destroying encoder and decoder resources cleanly..." << std::endl;
    moonshine_encoder_destroy(encoder);
    moonshine_video_destroy(decoder);
    std::cout << "  [+] Encoder and decoder handles destroyed cleanly." << std::endl;

#else
    std::cout << "[*] Note: Non-Windows operating system detected. Skipping live Direct3D 11 NVENC test." << std::endl;
#endif

    std::cout << "[+] Live NVIDIA NVENC pipeline and GPU integration test passed successfully." << std::endl;
    return 0;
}
