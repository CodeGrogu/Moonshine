#include "encoder/nvenc/nvenc_session.hpp"
#include "encoder/nvenc/nvenc_resource_guard.hpp"
#include <algorithm>
#include <chrono>
#include <cstring>

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder::nvenc {

NvencSession::NvencSession() = default;

NvencSession::~NvencSession() {
    close();
}

NvencSession::NvencSession(NvencSession&& other) noexcept
    : _api(other._api),
      _d3d_device(other._d3d_device),
      _session(other._session),
      _config(other._config),
      _preset(other._preset),
      _tuning(other._tuning),
      _intra_refresh_enabled(other._intra_refresh_enabled),
      _intra_refresh_period(other._intra_refresh_period),
      _intra_refresh_count(other._intra_refresh_count),
      _is_configured(other._is_configured),
      _bitstream_pool(std::move(other._bitstream_pool)) {
    std::lock_guard<std::mutex> lock(other._in_flight_mutex);
    _in_flight_frames = std::move(other._in_flight_frames);

    other._api = nullptr;
    other._d3d_device = nullptr;
    other._session = nullptr;
    other._is_configured = false;
}

NvencSession& NvencSession::operator=(NvencSession&& other) noexcept {
    if (this != &other) {
        close();
        _api = other._api;
        _d3d_device = other._d3d_device;
        _session = other._session;
        _config = other._config;
        _preset = other._preset;
        _tuning = other._tuning;
        _intra_refresh_enabled = other._intra_refresh_enabled;
        _intra_refresh_period = other._intra_refresh_period;
        _intra_refresh_count = other._intra_refresh_count;
        _is_configured = other._is_configured;
        _bitstream_pool = std::move(other._bitstream_pool);

        std::lock_guard<std::mutex> lock(other._in_flight_mutex);
        _in_flight_frames = std::move(other._in_flight_frames);

        other._api = nullptr;
        other._d3d_device = nullptr;
        other._session = nullptr;
        other._is_configured = false;
    }
    return *this;
}

bool NvencSession::open(NvencApi& api, void* d3d_device) {
    close();

    if (!d3d_device) {
        return false;
    }

    if (!api.is_loaded() && !api.load()) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) {
        return false;
    }

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) {
        return false;
    }

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) {
        return false;
    }

    const auto& fn = api.functions();
    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS session_params{};
    session_params.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    session_params.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
    session_params.device = d3d_device;
    session_params.apiVersion = NVENCAPI_VERSION;

    auto pfn_open_session_ex = reinterpret_cast<PNVENCOPENENCODESESSIONEX>(fn.nvEncOpenEncodeSessionEx);
    if (!pfn_open_session_ex || pfn_open_session_ex(&session_params, &_session) != NV_ENC_SUCCESS || !_session) {
        _session = nullptr;
        return false;
    }

    _api = &api;
    _d3d_device = d3d_device;
    return true;
#else
    (void)api;
    (void)d3d_device;
    return false;
#endif
}

bool NvencSession::configure(const EncoderConfig& config) {
    if (!_session || !_api) {
        return false;
    }

#if defined(_WIN32)
    const auto& fn = _api->functions();

    // Clear existing bitstream buffers if re-configuring
    _bitstream_pool.clear(_session, fn);

    auto selected_codec = static_cast<VideoCodec>(config.codec);
    GUID codec_guid = NV_ENC_CODEC_H264_GUID_LOCAL;
    if (selected_codec == VideoCodec::Hevc || selected_codec == VideoCodec::HevcMain10) {
        codec_guid = NV_ENC_CODEC_HEVC_GUID_LOCAL;
    } else if (selected_codec == VideoCodec::Av1) {
        codec_guid = NV_ENC_CODEC_AV1_GUID_LOCAL;
    }

    GUID preset_guid = NV_ENC_PRESET_P1_GUID_LOCAL;
    switch (_preset) {
        case NvencPreset::P1_UltraFast: preset_guid = NV_ENC_PRESET_P1_GUID_LOCAL; break;
        case NvencPreset::P2_Fast: preset_guid = NV_ENC_PRESET_P2_GUID_LOCAL; break;
        case NvencPreset::P3_Medium: preset_guid = NV_ENC_PRESET_P3_GUID_LOCAL; break;
        case NvencPreset::P4_Default: preset_guid = NV_ENC_PRESET_P4_GUID_LOCAL; break;
        case NvencPreset::P5_Slow: preset_guid = NV_ENC_PRESET_P5_GUID_LOCAL; break;
        case NvencPreset::P6_Slower: preset_guid = NV_ENC_PRESET_P6_GUID_LOCAL; break;
        case NvencPreset::P7_Slowest: preset_guid = NV_ENC_PRESET_P7_GUID_LOCAL; break;
    }

    uint32_t tuning_info = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
    switch (_tuning) {
        case NvencTuning::HighQuality: tuning_info = NV_ENC_TUNING_INFO_HIGH_QUALITY; break;
        case NvencTuning::LowLatency: tuning_info = NV_ENC_TUNING_INFO_LOW_LATENCY; break;
        case NvencTuning::UltraLowLatency: tuning_info = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY; break;
        case NvencTuning::Lossless: tuning_info = NV_ENC_TUNING_INFO_LOSSLESS; break;
    }

    NV_ENC_PRESET_CONFIG preset_config{};
    preset_config.version = NV_ENC_PRESET_CONFIG_VER;
    preset_config.presetCfg.version = NV_ENC_CONFIG_VER;

    auto pfn_get_preset_config_ex = reinterpret_cast<PNVENCGETENCODEPRESETCONFIGEX>(fn.nvEncGetEncodePresetConfigEx);
    NV_ENC_CONFIG enc_config{};
    if (pfn_get_preset_config_ex && pfn_get_preset_config_ex(_session, codec_guid, preset_guid, tuning_info, &preset_config) == NV_ENC_SUCCESS) {
        enc_config = preset_config.presetCfg;
    } else {
        enc_config.version = NV_ENC_CONFIG_VER;
        enc_config.profileGUID = NV_ENC_CODEC_PROFILE_AUTOSELECT_GUID_LOCAL;
    }

    if (selected_codec == VideoCodec::HevcMain10) {
        enc_config.profileGUID = NV_ENC_HEVC_PROFILE_MAIN10_GUID_LOCAL;
        enc_config.encodeCodecConfig.hevcConfig.pixelBitDepthMinus8 = 2;
    } else if (selected_codec == VideoCodec::Hevc) {
        enc_config.profileGUID = NV_ENC_HEVC_PROFILE_MAIN_GUID_LOCAL;
        enc_config.encodeCodecConfig.hevcConfig.pixelBitDepthMinus8 = 0;
    } else if (selected_codec == VideoCodec::H264) {
        enc_config.profileGUID = NV_ENC_H264_PROFILE_HIGH_GUID_LOCAL;
    } else if (selected_codec == VideoCodec::Av1) {
        enc_config.profileGUID = NV_ENC_AV1_PROFILE_MAIN_GUID_LOCAL;
    }

    enc_config.gopLength = (config.gop_length == 0) ? NVENC_INFINITE_GOPLENGTH : config.gop_length;
    enc_config.frameIntervalP = 1;
    enc_config.frameFieldMode = NV_ENC_PARAMS_FRAME_FIELD_MODE_FRAME;
    enc_config.mvPrecision = 0;

    // Rate control configuration (low-latency CBR default)
    enc_config.rcParams.rateControlMode = (config.rc_mode == 0) ? NV_ENC_PARAMS_RC_CBR : NV_ENC_PARAMS_RC_VBR;
    enc_config.rcParams.averageBitRate = config.bitrate_kbps * 1000;
    enc_config.rcParams.maxBitRate = (config.peak_bitrate_kbps > 0 ? config.peak_bitrate_kbps : config.bitrate_kbps * 3 / 2) * 1000;
    enc_config.rcParams.vbvBufferSize = (config.fps > 0) ? (enc_config.rcParams.averageBitRate / config.fps) : enc_config.rcParams.averageBitRate;
    enc_config.rcParams.vbvInitialDelay = enc_config.rcParams.vbvBufferSize;
    enc_config.rcParams.zeroReorderDelay = 1;

    // Intra-refresh configuration if requested
    if (_intra_refresh_enabled || config.enable_intra_refresh) {
        uint32_t period = _intra_refresh_period > 0 ? _intra_refresh_period : 60;
        uint32_t count = _intra_refresh_count > 0 ? _intra_refresh_count : 4;
        if (selected_codec == VideoCodec::H264) {
            enc_config.encodeCodecConfig.h264Config.enableIntraRefresh = 1;
            enc_config.encodeCodecConfig.h264Config.intraRefreshPeriod = period;
            enc_config.encodeCodecConfig.h264Config.intraRefreshCnt = count;
        } else if (selected_codec == VideoCodec::Hevc || selected_codec == VideoCodec::HevcMain10) {
            enc_config.encodeCodecConfig.hevcConfig.enableIntraRefresh = 1;
            enc_config.encodeCodecConfig.hevcConfig.intraRefreshPeriod = period;
            enc_config.encodeCodecConfig.hevcConfig.intraRefreshCnt = count;
        } else if (selected_codec == VideoCodec::Av1) {
            enc_config.encodeCodecConfig.av1Config.enableIntraRefresh = 1;
            enc_config.encodeCodecConfig.av1Config.intraRefreshPeriod = period;
            enc_config.encodeCodecConfig.av1Config.intraRefreshCnt = count;
        }
    }

    NV_ENC_INITIALIZE_PARAMS init_params{};
    init_params.version = NV_ENC_INITIALIZE_PARAMS_VER;
    init_params.encodeGUID = codec_guid;
    init_params.presetGUID = preset_guid;
    init_params.encodeWidth = config.width;
    init_params.encodeHeight = config.height;
    init_params.darWidth = config.width;
    init_params.darHeight = config.height;
    init_params.frameRateNum = config.fps;
    init_params.frameRateDen = 1;
    init_params.enablePTD = 1;
    init_params.enableEncodeAsync = 0;
    init_params.encodeConfig = &enc_config;
    init_params.maxEncodeWidth = config.width;
    init_params.maxEncodeHeight = config.height;
    init_params.tuningInfo = tuning_info;

    auto pfn_init_encoder = reinterpret_cast<PNVENCINITIALIZEENCODER>(fn.nvEncInitializeEncoder);
    if (!pfn_init_encoder || pfn_init_encoder(_session, &init_params) != NV_ENC_SUCCESS) {
        close();
        return false;
    }

    _config = config;
    _is_configured = true;
    return true;
#else
    (void)config;
    return false;
#endif
}

bool NvencSession::encode(
    void* registered_resource,
    bool force_idr,
    uint64_t frame_id,
    uint64_t timestamp_us,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
#if defined(_WIN32)
    if (!_session || !_api || !registered_resource || !out_bitstream || max_buffer_size == 0) {
        out_written_size = 0;
        return false;
    }

    const auto& fn = _api->functions();
    void* bitstream_buf = _bitstream_pool.acquire_buffer(_session, fn);
    if (!bitstream_buf) {
        out_written_size = 0;
        return false;
    }

    NvencMappedResourceGuard map_guard(_session, &fn, registered_resource);
    if (!map_guard.is_valid()) {
        _bitstream_pool.release_buffer(bitstream_buf);
        out_written_size = 0;
        return false;
    }

    NV_ENC_PIC_PARAMS pic_params{};
    pic_params.version = NV_ENC_PIC_PARAMS_VER;
    pic_params.inputWidth = _config.width;
    pic_params.inputHeight = _config.height;
    pic_params.inputPitch = _config.width;
    pic_params.inputBuffer = map_guard.mapped_resource();
    pic_params.outputBitstream = bitstream_buf;
    pic_params.bufferFmt = map_guard.mapped_buffer_format();
    pic_params.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
    pic_params.frameIdx = static_cast<uint32_t>(frame_id);
    pic_params.inputTimeStamp = timestamp_us;
    pic_params.encodePicFlags = 0;
    if (force_idr) {
        pic_params.encodePicFlags |= NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS;
    }

    auto pfn_encode = reinterpret_cast<PNVENCENCODEPICTURE>(fn.nvEncEncodePicture);
    if (!pfn_encode || pfn_encode(_session, &pic_params) != NV_ENC_SUCCESS) {
        _bitstream_pool.release_buffer(bitstream_buf);
        out_written_size = 0;
        return false;
    }

    NvencLockedBitstreamGuard lock_guard(_session, &fn, bitstream_buf);
    if (!lock_guard.is_valid()) {
        _bitstream_pool.release_buffer(bitstream_buf);
        out_written_size = 0;
        return false;
    }

    uint32_t copy_size = (std::min)(lock_guard.bitstream_size(), max_buffer_size);
    std::memcpy(out_bitstream, lock_guard.bitstream_ptr(), copy_size);

    out_written_size = copy_size;
    out_desc.frame_index = frame_id;
    if (timestamp_us > 0) {
        out_desc.timestamp_qpc = static_cast<int64_t>(timestamp_us);
    } else {
        auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
        out_desc.timestamp_qpc = std::chrono::duration_cast<std::chrono::microseconds>(now).count();
    }
    out_desc.payload_size = copy_size;
    out_desc.is_keyframe = (lock_guard.is_keyframe() || force_idr) ? 1 : 0;
    out_desc.is_header_packet = out_desc.is_keyframe;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    _bitstream_pool.release_buffer(bitstream_buf);
    return true;
#else
    (void)registered_resource;
    (void)force_idr;
    (void)frame_id;
    (void)timestamp_us;
    (void)out_desc;
    (void)out_bitstream;
    (void)max_buffer_size;
    out_written_size = 0;
    return false;
#endif
}

bool NvencSession::encode(
    void* registered_resource,
    bool force_idr,
    uint32_t frame_idx,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
    uint64_t timestamp_us = static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(now).count()
    );
    return encode(
        registered_resource,
        force_idr,
        static_cast<uint64_t>(frame_idx),
        timestamp_us,
        out_desc,
        out_bitstream,
        max_buffer_size,
        out_written_size
    );
}

bool NvencSession::submit_frame(
    void* registered_resource,
    bool force_idr,
    uint64_t frame_id,
    uint64_t timestamp_us,
    void* surface
) {
#if defined(_WIN32)
    if (!_session || !_api || !registered_resource) {
        return false;
    }

    const auto& fn = _api->functions();
    void* bitstream_buf = _bitstream_pool.acquire_buffer(_session, fn);
    if (!bitstream_buf) {
        return false;
    }

    NvencMappedResourceGuard map_guard(_session, &fn, registered_resource);
    if (!map_guard.is_valid()) {
        _bitstream_pool.release_buffer(bitstream_buf);
        return false;
    }

    NV_ENC_PIC_PARAMS pic_params{};
    pic_params.version = NV_ENC_PIC_PARAMS_VER;
    pic_params.inputWidth = _config.width;
    pic_params.inputHeight = _config.height;
    pic_params.inputPitch = _config.width;
    pic_params.inputBuffer = map_guard.mapped_resource();
    pic_params.outputBitstream = bitstream_buf;
    pic_params.bufferFmt = map_guard.mapped_buffer_format();
    pic_params.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
    pic_params.frameIdx = static_cast<uint32_t>(frame_id);
    pic_params.inputTimeStamp = timestamp_us;
    pic_params.encodePicFlags = 0;
    if (force_idr) {
        pic_params.encodePicFlags |= NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS;
    }

    auto pfn_encode = reinterpret_cast<PNVENCENCODEPICTURE>(fn.nvEncEncodePicture);
    if (!pfn_encode || pfn_encode(_session, &pic_params) != NV_ENC_SUCCESS) {
        _bitstream_pool.release_buffer(bitstream_buf);
        return false;
    }

    {
        std::lock_guard<std::mutex> lock(_in_flight_mutex);
        _in_flight_frames.push_back(NvencInFlightFrame{
            .frame_id = frame_id,
            .timestamp_us = timestamp_us,
            .surface = surface,
            .registered_resource = registered_resource,
            .bitstream_buffer = bitstream_buf,
            .submitted = true,
            .completed = false,
            .keyframe = force_idr
        });
    }

    return true;
#else
    (void)registered_resource;
    (void)force_idr;
    (void)frame_id;
    (void)timestamp_us;
    (void)surface;
    return false;
#endif
}

bool NvencSession::poll_packet(
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    EncodedPacketDesc& out_desc,
    uint32_t& out_written_size
) {
#if defined(_WIN32)
    out_written_size = 0;
    if (!_session || !_api || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

    NvencInFlightFrame in_flight{};
    {
        std::lock_guard<std::mutex> lock(_in_flight_mutex);
        if (_in_flight_frames.empty()) {
            return false;
        }
        in_flight = _in_flight_frames.front();
        _in_flight_frames.pop_front();
    }

    const auto& fn = _api->functions();
    NvencLockedBitstreamGuard lock_guard(_session, &fn, in_flight.bitstream_buffer);
    if (!lock_guard.is_valid()) {
        _bitstream_pool.release_buffer(in_flight.bitstream_buffer);
        return false;
    }

    uint32_t copy_size = (std::min)(lock_guard.bitstream_size(), max_buffer_size);
    std::memcpy(out_bitstream, lock_guard.bitstream_ptr(), copy_size);
    out_written_size = copy_size;

    out_desc.frame_index = in_flight.frame_id;
    if (in_flight.timestamp_us > 0) {
        out_desc.timestamp_qpc = static_cast<int64_t>(in_flight.timestamp_us);
    } else {
        auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
        out_desc.timestamp_qpc = std::chrono::duration_cast<std::chrono::microseconds>(now).count();
    }
    out_desc.payload_size = copy_size;
    out_desc.is_keyframe = (lock_guard.is_keyframe() || in_flight.keyframe) ? 1 : 0;
    out_desc.is_header_packet = out_desc.is_keyframe;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    _bitstream_pool.release_buffer(in_flight.bitstream_buffer);
    return true;
#else
    (void)out_bitstream;
    (void)max_buffer_size;
    (void)out_desc;
    out_written_size = 0;
    return false;
#endif
}

bool NvencSession::reconfigure(const EncoderConfig& new_config) {
    if (!_session || !_is_configured) {
        return false;
    }

    _config = new_config;

#if defined(_WIN32)
    if (_api && _api->functions().nvEncReconfigureEncoder) {
        NV_ENC_RECONFIGURE_PARAMS reconfig_params{};
        reconfig_params.version = NV_ENC_RECONFIGURE_PARAMS_VER;
        reconfig_params.resetEncoder = 0;
        reconfig_params.forceIDR = 1;

        reconfig_params.reInitEncodeParams.version = NV_ENC_INITIALIZE_PARAMS_VER;
        reconfig_params.reInitEncodeParams.encodeWidth = new_config.width;
        reconfig_params.reInitEncodeParams.encodeHeight = new_config.height;
        reconfig_params.reInitEncodeParams.frameRateNum = new_config.fps;
        reconfig_params.reInitEncodeParams.frameRateDen = 1;

        NV_ENC_CONFIG enc_config{};
        enc_config.version = NV_ENC_CONFIG_VER;
        enc_config.rcParams.rateControlMode = (new_config.rc_mode == 0) ? NV_ENC_PARAMS_RC_CBR : NV_ENC_PARAMS_RC_VBR;
        enc_config.rcParams.averageBitRate = new_config.bitrate_kbps * 1000;
        enc_config.rcParams.maxBitRate = (new_config.peak_bitrate_kbps > 0 ? new_config.peak_bitrate_kbps : new_config.bitrate_kbps * 3 / 2) * 1000;
        enc_config.rcParams.vbvBufferSize = (new_config.fps > 0) ? (enc_config.rcParams.averageBitRate / new_config.fps) : enc_config.rcParams.averageBitRate;
        enc_config.rcParams.vbvInitialDelay = enc_config.rcParams.vbvBufferSize;
        reconfig_params.reInitEncodeParams.encodeConfig = &enc_config;

        auto pfn_reconfig = reinterpret_cast<PNVENCRECONFIGUREENCODER>(_api->functions().nvEncReconfigureEncoder);
        if (pfn_reconfig) {
            pfn_reconfig(_session, &reconfig_params);
        }
    }
#endif
    return true;
}

void NvencSession::close() {
#if defined(_WIN32)
    if (_session && _api) {
        const auto& fn = _api->functions();
        {
            std::lock_guard<std::mutex> lock(_in_flight_mutex);
            _in_flight_frames.clear();
        }
        _bitstream_pool.clear(_session, fn);

        if (fn.nvEncDestroyEncoder) {
            auto pfn_destroy_encoder = reinterpret_cast<PNVENCDESTROYENCODER>(fn.nvEncDestroyEncoder);
            if (pfn_destroy_encoder) {
                pfn_destroy_encoder(_session);
            }
        }
        _session = nullptr;
    }
#endif
    {
        std::lock_guard<std::mutex> lock(_in_flight_mutex);
        _in_flight_frames.clear();
    }
    _session = nullptr;
    _api = nullptr;
    _d3d_device = nullptr;
    _is_configured = false;
}

bool NvencSession::is_open() const noexcept {
    return _session != nullptr;
}

bool NvencSession::is_configured() const noexcept {
    return _is_configured;
}

void* NvencSession::session_handle() const noexcept {
    return _session;
}

void* NvencSession::bitstream_buffer() const noexcept {
    return nullptr;
}

const EncoderConfig& NvencSession::config() const noexcept {
    return _config;
}

NvencBitstreamPool& NvencSession::bitstream_pool() noexcept {
    return _bitstream_pool;
}

void NvencSession::set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) noexcept {
    _preset = preset;
    _tuning = tuning;
}

void NvencSession::set_intra_refresh(bool enabled, uint32_t period, uint32_t count) noexcept {
    _intra_refresh_enabled = enabled;
    _intra_refresh_period = period;
    _intra_refresh_count = count;
}

} // namespace moonshine::encoder::nvenc
