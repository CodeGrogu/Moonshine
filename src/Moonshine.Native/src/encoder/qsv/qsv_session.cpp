#include "encoder/qsv/qsv_session.hpp"
#include <algorithm>
#include <chrono>
#include <cstring>

#if defined(_WIN32)
#include <d3d11.h>
#include <d3d11_4.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder::qsv {

QsvSession::QsvSession() = default;

QsvSession::~QsvSession() {
    close();
}

QsvSession::QsvSession(QsvSession&& other) noexcept
    : _api(other._api),
      _d3d_device(other._d3d_device),
      _loader(other._loader),
      _session(other._session),
      _params(other._params),
      _ext_opt(other._ext_opt),
      _ext_opt2(other._ext_opt2),
      _bitstream_buffer(std::move(other._bitstream_buffer)),
      _config(other._config),
      _usage(other._usage),
      _low_power_vdenc(other._low_power_vdenc),
      _intra_refresh_enabled(other._intra_refresh_enabled),
      _intra_refresh_cycle_size(other._intra_refresh_cycle_size),
      _intra_refresh_qp_delta(other._intra_refresh_qp_delta),
      _is_configured(other._is_configured) {
    other._api = nullptr;
    other._d3d_device = nullptr;
    other._loader = nullptr;
    other._session = nullptr;
    other._is_configured = false;
    _ext_buffers[0] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt);
    _ext_buffers[1] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt2);
    _params.ExtParam = _ext_buffers;
}

QsvSession& QsvSession::operator=(QsvSession&& other) noexcept {
    if (this != &other) {
        close();
        _api = other._api;
        _d3d_device = other._d3d_device;
        _loader = other._loader;
        _session = other._session;
        _params = other._params;
        _ext_opt = other._ext_opt;
        _ext_opt2 = other._ext_opt2;
        _bitstream_buffer = std::move(other._bitstream_buffer);
        _config = other._config;
        _usage = other._usage;
        _low_power_vdenc = other._low_power_vdenc;
        _intra_refresh_enabled = other._intra_refresh_enabled;
        _intra_refresh_cycle_size = other._intra_refresh_cycle_size;
        _intra_refresh_qp_delta = other._intra_refresh_qp_delta;
        _is_configured = other._is_configured;

        other._api = nullptr;
        other._d3d_device = nullptr;
        other._loader = nullptr;
        other._session = nullptr;
        other._is_configured = false;
        _ext_buffers[0] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt);
        _ext_buffers[1] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt2);
        _params.ExtParam = _ext_buffers;
    }
    return *this;
}

bool QsvSession::open(QsvApi& api, void* d3d_device) {
    close();

    if (!api.is_loaded() || !d3d_device) {
        return false;
    }

    _api = &api;
    _d3d_device = d3d_device;

    if (api.is_vpl() && api.MFXLoad && api.MFXCreateSession) {
        // Modern oneVPL 2.x session initialization architecture
        mfxLoader loader = api.MFXLoad();
        if (loader) {
            if (api.MFXCreateConfig && api.MFXSetConfigFilterProperty) {
                mfxConfig cfg = api.MFXCreateConfig(loader);
                if (cfg) {
                    mfxVariant var{};
                    var.Type = MFX_VARIANT_TYPE_U32;
                    var.Data.U32 = MFX_IMPL_TYPE_HARDWARE;
                    api.MFXSetConfigFilterProperty(cfg, reinterpret_cast<const uint8_t*>("mfxImplDescription.Impl"), var);

                    mfxVariant accelVar{};
                    accelVar.Type = MFX_VARIANT_TYPE_U32;
                    accelVar.Data.U32 = MFX_ACCEL_MODE_VIA_D3D11;
                    api.MFXSetConfigFilterProperty(cfg, reinterpret_cast<const uint8_t*>("mfxImplDescription.AccelerationMode"), accelVar);
                }
            }

            mfxStatus sts = MFX_ERR_NOT_FOUND;
            for (uint32_t implIdx = 0; implIdx < 8; ++implIdx) {
                sts = api.MFXCreateSession(loader, implIdx, &_session);
                _last_status = sts;
                if (sts == MFX_ERR_NONE && _session) {
                    break;
                }
            }

            if (sts != MFX_ERR_NONE || !_session) {
                // Try without config filter properties (enumerate all installed runtimes)
                api.MFXUnload(loader);
                _session = nullptr;
                loader = api.MFXLoad();
                if (loader) {
                    for (uint32_t implIdx = 0; implIdx < 8; ++implIdx) {
                        sts = api.MFXCreateSession(loader, implIdx, &_session);
                        _last_status = sts;
                        if (sts == MFX_ERR_NONE && _session) {
                            break;
                        }
                    }
                }
            }

            if (sts == MFX_ERR_NONE && _session) {
                _loader = loader;
            } else if (loader) {
                api.MFXUnload(loader);
                _loader = nullptr;
                _session = nullptr;
            }
        }
    }

    if (!_session && api.MFXInitEx) {
        // Fallback to legacy MSDK initialization if modern oneVPL session creation was unavailable
        mfxInitParam initPar{};
        initPar.Implementation = MFX_IMPL_HARDWARE_ANY | MFX_IMPL_VIA_D3D11;
        initPar.Version.Major = 1;
        initPar.Version.Minor = 0;
        initPar.GPUCopy = 1;

        mfxStatus sts = api.MFXInitEx(initPar, &_session);
        _last_status = sts;
        if (sts != MFX_ERR_NONE || !_session) {
            initPar.Implementation = MFX_IMPL_AUTO_ANY;
            sts = api.MFXInitEx(initPar, &_session);
            _last_status = sts;
            if (sts != MFX_ERR_NONE || !_session) {
                close();
                return false;
            }
        }
    }

    if (!_session) {
        close();
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    Microsoft::WRL::ComPtr<ID3D11Multithread> multithread;
    if (SUCCEEDED(dev->QueryInterface(IID_PPV_ARGS(&multithread)))) {
        multithread->SetMultithreadProtected(TRUE);
    }
#endif

    mfxStatus sts = api.MFXVideoCORE_SetHandle(_session, MFX_HANDLE_D3D11_DEVICE, d3d_device);
    _last_status = sts;
    if (sts != MFX_ERR_NONE) {
        close();
        return false;
    }

    return true;
}

mfxStatus QsvSession::last_status() const noexcept {
    return _last_status;
}

bool QsvSession::configure(const EncoderConfig& config) {
    if (!_session || !_api) return false;
    if (config.bitrate_kbps < 500 || config.bitrate_kbps > 150000) return false;

    std::lock_guard<std::mutex> lock(_mutex);
    _config = config;

    std::memset(&_params, 0, sizeof(mfxVideoParam));
    std::memset(&_ext_opt, 0, sizeof(mfxExtCodingOption));
    std::memset(&_ext_opt2, 0, sizeof(mfxExtCodingOption2));

    uint32_t codec_id = MFX_CODEC_HEVC;
    if (config.codec == static_cast<uint32_t>(VideoCodec::H264)) {
        codec_id = MFX_CODEC_AVC;
    } else if (config.codec == static_cast<uint32_t>(VideoCodec::Av1)) {
        codec_id = MFX_CODEC_AV1;
    }

    _params.mfx.CodecId = codec_id;
    _params.mfx.TargetUsage = static_cast<uint16_t>(_usage == QsvTargetUsage::BestSpeed ? MFX_TARGETUSAGE_BEST_SPEED : (_usage == QsvTargetUsage::Balanced ? MFX_TARGETUSAGE_BALANCED : MFX_TARGETUSAGE_BEST_QUALITY));
    _params.mfx.TargetKbps = static_cast<uint16_t>(std::min<uint32_t>(config.bitrate_kbps, 65535));
    _params.mfx.MaxKbps = _params.mfx.TargetKbps;
    _params.mfx.RateControlMethod = MFX_RATECONTROL_CBR;
    _params.mfx.BufferSizeInKB = static_cast<uint16_t>(std::max<uint32_t>(100, config.bitrate_kbps / 8));
    _params.mfx.InitialDelayInKB = static_cast<uint16_t>(_params.mfx.BufferSizeInKB / 2);
    _params.mfx.LowPower = 0;
    _params.mfx.GopRefDist = 1; // Zero B-frames
    _params.mfx.GopPicSize = static_cast<uint16_t>(config.gop_length > 0 ? config.gop_length : 60);
    _params.mfx.IdrInterval = 0;
    _params.mfx.NumSlice = 1;

    _params.mfx.FrameInfo.FourCC = (config.codec == static_cast<uint32_t>(VideoCodec::HevcMain10)) ? MFX_FOURCC_P010 : MFX_FOURCC_NV12;
    _params.mfx.FrameInfo.Width = static_cast<uint16_t>((config.width + 15) & ~15);
    _params.mfx.FrameInfo.Height = static_cast<uint16_t>((config.height + 15) & ~15);
    _params.mfx.FrameInfo.CropX = 0;
    _params.mfx.FrameInfo.CropY = 0;
    _params.mfx.FrameInfo.CropW = static_cast<uint16_t>(config.width);
    _params.mfx.FrameInfo.CropH = static_cast<uint16_t>(config.height);
    _params.mfx.FrameInfo.FrameRateExtN = config.fps > 0 ? config.fps : 60;
    _params.mfx.FrameInfo.FrameRateExtD = 1;
    _params.mfx.FrameInfo.PicStruct = 1; // Progressive
    _params.mfx.FrameInfo.ChromaFormat = 1; // 4:2:0

    _params.IOPattern = MFX_IOPATTERN_IN_VIDEO_MEMORY;
    _params.AsyncDepth = 1; // Ultra low latency single-frame pipelining

    // Coding Option 1
    _ext_opt.Header.BufferId = MFX_EXTBUFF_CODING_OPTION;
    _ext_opt.Header.BufferSz = sizeof(mfxExtCodingOption);
    _ext_opt.AUDelimiter = 1;
    _ext_opt.PicTimingSEI = 1;

    // Coding Option 2
    _ext_opt2.Header.BufferId = MFX_EXTBUFF_CODING_OPTION2;
    _ext_opt2.Header.BufferSz = sizeof(mfxExtCodingOption2);
    _ext_opt2.RepeatPPS = 1;
    _ext_opt2.LookAheadDepth = 0; // Zero lookahead for streaming

    if (_intra_refresh_enabled && _intra_refresh_cycle_size > 0) {
        _ext_opt2.IntRefType = 1; // Row / slice based refresh
        _ext_opt2.IntRefCycleSize = static_cast<uint16_t>(_intra_refresh_cycle_size);
        _ext_opt2.IntRefQPDelta = static_cast<int16_t>(_intra_refresh_qp_delta);
    }

    _params.ExtParam = nullptr;
    _params.NumExtParam = 0;

    mfxVideoParam inParams = _params;
    if (_api->MFXVideoENCODE_Query) {
        _api->MFXVideoENCODE_Query(_session, &inParams, &_params);
    }

    mfxStatus sts = _api->MFXVideoENCODE_Init(_session, &_params);
    _last_status = sts;
    if (sts < MFX_ERR_NONE) {
        // Try with H.264 if HEVC was attempted
        if (codec_id != MFX_CODEC_AVC) {
            _params.mfx.CodecId = MFX_CODEC_AVC;
            inParams = _params;
            if (_api->MFXVideoENCODE_Query) {
                _api->MFXVideoENCODE_Query(_session, &inParams, &_params);
            }
            sts = _api->MFXVideoENCODE_Init(_session, &_params);
            _last_status = sts;
        }
        if (sts < MFX_ERR_NONE) {
            return false;
        }
    }

    // Allocate bitstream internal cache
    uint32_t buf_sz = std::max(1024u * 1024u, config.width * config.height * 2);
    _bitstream_buffer.resize(buf_sz);

    _is_configured = true;
    return true;
}

bool QsvSession::encode(
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
    std::memset(&out_desc, 0, sizeof(EncodedPacketDesc));

    if (!_session || !_is_configured || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

    std::lock_guard<std::mutex> lock(_mutex);

    mfxFrameSurface1 surface{};
    surface.Info = _params.mfx.FrameInfo;
    surface.Data.MemId = d3d_texture;
    surface.Data.MemType = MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET | MFX_MEMTYPE_FROM_ENCODE;
    surface.Data.TimeStamp = timestamp_us * 90; // 90 kHz clock

    mfxBitstream bs{};
    bs.Data = _bitstream_buffer.data();
    bs.MaxLength = static_cast<uint32_t>(_bitstream_buffer.size());
    bs.DataOffset = 0;
    bs.DataLength = 0;

    mfxSyncPoint syncp = nullptr;
    mfxStatus sts = _api->MFXVideoENCODE_EncodeFrameAsync(_session, nullptr, &surface, &bs, &syncp);

    if (sts == MFX_ERR_NONE && syncp) {
        sts = _api->MFXVideoCORE_SyncOperation(_session, syncp, 1000); // 1000ms timeout
    }

    if (sts != MFX_ERR_NONE || bs.DataLength == 0) {
        return false;
    }

    if (bs.DataLength > max_buffer_size) {
        out_written_size = 0;
        return false; // Fail closed, buffer too small
    }

    std::memcpy(out_bitstream, bs.Data + bs.DataOffset, bs.DataLength);
    out_written_size = bs.DataLength;

    out_desc.frame_index = frame_id;
    out_desc.timestamp_qpc = timestamp_us;
    out_desc.payload_size = out_written_size;
    out_desc.is_keyframe = (force_idr || (bs.FrameType & MFX_FRAMETYPE_IDR) || (bs.FrameType & MFX_FRAMETYPE_I)) ? 1 : 0;
    out_desc.is_header_packet = out_desc.is_keyframe;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    return true;
}

bool QsvSession::reconfigure(const EncoderConfig& new_config) {
    if (!_session || !_is_configured) return false;
    if (new_config.bitrate_kbps < 500 || new_config.bitrate_kbps > 150000) return false;

    std::lock_guard<std::mutex> lock(_mutex);

    _config = new_config;
    _params.mfx.TargetKbps = static_cast<uint16_t>(std::min<uint32_t>(new_config.bitrate_kbps, 65535));
    uint32_t peak = new_config.peak_bitrate_kbps > 0 ? new_config.peak_bitrate_kbps : new_config.bitrate_kbps;
    _params.mfx.MaxKbps = static_cast<uint16_t>(std::min<uint32_t>(peak, 65535));
    _params.mfx.FrameInfo.FrameRateExtN = new_config.fps > 0 ? new_config.fps : 60;

    mfxStatus sts = _api->MFXVideoENCODE_Reset(_session, &_params);
    return sts == MFX_ERR_NONE;
}

void QsvSession::close() {
    std::lock_guard<std::mutex> lock(_mutex);
    if (_session && _api) {
        _api->MFXVideoENCODE_Close(_session);
        _api->MFXClose(_session);
        _session = nullptr;
    }
    if (_loader && _api && _api->MFXUnload) {
        _api->MFXUnload(_loader);
        _loader = nullptr;
    }
    _d3d_device = nullptr;
    _api = nullptr;
    _is_configured = false;
}

bool QsvSession::is_open() const noexcept {
    return _session != nullptr;
}

bool QsvSession::is_configured() const noexcept {
    return _is_configured && _session != nullptr;
}

const EncoderConfig& QsvSession::config() const noexcept {
    return _config;
}

void QsvSession::set_target_usage(QsvTargetUsage usage, bool low_power_vdenc) noexcept {
    _usage = usage;
    _low_power_vdenc = low_power_vdenc;
}

void QsvSession::set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta) noexcept {
    _intra_refresh_enabled = enabled;
    _intra_refresh_cycle_size = cycle_size;
    _intra_refresh_qp_delta = qp_delta;
}

} // namespace moonshine::encoder::qsv
