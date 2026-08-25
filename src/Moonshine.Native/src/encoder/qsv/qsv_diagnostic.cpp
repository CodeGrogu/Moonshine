#include "encoder/qsv/qsv_diagnostic.hpp"
#include "encoder/qsv/qsv_types.hpp"
#include <cstring>
#include <cstdio>
#include <vector>
#include <memory>
#include <algorithm>

#if defined(_WIN32)
#include <d3d11.h>
#include <d3d11_4.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#endif

namespace moonshine::encoder::qsv {

int QsvDiagnostic::run(MoonshineQsvDiagnosticReport* out_report) {
    if (!out_report) return -1;
    std::memset(out_report, 0, sizeof(MoonshineQsvDiagnosticReport));

#if defined(_WIN32)
    // 1. Discover Intel physical/integrated adapter
    Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
    if (!find_intel_adapter(*out_report, adapter)) {
        return 0;
    }

    // 2. Create Direct3D 11 hardware device on Intel adapter
    Microsoft::WRL::ComPtr<ID3D11Device> d3d_device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    if (!create_intel_device(adapter, *out_report, d3d_device, context)) {
        return 0;
    }

    // 3. Verify created device adapter vendor matches Intel (0x8086)
    if (!verify_device_adapter(d3d_device, *out_report)) {
        return 0;
    }

    // 4. Load modern oneVPL dispatcher runtime library
    QsvApi api;
    if (!load_one_vpl(api, *out_report)) {
        return 0;
    }

    // 5. Initialize oneVPL loader and configure hardware/D3D11 filter properties
    mfxLoader loader = api.MFXLoad();
    if (!loader) {
        std::snprintf(out_report->first_failed_stage, sizeof(out_report->first_failed_stage), "MFXLoad");
        api.unload();
        return 0;
    }

    if (!configure_filters(api, loader, *out_report)) {
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 6. Create modern oneVPL hardware session
    mfxSession session = nullptr;
    if (!create_session(api, loader, *out_report, session)) {
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 7. Bind Direct3D 11 device handle to session with multithread protection
    if (!bind_d3d11_handle(api, session, d3d_device.Get(), *out_report)) {
        api.MFXClose(session);
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 8. Query codec capabilities specifically against active Intel session
    query_adapter_codecs(api, session, *out_report);

    // 9. Configure encoder parameters
    EncoderConfig cfg{};
    cfg.width = 1920;
    cfg.height = 1080;
    cfg.fps = 60;
    cfg.bitrate_kbps = 5000;
    cfg.peak_bitrate_kbps = 5000;
    cfg.codec = static_cast<uint32_t>(out_report->hevc_supported ? VideoCodec::Hevc : VideoCodec::H264);
    cfg.rc_mode = 0;
    cfg.gop_length = 0;

    mfxVideoParam params{};
    if (!configure_encoder(api, session, cfg, *out_report, params)) {
        api.MFXClose(session);
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 10. Create Direct3D 11 texture and render procedural SMPTE colour bars
    D3D11_TEXTURE2D_DESC tex_desc{};
    tex_desc.Width = 1920;
    tex_desc.Height = 1080;
    tex_desc.MipLevels = 1;
    tex_desc.ArraySize = 1;
    tex_desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    tex_desc.SampleDesc.Count = 1;
    tex_desc.Usage = D3D11_USAGE_DEFAULT;
    tex_desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

    Microsoft::WRL::ComPtr<ID3D11Texture2D> test_texture;
    HRESULT hr = d3d_device->CreateTexture2D(&tex_desc, nullptr, &test_texture);
    if (FAILED(hr) || !test_texture) {
        out_report->last_hresult = static_cast<int32_t>(hr);
        std::snprintf(out_report->first_failed_stage, sizeof(out_report->first_failed_stage), "CreateTexture2D");
        api.MFXVideoENCODE_Close(session);
        api.MFXClose(session);
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    render_smpte_colour_bars(d3d_device.Get(), context.Get(), test_texture.Get(), 1920, 1080);

    // 11. Encode known visual frame
    std::vector<uint8_t> bitstream;
    bool is_keyframe = false;
    if (!encode_known_frame(api, session, test_texture.Get(), context.Get(), params, *out_report, bitstream, is_keyframe)) {
        api.MFXVideoENCODE_Close(session);
        api.MFXClose(session);
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 12. Validate bitstream headers and NALU start codes
    if (!validate_bitstream(bitstream.data(), static_cast<uint32_t>(bitstream.size()), *out_report)) {
        api.MFXVideoENCODE_Close(session);
        api.MFXClose(session);
        api.MFXUnload(loader);
        api.unload();
        return 0;
    }

    // 13. Direct3D 11 Decoder Loopback & Decoded Texture Extraction
    decode_and_validate(
        bitstream.data(),
        static_cast<uint32_t>(bitstream.size()),
        is_keyframe,
        1920,
        1080,
        static_cast<video::VideoCodec>(cfg.codec),
        *out_report
    );

    api.MFXVideoENCODE_Close(session);
    api.MFXClose(session);
    api.MFXUnload(loader);
    api.unload();
    return 0;
#else
    (void)out_report;
    return -1;
#endif
}

#if defined(_WIN32)

bool QsvDiagnostic::find_intel_adapter(
    MoonshineQsvDiagnosticReport& report,
    Microsoft::WRL::ComPtr<IDXGIAdapter1>& out_adapter
) {
    Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
        report.last_hresult = -1;
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "CreateDXGIFactory1");
        return false;
    }

    Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
    for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
        DXGI_ADAPTER_DESC1 desc{};
        if (SUCCEEDED(adapter->GetDesc1(&desc))) {
            if (desc.VendorId == 0x8086) {
                out_adapter = adapter;
                report.adapter_found = 1;
                report.adapter_device_id = desc.DeviceId;
                WideCharToMultiByte(
                    CP_UTF8, 0, desc.Description, -1,
                    report.adapter_description, sizeof(report.adapter_description) - 1,
                    nullptr, nullptr
                );
                return true;
            }
        }
    }

    std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "FindIntelAdapter");
    return false;
}

bool QsvDiagnostic::create_intel_device(
    const Microsoft::WRL::ComPtr<IDXGIAdapter1>& adapter,
    MoonshineQsvDiagnosticReport& report,
    Microsoft::WRL::ComPtr<ID3D11Device>& out_device,
    Microsoft::WRL::ComPtr<ID3D11DeviceContext>& out_context
) {
    const UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
    const D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    D3D_FEATURE_LEVEL fl{};

    HRESULT hr = D3D11CreateDevice(
        adapter.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        create_flags,
        feature_levels,
        2,
        D3D11_SDK_VERSION,
        &out_device,
        &fl,
        &out_context
    );

    report.last_hresult = static_cast<int32_t>(hr);
    if (FAILED(hr) || !out_device) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "D3D11CreateDevice");
        return false;
    }

    report.d3d11_device_created = 1;
    return true;
}

bool QsvDiagnostic::verify_device_adapter(
    const Microsoft::WRL::ComPtr<ID3D11Device>& device,
    MoonshineQsvDiagnosticReport& report
) {
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgi_dev)))) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "QueryIDXGIDevice");
        return false;
    }

    Microsoft::WRL::ComPtr<IDXGIAdapter> dev_adapter;
    if (FAILED(dxgi_dev->GetAdapter(&dev_adapter))) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "GetAdapter");
        return false;
    }

    DXGI_ADAPTER_DESC dev_desc{};
    if (FAILED(dev_adapter->GetDesc(&dev_desc)) || dev_desc.VendorId != 0x8086) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "VerifyIntelVendorId");
        return false;
    }

    report.d3d11_vendor_verified = 1;
    return true;
}

bool QsvDiagnostic::load_one_vpl(
    QsvApi& api,
    MoonshineQsvDiagnosticReport& report
) {
    if (!api.load()) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "LoadOneVplDll");
        return false;
    }

    report.vpl_dll_loaded = 1;
    std::snprintf(report.vpl_dll_name, sizeof(report.vpl_dll_name), "%s", api.resolved_dll_name().c_str());
    return true;
}

bool QsvDiagnostic::configure_filters(
    QsvApi& api,
    mfxLoader loader,
    MoonshineQsvDiagnosticReport& report
) {
    // Filter 1: Hardware Implementation
    mfxConfig cfg1 = api.MFXCreateConfig(loader);
    if (cfg1) {
        report.vpl_config_created = 1;
        mfxVariant impl_var{};
        impl_var.Version.Major = 1;
        impl_var.Version.Minor = 0;
        impl_var.Type = MFX_VARIANT_TYPE_U32;
        impl_var.Data.U32 = MFX_IMPL_TYPE_HARDWARE;
        mfxStatus sts = api.MFXSetConfigFilterProperty(
            cfg1,
            reinterpret_cast<const uint8_t*>("mfxImplDescription.Impl"),
            impl_var
        );
        report.impl_filter_status = static_cast<int32_t>(sts);
        if (sts == MFX_ERR_NONE) {
            report.vpl_impl_filter_applied = 1;
        }
    }

    // Filter 2: Direct3D 11 Acceleration Mode
    mfxConfig cfg2 = api.MFXCreateConfig(loader);
    if (cfg2) {
        mfxVariant accel_var{};
        accel_var.Version.Major = 1;
        accel_var.Version.Minor = 0;
        accel_var.Type = MFX_VARIANT_TYPE_U32;
        accel_var.Data.U32 = MFX_ACCEL_MODE_VIA_D3D11;
        mfxStatus sts = api.MFXSetConfigFilterProperty(
            cfg2,
            reinterpret_cast<const uint8_t*>("mfxImplDescription.AccelerationMode"),
            accel_var
        );
        report.accel_filter_status = static_cast<int32_t>(sts);
        if (sts == MFX_ERR_NONE) {
            report.vpl_accel_filter_applied = 1;
        }
    }

    return true;
}

bool QsvDiagnostic::create_session(
    QsvApi& api,
    mfxLoader loader,
    MoonshineQsvDiagnosticReport& report,
    mfxSession& out_session
) {
    if (!api.MFXCreateSession || !loader) return false;
    mfxSession session = nullptr;
    mfxStatus sts = MFX_ERR_NOT_FOUND;

    for (uint32_t impl_idx = 0; impl_idx < 8; ++impl_idx) {
        session = nullptr;
        sts = api.MFXCreateSession(loader, impl_idx, &session);
        report.last_mfx_status = sts;
        if (sts == MFX_ERR_NONE && session != nullptr) {
            break;
        }
        if (sts == MFX_ERR_NOT_FOUND) {
            break;
        }
    }

    if (sts != MFX_ERR_NONE || !session) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "MFXCreateSession");
        return false;
    }

    out_session = session;
    report.vpl_session_created = 1;
    report.legacy_mfx_fallback_used = 0;
    return true;
}

bool QsvDiagnostic::bind_d3d11_handle(
    QsvApi& api,
    mfxSession session,
    ID3D11Device* device,
    MoonshineQsvDiagnosticReport& report
) {
    Microsoft::WRL::ComPtr<ID3D11Multithread> multithread;
    if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&multithread)))) {
        multithread->SetMultithreadProtected(TRUE);
    }

    mfxStatus sts = api.MFXVideoCORE_SetHandle(session, MFX_HANDLE_D3D11_DEVICE, device);
    report.last_mfx_status = sts;
    if (sts != MFX_ERR_NONE) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "MFXVideoCORE_SetHandle");
        return false;
    }

    report.d3d11_handle_bound = 1;
    return true;
}

void QsvDiagnostic::query_adapter_codecs(
    QsvApi& api,
    mfxSession session,
    MoonshineQsvDiagnosticReport& report
) {
    auto query_codec = [&](uint32_t codec_fourcc, uint32_t& out_queried, uint32_t& out_supported) {
        out_queried = 1;
        out_supported = 0;

        if (!api.MFXVideoENCODE_Query) return;

        mfxVideoParam in_param{};
        in_param.mfx.CodecId = codec_fourcc;
        in_param.mfx.TargetUsage = MFX_TARGETUSAGE_BALANCED;
        in_param.mfx.TargetKbps = 5000;
        in_param.mfx.RateControlMethod = MFX_RATECONTROL_CBR;
        in_param.mfx.FrameInfo.FourCC = MFX_FOURCC_NV12;
        in_param.mfx.FrameInfo.Width = 1920;
        in_param.mfx.FrameInfo.Height = 1080;
        in_param.mfx.FrameInfo.CropW = 1920;
        in_param.mfx.FrameInfo.CropH = 1080;
        in_param.mfx.FrameInfo.FrameRateExtN = 60;
        in_param.mfx.FrameInfo.FrameRateExtD = 1;
        in_param.IOPattern = MFX_IOPATTERN_IN_VIDEO_MEMORY;

        mfxVideoParam out_param{};
        mfxStatus sts = api.MFXVideoENCODE_Query(session, &in_param, &out_param);
        if (sts == MFX_ERR_NONE || sts == MFX_WRN_INCOMPATIBLE_VIDEO_PARAM) {
            out_supported = 1;
        }
    };

    query_codec(MFX_CODEC_AVC, report.h264_queried, report.h264_supported);
    query_codec(MFX_CODEC_HEVC, report.hevc_queried, report.hevc_supported);
    query_codec(MFX_CODEC_AV1, report.av1_queried, report.av1_supported);
}

bool QsvDiagnostic::configure_encoder(
    QsvApi& api,
    mfxSession session,
    const EncoderConfig& config,
    MoonshineQsvDiagnosticReport& report,
    mfxVideoParam& out_params
) {
    if (!api.MFXVideoENCODE_Init) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "MFXVideoENCODE_Init_Missing");
        return false;
    }

    std::memset(&out_params, 0, sizeof(mfxVideoParam));
    uint32_t codec_fourcc = MFX_CODEC_AVC;
    if (config.codec == static_cast<uint32_t>(VideoCodec::Hevc)) {
        codec_fourcc = MFX_CODEC_HEVC;
    } else if (config.codec == static_cast<uint32_t>(VideoCodec::Av1)) {
        codec_fourcc = MFX_CODEC_AV1;
    }

    out_params.mfx.CodecId = codec_fourcc;
    out_params.mfx.TargetUsage = MFX_TARGETUSAGE_BALANCED;
    out_params.mfx.TargetKbps = static_cast<uint16_t>(config.bitrate_kbps > 65535 ? 65535 : config.bitrate_kbps);
    out_params.mfx.MaxKbps = out_params.mfx.TargetKbps;
    out_params.mfx.BufferSizeInKB = static_cast<uint16_t>(out_params.mfx.TargetKbps / 8);
    out_params.mfx.InitialDelayInKB = static_cast<uint16_t>(out_params.mfx.BufferSizeInKB / 2);
    out_params.mfx.RateControlMethod = MFX_RATECONTROL_CBR;
    out_params.mfx.GopRefDist = 1;
    out_params.mfx.GopPicSize = 60;
    out_params.mfx.IdrInterval = 1;

    out_params.mfx.FrameInfo.FourCC = MFX_FOURCC_RGB4;
    out_params.mfx.FrameInfo.Width = static_cast<uint16_t>((config.width + 15) & ~15);
    out_params.mfx.FrameInfo.Height = static_cast<uint16_t>((config.height + 15) & ~15);
    out_params.mfx.FrameInfo.CropX = 0;
    out_params.mfx.FrameInfo.CropY = 0;
    out_params.mfx.FrameInfo.CropW = static_cast<uint16_t>(config.width);
    out_params.mfx.FrameInfo.CropH = static_cast<uint16_t>(config.height);
    out_params.mfx.FrameInfo.FrameRateExtN = static_cast<uint32_t>(config.fps);
    out_params.mfx.FrameInfo.FrameRateExtD = 1;
    out_params.mfx.FrameInfo.PicStruct = 1; // Progressive
    out_params.IOPattern = MFX_IOPATTERN_IN_VIDEO_MEMORY;

    mfxStatus sts = api.MFXVideoENCODE_Init(session, &out_params);
    report.last_mfx_status = sts;
    if (sts < MFX_ERR_NONE) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "MFXVideoENCODE_Init");
        return false;
    }

    report.encoder_configured = 1;
    return true;
}

bool QsvDiagnostic::render_smpte_colour_bars(
    ID3D11Device* device,
    ID3D11DeviceContext* context,
    ID3D11Texture2D* texture,
    uint32_t width,
    uint32_t height
) {
    (void)device;
    if (!context || !texture || width == 0 || height == 0) return false;

    // Standard SMPTE 75% colour bar pixel values in BGRA8 format (0xAARRGGBB order in memory)
    // 0: White (75%), 1: Yellow, 2: Cyan, 3: Green, 4: Magenta, 5: Red, 6: Blue
    const uint32_t bar_colours[7] = {
        0xFFBFBFBF, // 75% White (B:191, G:191, R:191)
        0xFF00BFBF, // 75% Yellow (B:0,   G:191, R:191)
        0xFFBFBF00, // 75% Cyan   (B:191, G:191, R:0)
        0xFF00BF00, // 75% Green  (B:0,   G:191, R:0)
        0xFFBF00BF, // 75% Magenta(B:191, G:0,   R:191)
        0xFF0000BF, // 75% Red    (B:0,   G:0,   R:191)
        0xFFBF0000  // 75% Blue   (B:191, G:0,   R:0)
    };

    std::vector<uint32_t> pixels(width * height);
    const uint32_t top_height = (height * 3) / 4;

    for (uint32_t y = 0; y < height; ++y) {
        for (uint32_t x = 0; x < width; ++x) {
            if (y < top_height) {
                uint32_t bar_index = (x * 7) / width;
                if (bar_index > 6) bar_index = 6;
                pixels[y * width + x] = bar_colours[bar_index];
            } else {
                // Lower quarter: alternating black and white test bars
                uint32_t bar_index = (x * 4) / width;
                pixels[y * width + x] = (bar_index % 2 == 0) ? 0xFF000000 : 0xFFFFFFFF;
            }
        }
    }

    context->UpdateSubresource(texture, 0, nullptr, pixels.data(), width * sizeof(uint32_t), 0);
    return true;
}

bool QsvDiagnostic::encode_known_frame(
    QsvApi& api,
    mfxSession session,
    ID3D11Texture2D* texture,
    ID3D11DeviceContext* context,
    const mfxVideoParam& params,
    MoonshineQsvDiagnosticReport& report,
    std::vector<uint8_t>& out_bitstream,
    bool& out_is_keyframe
) {
    (void)context;
    if (!api.MFXVideoENCODE_EncodeFrameAsync) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "MFXVideoENCODE_EncodeFrameAsync_Missing");
        return false;
    }

    // Allocate bitstream buffer
    std::vector<uint8_t> buffer(2 * 1024 * 1024);
    mfxBitstream bs{};
    bs.Data = buffer.data();
    bs.MaxLength = static_cast<uint32_t>(buffer.size());
    bs.DataOffset = 0;
    bs.DataLength = 0;

    mfxHDLPair hdl_pair{};
    hdl_pair.first = texture;
    hdl_pair.second = (mfxHDL)(uintptr_t)0;

    mfxFrameSurface1 surface{};
    surface.Info = params.mfx.FrameInfo;
    surface.Data.MemType = MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET | MFX_MEMTYPE_FROM_ENCODE;
    surface.Data.MemId = &hdl_pair;
    surface.Data.TimeStamp = 0;

    mfxSyncPoint sync_point = nullptr;
    mfxStatus sts = api.MFXVideoENCODE_EncodeFrameAsync(session, nullptr, &surface, &bs, &sync_point);
    report.last_mfx_status = sts;

    if (sts == MFX_ERR_NONE && sync_point && api.MFXVideoCORE_SyncOperation) {
        sts = api.MFXVideoCORE_SyncOperation(session, sync_point, 1000);
        report.last_mfx_status = sts;
    }

    if (sts == MFX_ERR_NONE && bs.DataLength > 0) {
        report.frame_encoded = 1;
        out_bitstream.assign(bs.Data + bs.DataOffset, bs.Data + bs.DataOffset + bs.DataLength);
        out_is_keyframe = (bs.FrameType & MFX_FRAMETYPE_IDR) || (bs.FrameType & MFX_FRAMETYPE_I);
        return true;
    }

    std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "EncodeFrameAsync");
    return false;
}

bool QsvDiagnostic::validate_bitstream(
    const uint8_t* bitstream,
    uint32_t size,
    MoonshineQsvDiagnosticReport& report
) {
    if (!bitstream || size < 4) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "BitstreamTooSmall");
        return false;
    }

    // Check for standard 3-byte (0x000001) or 4-byte (0x00000001) NALU start codes
    bool has_start_code = (bitstream[0] == 0 && bitstream[1] == 0 &&
        (bitstream[2] == 1 || (bitstream[2] == 0 && bitstream[3] == 1)));

    if (has_start_code) {
        report.bitstream_valid = 1;
        return true;
    }

    std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "InvalidBitstreamStartCode");
    return false;
}

bool QsvDiagnostic::decode_and_validate(
    const uint8_t* bitstream,
    uint32_t size,
    bool is_keyframe,
    uint32_t width,
    uint32_t height,
    video::VideoCodec codec,
    MoonshineQsvDiagnosticReport& report
) {
    auto decoder = std::make_unique<video::D3D11VideoDecoder>();
    if (decoder->Initialize(nullptr, width, height, codec) != 0) {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "DecoderInitialize");
        return false;
    }
    report.decoder_created = 1;

    MoonshineFrameDesc frame_desc{};
    frame_desc.frame_index = 0;
    frame_desc.total_bytes = size;
    frame_desc.packet_count = 1;
    frame_desc.is_keyframe = is_keyframe ? 1 : 0;
    frame_desc.frame_buffer = const_cast<uint8_t*>(bitstream);

    if (decoder->SubmitFrame(frame_desc) == 0) {
        report.decoder_accepted = 1;
    } else {
        std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "DecoderSubmitFrame");
        return false;
    }

    if (decoder->GetTextureHandle() != nullptr || decoder->GetDecodedFrames() > 0) {
        report.decoded_texture_available = 1;
        report.decoder_loopback_passed = 1;
        return true;
    }

    std::snprintf(report.first_failed_stage, sizeof(report.first_failed_stage), "DecodedTextureNotAvailable");
    return false;
}

#endif // _WIN32

} // namespace moonshine::encoder::qsv
