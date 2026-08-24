#include "encoder/amf/amf_session.hpp"
#include <cstring>
#include <algorithm>
#include <thread>
#include <chrono>

#if defined(_WIN32)
#include <d3d11.h>
#endif

namespace moonshine::encoder::amf {

AmfSession::AmfSession() = default;

AmfSession::~AmfSession() {
    close();
}

AmfSession::AmfSession(AmfSession&& other) noexcept {
    std::lock_guard<std::mutex> lock(other._mutex);
    _api = other._api;
    _context = other._context;
    _encoder = other._encoder;
    _d3d_device = other._d3d_device;
    _config = other._config;
    _preset = other._preset;
    _usage = other._usage;
    _intra_refresh_enabled = other._intra_refresh_enabled;
    _intra_refresh_num_mbs_per_slot = other._intra_refresh_num_mbs_per_slot;
    _is_configured = other._is_configured;

    other._api = nullptr;
    other._context = nullptr;
    other._encoder = nullptr;
    other._d3d_device = nullptr;
    other._is_configured = false;
}

AmfSession& AmfSession::operator=(AmfSession&& other) noexcept {
    if (this != &other) {
        close();
        std::scoped_lock lock(_mutex, other._mutex);
        _api = other._api;
        _context = other._context;
        _encoder = other._encoder;
        _d3d_device = other._d3d_device;
        _config = other._config;
        _preset = other._preset;
        _usage = other._usage;
        _intra_refresh_enabled = other._intra_refresh_enabled;
        _intra_refresh_num_mbs_per_slot = other._intra_refresh_num_mbs_per_slot;
        _is_configured = other._is_configured;

        other._api = nullptr;
        other._context = nullptr;
        other._encoder = nullptr;
        other._d3d_device = nullptr;
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

    // Configure properties for ultra-low-latency real-time streaming with fail-closed checks
    if (pEncoder->SetProperty(AMF_VIDEO_ENCODER_USAGE, make_int64_variant(static_cast<int64_t>(_usage))) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_QUALITY_PRESET, make_int64_variant(static_cast<int64_t>(_preset))) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD, make_int64_variant(config.rc_mode == 0 ? 0 : 1)) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, make_int64_variant(static_cast<int64_t>(config.bitrate_kbps) * 1000)) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_PEAK_BITRATE, make_int64_variant(static_cast<int64_t>(config.peak_bitrate_kbps) * 1000)) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_B_PIC_PATTERN, make_int64_variant(0)) != AMF_OK ||
        pEncoder->SetProperty(AMF_VIDEO_ENCODER_FILLER_DATA_ENABLE, make_bool_variant(config.enable_filler_data != 0)) != AMF_OK) {
        pEncoder->Release();
        return false;
    }

    if (_intra_refresh_enabled && _intra_refresh_num_mbs_per_slot > 0) {
        if (pEncoder->SetProperty(AMF_VIDEO_ENCODER_INTRA_REFRESH_NUM_MBS_PER_SLOT, make_int64_variant(_intra_refresh_num_mbs_per_slot)) != AMF_OK) {
            pEncoder->Release();
            return false;
        }
    }

    AMF_SURFACE_FORMAT surface_format = AMF_SURFACE_BGRA;
    if (video_codec == VideoCodec::HevcMain10) {
        surface_format = AMF_SURFACE_R10G10B10A2;
        // Strictly require 10-bit initialization without silent downgrade
        if (pEncoder->Init(surface_format, static_cast<amf_int32>(config.width), static_cast<amf_int32>(config.height)) != AMF_OK) {
            pEncoder->Release();
            return false;
        }
    } else {
        if (pEncoder->Init(surface_format, static_cast<amf_int32>(config.width), static_cast<amf_int32>(config.height)) != AMF_OK) {
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

    // Bounded asynchronous polling for output packet
    AMFData* pData = nullptr;
    AMF_RESULT query_res = AMF_REPEAT;

    for (int poll_attempt = 0; poll_attempt < 20; ++poll_attempt) {
        query_res = _encoder->QueryOutput(&pData);
        if (query_res == AMF_OK && pData) {
            break;
        }
        if (query_res == AMF_EOF || query_res == AMF_INVALID_ARG) {
            return false;
        }
        if (query_res == AMF_REPEAT) {
            std::this_thread::yield();
        }
    }

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

                // Inspect bitstream NAL units to detect keyframe status authoritatively
                bool is_keyframe = false;
                const uint8_t* ptr = static_cast<const uint8_t*>(pNative);
                for (size_t i = 0; i + 4 < buffer_size && i < 128; ++i) {
                    if (ptr[i] == 0 && ptr[i+1] == 0 && ptr[i+2] == 1) {
                        if (_config.codec == static_cast<uint32_t>(VideoCodec::H264)) {
                            uint8_t nal_type = ptr[i+3] & 0x1F;
                            if (nal_type == 5 || nal_type == 7) is_keyframe = true;
                        } else {
                            uint8_t nal_type = (ptr[i+3] >> 1) & 0x3F;
                            if (nal_type == 19 || nal_type == 20 || nal_type == 32) is_keyframe = true;
                        }
                    } else if (ptr[i] == 0 && ptr[i+1] == 0 && ptr[i+2] == 0 && ptr[i+3] == 1) {
                        if (_config.codec == static_cast<uint32_t>(VideoCodec::H264)) {
                            uint8_t nal_type = ptr[i+4] & 0x1F;
                            if (nal_type == 5 || nal_type == 7) is_keyframe = true;
                        } else {
                            uint8_t nal_type = (ptr[i+4] >> 1) & 0x3F;
                            if (nal_type == 19 || nal_type == 20 || nal_type == 32) is_keyframe = true;
                        }
                    }
                }

                if (!is_keyframe && (force_idr || frame_id == 0)) {
                    is_keyframe = true;
                }

                out_desc.is_keyframe = is_keyframe ? 1 : 0;
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

    if (_encoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, make_int64_variant(static_cast<int64_t>(new_config.bitrate_kbps) * 1000)) != AMF_OK ||
        _encoder->SetProperty(AMF_VIDEO_ENCODER_PEAK_BITRATE, make_int64_variant(static_cast<int64_t>(new_config.peak_bitrate_kbps) * 1000)) != AMF_OK) {
        return false;
    }

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
#if defined(_WIN32)
        bool device_healthy = true;
        if (_d3d_device) {
            auto* dev = static_cast<ID3D11Device*>(_d3d_device);
            if (dev->GetDeviceRemovedReason() != S_OK) {
                device_healthy = false;
            }
        }
        if (device_healthy) {
            _encoder->Drain();
        }
#else
        _encoder->Drain();
#endif
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
    std::lock_guard<std::mutex> lock(_mutex);
    _preset = preset;
    _usage = usage;
    if (_encoder) {
        _encoder->SetProperty(AMF_VIDEO_ENCODER_QUALITY_PRESET, make_int64_variant(static_cast<int64_t>(_preset)));
        _encoder->SetProperty(AMF_VIDEO_ENCODER_USAGE, make_int64_variant(static_cast<int64_t>(_usage)));
    }
}

void AmfSession::set_intra_refresh(bool enable, uint32_t num_mbs_per_slot) noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    _intra_refresh_enabled = enable;
    _intra_refresh_num_mbs_per_slot = num_mbs_per_slot;
    if (_encoder && enable && num_mbs_per_slot > 0) {
        _encoder->SetProperty(AMF_VIDEO_ENCODER_INTRA_REFRESH_NUM_MBS_PER_SLOT, make_int64_variant(num_mbs_per_slot));
    }
}

} // namespace moonshine::encoder::amf
