#include "encoder/amf/amf_session.hpp"
#include <algorithm>
#include <chrono>
#include <cstring>

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder::amf {

AmfSession::AmfSession() = default;

AmfSession::~AmfSession() {
    close();
}

AmfSession::AmfSession(AmfSession&& other) noexcept
    : _api(other._api),
      _d3d_device(other._d3d_device),
      _context(other._context),
      _encoder(other._encoder),
      _config(other._config),
      _preset(other._preset),
      _usage(other._usage),
      _intra_refresh_enabled(other._intra_refresh_enabled),
      _intra_refresh_num_mbs_per_slot(other._intra_refresh_num_mbs_per_slot),
      _is_configured(other._is_configured) {
    other._api = nullptr;
    other._d3d_device = nullptr;
    other._context = nullptr;
    other._encoder = nullptr;
    other._is_configured = false;
}

AmfSession& AmfSession::operator=(AmfSession&& other) noexcept {
    if (this != &other) {
        close();
        _api = other._api;
        _d3d_device = other._d3d_device;
        _context = other._context;
        _encoder = other._encoder;
        _config = other._config;
        _preset = other._preset;
        _usage = other._usage;
        _intra_refresh_enabled = other._intra_refresh_enabled;
        _intra_refresh_num_mbs_per_slot = other._intra_refresh_num_mbs_per_slot;
        _is_configured = other._is_configured;

        other._api = nullptr;
        other._d3d_device = nullptr;
        other._context = nullptr;
        other._encoder = nullptr;
        other._is_configured = false;
    }
    return *this;
}

bool AmfSession::open(AmfApi& api, void* d3d_device) {
    close();

    if (!api.is_loaded() || !api.factory() || !d3d_device) {
        return false;
    }

    _api = &api;
    _d3d_device = d3d_device;

    AMFContext* pContext = nullptr;
    if (_api->factory()->CreateContext(&pContext) != AMF_OK || !pContext) {
        return false;
    }

    if (pContext->InitDX11(d3d_device) != AMF_OK) {
        pContext->Release();
        return false;
    }

    _context = pContext;
    return true;
}

bool AmfSession::configure(const EncoderConfig& config) {
    if (!_context || !_api || !_api->factory()) {
        return false;
    }

    if (_encoder) {
        _encoder->Drain();
        _encoder->Terminate();
        _encoder->Release();
        _encoder = nullptr;
    }

    const wchar_t* component_id = AMFVideoEncoder_HEVC;
    auto video_codec = static_cast<VideoCodec>(config.codec);
    if (video_codec == VideoCodec::H264) {
        component_id = AMFVideoEncoderVCE_AVC;
    } else if (video_codec == VideoCodec::Av1) {
        component_id = AMFVideoEncoder_AV1;
    }

    AMFComponent* pEncoder = nullptr;
    if (_api->factory()->CreateComponent(_context, component_id, &pEncoder) != AMF_OK || !pEncoder) {
        return false;
    }

    // Configure properties for ultra-low-latency real-time streaming
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_USAGE, make_int64_variant(static_cast<int64_t>(_usage)));
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_QUALITY_PRESET, make_int64_variant(static_cast<int64_t>(_preset)));
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD, make_int64_variant(config.rc_mode == 0 ? 0 : 1)); // 0: CBR, 1: VBR
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, make_int64_variant(static_cast<int64_t>(config.bitrate_kbps) * 1000));
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_PEAK_BITRATE, make_int64_variant(static_cast<int64_t>(config.peak_bitrate_kbps) * 1000));
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_B_PIC_PATTERN, make_int64_variant(0)); // Zero B-frames for game streaming
    pEncoder->SetProperty(AMF_VIDEO_ENCODER_FILLER_DATA_ENABLE, make_bool_variant(config.enable_filler_data != 0));

    if (_intra_refresh_enabled && _intra_refresh_num_mbs_per_slot > 0) {
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_INTRA_REFRESH_NUM_MBS_PER_SLOT, make_int64_variant(_intra_refresh_num_mbs_per_slot));
    }

    AMF_SURFACE_FORMAT surface_format = AMF_SURFACE_BGRA;
    if (video_codec == VideoCodec::HevcMain10) {
        surface_format = AMF_SURFACE_R10G10B10A2;
    }

    if (pEncoder->Init(surface_format, static_cast<amf_int32>(config.width), static_cast<amf_int32>(config.height)) != AMF_OK) {
        // Fallback to BGRA format if R10G10B10A2 init failed
        if (pEncoder->Init(AMF_SURFACE_BGRA, static_cast<amf_int32>(config.width), static_cast<amf_int32>(config.height)) != AMF_OK) {
            pEncoder->Release();
            return false;
        }
    }

    _encoder = pEncoder;
    _config = config;
    _is_configured = true;
    return true;
}

bool AmfSession::encode(
    void* d3d_texture,
    bool force_idr,
    uint64_t frame_id,
    uint64_t timestamp_us,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    out_written_size = 0;

    if (!_context || !_encoder || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

    std::lock_guard<std::mutex> lock(_mutex);

    AMFSurface* pSurface = nullptr;
    if (_context->CreateSurfaceFromDX11Native(d3d_texture, &pSurface, nullptr) != AMF_OK || !pSurface) {
        return false;
    }

    pSurface->SetPts(static_cast<amf_pts>(timestamp_us));

    if (force_idr) {
        _encoder->SetProperty(AMF_VIDEO_ENCODER_FORCE_PICTURE_TYPE, make_int64_variant(3)); // 3: IDR
    }

    AMF_RESULT res = _encoder->SubmitInput(pSurface);
    pSurface->Release();

    if (res != AMF_OK && res != AMF_INPUT_FULL && res != AMF_NEED_MORE_INPUT) {
        return false;
    }

    // Poll for encoded packet
    AMFData* pData = nullptr;
    AMF_RESULT query_res = _encoder->QueryOutput(&pData);

    if (query_res == AMF_OK && pData) {
        if (pData->GetDataType() == AMF_DATA_BUFFER) {
            auto* pBuffer = static_cast<AMFBuffer*>(pData);
            size_t buffer_size = pBuffer->GetSize();
            void* pNative = pBuffer->GetNative();

            if (buffer_size > max_buffer_size) {
                pData->Release();
                out_written_size = 0;
                return false;
            }

            if (pNative && buffer_size > 0) {
                std::memcpy(out_bitstream, pNative, buffer_size);
                out_written_size = static_cast<uint32_t>(buffer_size);

                out_desc.frame_index = frame_id;
                out_desc.timestamp_qpc = static_cast<int64_t>(timestamp_us);
                out_desc.payload_size = out_written_size;
                out_desc.is_keyframe = force_idr ? 1 : (frame_id == 0 ? 1 : 0);
                out_desc.is_header_packet = out_desc.is_keyframe;
                out_desc.temporal_id = 0;
                out_desc.reserved = 0;

                pData->Release();
                return true;
            }
        }
        pData->Release();
    }

    return false;
}

bool AmfSession::reconfigure(const EncoderConfig& new_config) {
    if (!_encoder) return false;

    std::lock_guard<std::mutex> lock(_mutex);

    _encoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, make_int64_variant(static_cast<int64_t>(new_config.bitrate_kbps) * 1000));
    _encoder->SetProperty(AMF_VIDEO_ENCODER_PEAK_BITRATE, make_int64_variant(static_cast<int64_t>(new_config.peak_bitrate_kbps) * 1000));

    if (new_config.width != _config.width || new_config.height != _config.height) {
        if (_encoder->ReInit(static_cast<amf_int32>(new_config.width), static_cast<amf_int32>(new_config.height)) != AMF_OK) {
            return false;
        }
    }

    _config = new_config;
    return true;
}

void AmfSession::close() {
    std::lock_guard<std::mutex> lock(_mutex);
    if (_encoder) {
        _encoder->Drain();
        _encoder->Terminate();
        _encoder->Release();
        _encoder = nullptr;
    }
    if (_context) {
        _context->Terminate();
        _context->Release();
        _context = nullptr;
    }
    _d3d_device = nullptr;
    _api = nullptr;
    _is_configured = false;
}

bool AmfSession::is_open() const noexcept {
    return _context != nullptr;
}

bool AmfSession::is_configured() const noexcept {
    return _is_configured && _encoder != nullptr;
}

const EncoderConfig& AmfSession::config() const noexcept {
    return _config;
}

void AmfSession::set_preset_and_usage(AmfQualityPreset preset, AmfUsage usage) noexcept {
    _preset = preset;
    _usage = usage;
}

void AmfSession::set_intra_refresh(bool enabled, uint32_t num_mbs_per_slot) noexcept {
    _intra_refresh_enabled = enabled;
    _intra_refresh_num_mbs_per_slot = num_mbs_per_slot;
}

} // namespace moonshine::encoder::amf
