#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include "encoder/qsv/qsv_api.hpp"
#include "encoder/qsv/qsv_session.hpp"
#include "moonshine/video/video_decoder_interface.hpp"

#if defined(_WIN32)
#include <d3d11.h>
#include <d3d11_4.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#endif

#include <vector>
#include <cstdint>

namespace moonshine::encoder::qsv {

/**
 * Modular Intel QuickSync / oneVPL diagnostic pipeline.
 * Decomposes hardware discovery, D3D11 creation, modern oneVPL loader,
 * session creation, codec capabilities, deterministic SMPTE colour bar encode,
 * bitstream validation, and decoder loopback verification into discrete steps.
 */
class QsvDiagnostic {
public:
    static int run(MoonshineQsvDiagnosticReport* out_report);

private:
#if defined(_WIN32)
    static bool find_intel_adapter(
        MoonshineQsvDiagnosticReport& report,
        Microsoft::WRL::ComPtr<IDXGIAdapter1>& out_adapter
    );

    static bool create_intel_device(
        const Microsoft::WRL::ComPtr<IDXGIAdapter1>& adapter,
        MoonshineQsvDiagnosticReport& report,
        Microsoft::WRL::ComPtr<ID3D11Device>& out_device,
        Microsoft::WRL::ComPtr<ID3D11DeviceContext>& out_context
    );

    static bool verify_device_adapter(
        const Microsoft::WRL::ComPtr<ID3D11Device>& device,
        MoonshineQsvDiagnosticReport& report
    );

    static bool load_one_vpl(
        QsvApi& api,
        MoonshineQsvDiagnosticReport& report
    );

    static bool configure_filters(
        QsvApi& api,
        mfxLoader loader,
        MoonshineQsvDiagnosticReport& report
    );

    static bool create_session(
        QsvApi& api,
        mfxLoader loader,
        MoonshineQsvDiagnosticReport& report,
        mfxSession& out_session
    );

    static bool bind_d3d11_handle(
        QsvApi& api,
        mfxSession session,
        ID3D11Device* device,
        MoonshineQsvDiagnosticReport& report
    );

    static void query_adapter_codecs(
        QsvApi& api,
        mfxSession session,
        MoonshineQsvDiagnosticReport& report
    );

    static bool configure_encoder(
        QsvApi& api,
        mfxSession session,
        const EncoderConfig& config,
        MoonshineQsvDiagnosticReport& report,
        mfxVideoParam& out_params
    );

    static bool render_smpte_colour_bars(
        ID3D11Device* device,
        ID3D11DeviceContext* context,
        ID3D11Texture2D* texture,
        uint32_t width,
        uint32_t height
    );

    static bool encode_known_frame(
        QsvApi& api,
        mfxSession session,
        ID3D11Texture2D* texture,
        ID3D11DeviceContext* context,
        const mfxVideoParam& params,
        MoonshineQsvDiagnosticReport& report,
        std::vector<uint8_t>& out_bitstream,
        bool& out_is_keyframe
    );

    static bool validate_bitstream(
        const uint8_t* bitstream,
        uint32_t size,
        MoonshineQsvDiagnosticReport& report
    );

    static bool decode_and_validate(
        const uint8_t* bitstream,
        uint32_t size,
        bool is_keyframe,
        uint32_t width,
        uint32_t height,
        video::VideoCodec codec,
        MoonshineQsvDiagnosticReport& report
    );
#endif
};

} // namespace moonshine::encoder::qsv
