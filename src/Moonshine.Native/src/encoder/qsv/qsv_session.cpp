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

// Convert microseconds to 90 kHz clock ticks (the standard MPEG timebase).
// Formula: ticks_90k = us * 90000 / 1000000 = us * 9 / 100
// Using 90000ULL numerator to preserve precision for large timestamps.
static inline constexpr mfxU64 us_to_90khz(uint64_t timestamp_us) noexcept {
    return static_cast<mfxU64>((timestamp_us * 90000ULL) / 1000000ULL);
}

QsvSession::QsvSession() = default;

QsvSession::~QsvSession() {
    close();
}

QsvSession::QsvSession(QsvSession&& other) noexcept
    : _api(other._api),
      _d3d_device(other._d3d_device),
      _loader(other._loader),
      _session(other._session),
      _last_status(other._last_status),
      _status_impl_filter(other._status_impl_filter),
      _status_accel_filter(other._status_accel_filter),
      _params(other._params),
      _ext_opt(other._ext_opt),
      _ext_opt2(other._ext_opt2),
      _bitstream_buffer(std::move(other._bitstream_buffer)),
      _surface_pool(std::move(other._surface_pool)),
      _surface_index(other._surface_index),
      _output_queue(std::move(other._output_queue)),
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
    other._surface_index = 0;
    _ext_buffers[0] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt);
    _ext_buffers[1] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt2);
    _params.ExtParam = _ext_buffers;

    for (auto& slot : _surface_pool) {
        slot.surface.Data.MemId = &slot.hdl_pair;
    }
}

QsvSession& QsvSession::operator=(QsvSession&& other) noexcept {
    if (this != &other) {
        close();
        _api = other._api;
        _d3d_device = other._d3d_device;
        _loader = other._loader;
        _session = other._session;
        _last_status = other._last_status;
        _status_impl_filter = other._status_impl_filter;
        _status_accel_filter = other._status_accel_filter;
        _params = other._params;
        _ext_opt = other._ext_opt;
        _ext_opt2 = other._ext_opt2;
        _bitstream_buffer = std::move(other._bitstream_buffer);
        _surface_pool = std::move(other._surface_pool);
        _surface_index = other._surface_index;
        _output_queue = std::move(other._output_queue);
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
        other._surface_index = 0;
        _ext_buffers[0] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt);
        _ext_buffers[1] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt2);
        _params.ExtParam = _ext_buffers;

        for (auto& slot : _surface_pool) {
            slot.surface.Data.MemId = &slot.hdl_pair;
        }
    }
    return *this;
}

bool QsvSession::open(QsvApi& api, void* d3d_device) {
    close();

    _status_impl_filter = MFX_ERR_NOT_INITIALIZED;
    _status_accel_filter = MFX_ERR_NOT_INITIALIZED;

    if (!api.is_loaded() || !d3d_device) {
        _last_status = MFX_ERR_NULL_PTR;
        return false;
    }

    _api = &api;
    _d3d_device = d3d_device;

    // Strict modern oneVPL 2.x session initialisation workflow
    if (!api.is_vpl() || !api.MFXLoad || !api.MFXCreateConfig ||
        !api.MFXSetConfigFilterProperty || !api.MFXCreateSession ||
        !api.MFXVideoCORE_SetHandle || !api.MFXUnload) {
        _last_status = MFX_ERR_UNSUPPORTED;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

    mfxLoader loader = api.MFXLoad();
    if (!loader) {
        _last_status = MFX_ERR_MEMORY_ALLOC;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

    // Configure hardware implementation filter property
    mfxConfig cfg1 = api.MFXCreateConfig(loader);
    if (!cfg1) {
        api.MFXUnload(loader);
        _last_status = MFX_ERR_MEMORY_ALLOC;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

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
    _status_impl_filter = sts;
    _last_status = sts;
    if (sts != MFX_ERR_NONE) {
        api.MFXUnload(loader);
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

    // Configure Direct3D 11 acceleration mode filter property
    mfxConfig cfg2 = api.MFXCreateConfig(loader);
    if (!cfg2) {
        api.MFXUnload(loader);
        _last_status = MFX_ERR_MEMORY_ALLOC;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

    mfxVariant accel_var{};
    accel_var.Version.Major = 1;
    accel_var.Version.Minor = 0;
    accel_var.Type = MFX_VARIANT_TYPE_U32;
    accel_var.Data.U32 = MFX_ACCEL_MODE_VIA_D3D11;
    sts = api.MFXSetConfigFilterProperty(
        cfg2,
        reinterpret_cast<const uint8_t*>("mfxImplDescription.AccelerationMode"),
        accel_var
    );
    _status_accel_filter = sts;
    _last_status = sts;
    if (sts != MFX_ERR_NONE) {
        api.MFXUnload(loader);
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    Microsoft::WRL::ComPtr<ID3D11Multithread> multithread;
    if (SUCCEEDED(dev->QueryInterface(IID_PPV_ARGS(&multithread)))) {
        multithread->SetMultithreadProtected(TRUE);
    }
#endif

    // Enumerate candidate hardware sessions matching filtered configuration and bind D3D11 handle
    mfxSession session = nullptr;
    sts = MFX_ERR_NOT_FOUND;
    for (uint32_t impl_idx = 0; impl_idx < 8; ++impl_idx) {
        session = nullptr;
        sts = api.MFXCreateSession(loader, impl_idx, &session);
        _last_status = sts;
        if (sts == MFX_ERR_NONE && session != nullptr) {
            sts = api.MFXVideoCORE_SetHandle(session, MFX_HANDLE_D3D11_DEVICE, d3d_device);
            if (sts == MFX_ERR_NONE) {
                _session = session;
                _loader = loader;
                _last_status = MFX_ERR_NONE;
                return true;
            }
            if (api.MFXClose) {
                api.MFXClose(session);
            }
        }
        if (sts == MFX_ERR_NOT_FOUND) {
            break;
        }
    }

    api.MFXUnload(loader);
    _loader = nullptr;
    _session = nullptr;
    _api = nullptr;
    _d3d_device = nullptr;
    return false;
}

mfxStatus QsvSession::last_status() const noexcept {
    return _last_status;
}

mfxStatus QsvSession::impl_filter_status() const noexcept {
    return _status_impl_filter;
}

mfxStatus QsvSession::accel_filter_status() const noexcept {
    return _status_accel_filter;
}

mfxSession QsvSession::session() const noexcept {
    return _session;
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

    uint16_t multiplier = 1;
    uint32_t target_kbps = config.bitrate_kbps;
    uint32_t max_kbps = config.peak_bitrate_kbps > 0 ? config.peak_bitrate_kbps : config.bitrate_kbps;
    uint32_t highest_kbps = std::max(target_kbps, max_kbps);
    if (highest_kbps > 65535) {
        multiplier = static_cast<uint16_t>((highest_kbps + 65534) / 65535);
    }

    _params.mfx.CodecId = codec_id;
    _params.mfx.TargetUsage = static_cast<uint16_t>(_usage == QsvTargetUsage::BestSpeed ? MFX_TARGETUSAGE_BEST_SPEED : (_usage == QsvTargetUsage::Balanced ? MFX_TARGETUSAGE_BALANCED : MFX_TARGETUSAGE_BEST_QUALITY));
    _params.mfx.BRCParamMultiplier = multiplier;
    _params.mfx.TargetKbps = static_cast<uint16_t>(target_kbps / multiplier);
    _params.mfx.MaxKbps = static_cast<uint16_t>(max_kbps / multiplier);
    _params.mfx.RateControlMethod = MFX_RATECONTROL_CBR;
    _params.mfx.BufferSizeInKB = static_cast<uint16_t>(std::max<uint32_t>(100, (target_kbps / 8) / multiplier));
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

    _ext_buffers[0] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt);
    _ext_buffers[1] = reinterpret_cast<mfxExtBuffer*>(&_ext_opt2);
    _params.ExtParam = _ext_buffers;
    _params.NumExtParam = 2;

    mfxVideoParam inParams = _params;
    if (_api->MFXVideoENCODE_Query) {
        _api->MFXVideoENCODE_Query(_session, &inParams, &_params);
    }

    mfxStatus sts = _api->MFXVideoENCODE_Init(_session, &_params);
    _last_status = sts;
    if (sts < MFX_ERR_NONE) {
        return false;
    }

    // Allocate bitstream internal cache
    uint32_t buf_sz = std::max(1024u * 1024u, config.width * config.height * 2);
    _bitstream_buffer.resize(buf_sz);

    // Initialize TrackedSurface pool with proper oneVPL mfxHDLPair bindings
    _surface_pool.resize(16);
    _surface_index = 0;
    for (auto& slot : _surface_pool) {
        std::memset(&slot.surface, 0, sizeof(mfxFrameSurface1));
        slot.surface.Info = _params.mfx.FrameInfo;
        slot.surface.Data.MemType = MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET | MFX_MEMTYPE_FROM_ENCODE;
        slot.hdl_pair.first = nullptr;
        slot.hdl_pair.second = (mfxHDL)(uintptr_t)0;
        slot.surface.Data.MemId = &slot.hdl_pair;
        slot.d3d_texture = nullptr;
        slot.in_use = false;
        slot.frame_id = 0;
    }

    _is_configured = true;
    return true;
}

EncodeResult QsvSession::encode(
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

    if (!_session || !_is_configured || !out_bitstream || max_buffer_size == 0) {
        return EncodeResult::Failed;
    }

    std::lock_guard<std::mutex> lock(_mutex);

    // Consume pending queued packet if available (e.g. from prior drain)
    if (!_output_queue.empty()) {
        auto pkt = std::move(_output_queue.front());
        _output_queue.pop();

        if (pkt.data.size() > max_buffer_size) {
            return EncodeResult::Failed;
        }

        std::memcpy(out_bitstream, pkt.data.data(), pkt.data.size());
        out_written_size = static_cast<uint32_t>(pkt.data.size());
        out_desc = pkt.desc;
        out_desc.frame_index = frame_id;
        out_desc.timestamp_qpc = timestamp_us;
        out_desc.payload_size = out_written_size;
        return EncodeResult::OutputProduced;
    }

    if (!d3d_texture) {
        return EncodeResult::Failed;
    }

    // Locate available surface from tracked surface pool
    TrackedSurface* slot = nullptr;
    for (size_t i = 0; i < _surface_pool.size(); ++i) {
        size_t idx = (_surface_index + i) % _surface_pool.size();
        if (!_surface_pool[idx].in_use) {
            _surface_index = (idx + 1) % _surface_pool.size();
            slot = &_surface_pool[idx];
            break;
        }
    }
    if (!slot) {
        slot = &_surface_pool[_surface_index];
        _surface_index = (_surface_index + 1) % _surface_pool.size();
    }

    // Configure proper oneVPL D3D11 texture surface binding via mfxHDLPair
    slot->d3d_texture = d3d_texture;
    slot->hdl_pair.first = d3d_texture;
    slot->hdl_pair.second = (mfxHDL)(uintptr_t)0;
    slot->surface.Info = _params.mfx.FrameInfo;
    slot->surface.Data.MemType = MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET | MFX_MEMTYPE_FROM_ENCODE;
    slot->surface.Data.MemId = &slot->hdl_pair;
    slot->surface.Data.TimeStamp = us_to_90khz(timestamp_us);
    slot->in_use = true;
    slot->frame_id = frame_id;

    mfxBitstream bs{};
    bs.Data = _bitstream_buffer.data();
    bs.MaxLength = static_cast<uint32_t>(_bitstream_buffer.size());
    bs.DataOffset = 0;
    bs.DataLength = 0;

    // Construct per-frame encode control to enforce IDR when requested.
    // Passing nullptr means the encoder chooses the frame type autonomously,
    // which does NOT honour force_idr requests.
    mfxEncodeCtrl ctrl{};
    mfxEncodeCtrl* ctrl_ptr = nullptr;
    if (force_idr) {
        ctrl.FrameType = MFX_FRAMETYPE_I | MFX_FRAMETYPE_IDR | MFX_FRAMETYPE_REF;
        ctrl_ptr = &ctrl;
    }

    mfxSyncPoint syncp = nullptr;
    mfxStatus sts = _api->MFXVideoENCODE_EncodeFrameAsync(_session, ctrl_ptr, &slot->surface, &bs, &syncp);

    if (sts < MFX_ERR_NONE) {
        // Surface rejected by encoder
        slot->in_use = false;
        slot->d3d_texture = nullptr;
        _last_status = sts;
        return EncodeResult::Failed;
    }

    if (sts == MFX_ERR_MORE_DATA) {
        slot->in_use = false;
        slot->d3d_texture = nullptr;
        return EncodeResult::AcceptedNoOutput;
    }

    if (sts == MFX_ERR_NONE && syncp) {
        sts = _api->MFXVideoCORE_SyncOperation(_session, syncp, 1000); // 1000ms timeout
        _last_status = sts;
    }

    // Release surface slot after synchronization
    slot->in_use = false;
    slot->d3d_texture = nullptr;

    if (sts != MFX_ERR_NONE) {
        return EncodeResult::Failed;
    }

    if (bs.DataLength == 0) {
        return EncodeResult::AcceptedNoOutput;
    }

    if (bs.DataLength > max_buffer_size) {
        out_written_size = 0;
        return EncodeResult::Failed; // Fail closed, buffer too small
    }

    std::memcpy(out_bitstream, bs.Data + bs.DataOffset, bs.DataLength);
    out_written_size = bs.DataLength;

    out_desc.frame_index = frame_id;
    out_desc.timestamp_qpc = timestamp_us;
    out_desc.payload_size = out_written_size;
    // Determine keyframe status from the actual bitstream FrameType flags.
    // Do NOT rely on the force_idr request flag: the encoder may not honour it,
    // and the bitstream is the sole authority on what was actually produced.
    out_desc.is_keyframe = ((bs.FrameType & MFX_FRAMETYPE_IDR) || (bs.FrameType & MFX_FRAMETYPE_I)) ? 1 : 0;
    out_desc.is_header_packet = out_desc.is_keyframe;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    return EncodeResult::OutputProduced;
}

bool QsvSession::reconfigure(const EncoderConfig& new_config) {
    if (!_session || !_is_configured || !_api) return false;
    if (new_config.bitrate_kbps < 500 || new_config.bitrate_kbps > 150000) return false;

    std::lock_guard<std::mutex> lock(_mutex);

    bool resolution_changed = (new_config.width != _config.width || new_config.height != _config.height);
    _config = new_config;

    uint16_t multiplier = 1;
    uint32_t target_kbps = new_config.bitrate_kbps;
    uint32_t max_kbps = new_config.peak_bitrate_kbps > 0 ? new_config.peak_bitrate_kbps : new_config.bitrate_kbps;
    uint32_t highest_kbps = (std::max)(target_kbps, max_kbps);
    if (highest_kbps > 65535) {
        multiplier = static_cast<uint16_t>((highest_kbps + 65534) / 65535);
    }

    _params.mfx.BRCParamMultiplier = multiplier;
    _params.mfx.TargetKbps = static_cast<uint16_t>(target_kbps / multiplier);
    _params.mfx.MaxKbps = static_cast<uint16_t>(max_kbps / multiplier);
    _params.mfx.BufferSizeInKB = static_cast<uint16_t>((std::max)(100u, static_cast<uint32_t>((target_kbps / 8) / multiplier)));
    _params.mfx.InitialDelayInKB = static_cast<uint16_t>(_params.mfx.BufferSizeInKB / 2);
    _params.mfx.FrameInfo.FrameRateExtN = new_config.fps > 0 ? new_config.fps : 60;

    if (resolution_changed) {
        _params.mfx.FrameInfo.Width = static_cast<uint16_t>((new_config.width + 15) & ~15);
        _params.mfx.FrameInfo.Height = static_cast<uint16_t>((new_config.height + 15) & ~15);
        _params.mfx.FrameInfo.CropW = static_cast<uint16_t>(new_config.width);
        _params.mfx.FrameInfo.CropH = static_cast<uint16_t>(new_config.height);

        uint32_t buf_sz = (std::max)(1024u * 1024u, new_config.width * new_config.height * 2);
        if (_bitstream_buffer.size() < buf_sz) {
            _bitstream_buffer.resize(buf_sz);
        }
    }

    mfxVideoParam inParams = _params;
    if (_api->MFXVideoENCODE_Query) {
        _api->MFXVideoENCODE_Query(_session, &inParams, &_params);
    }

    mfxStatus sts = MFX_ERR_NONE;
    if (_api->MFXVideoENCODE_Reset) {
        sts = _api->MFXVideoENCODE_Reset(_session, &_params);
    } else {
        sts = MFX_ERR_UNSUPPORTED;
    }

    if (sts < MFX_ERR_NONE) {
        if (_api->MFXVideoENCODE_Close && _api->MFXVideoENCODE_Init) {
            _api->MFXVideoENCODE_Close(_session);
            sts = _api->MFXVideoENCODE_Init(_session, &_params);
            _last_status = sts;
            if (sts < MFX_ERR_NONE) {
                return false;
            }
        } else {
            return false;
        }
    }

    // Refresh tracked surface pool frame parameters
    for (auto& slot : _surface_pool) {
        slot.surface.Info = _params.mfx.FrameInfo;
        slot.surface.Data.MemType = MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET | MFX_MEMTYPE_FROM_ENCODE;
        slot.surface.Data.MemId = &slot.hdl_pair;
        slot.in_use = false;
        slot.d3d_texture = nullptr;
    }

    _last_status = sts;
    return true;
}

bool QsvSession::drain() {
    if (!_session || !_api || !_api->MFXVideoENCODE_EncodeFrameAsync) return false;
    std::lock_guard<std::mutex> lock(_mutex);

    mfxBitstream bs{};
    bs.Data = _bitstream_buffer.data();
    bs.MaxLength = static_cast<uint32_t>(_bitstream_buffer.size());

    mfxSyncPoint syncp = nullptr;
    mfxStatus sts = MFX_ERR_NONE;

    for (int i = 0; i < 30; ++i) {
        syncp = nullptr;
        bs.DataOffset = 0;
        bs.DataLength = 0;
        sts = _api->MFXVideoENCODE_EncodeFrameAsync(_session, nullptr, nullptr, &bs, &syncp);
        if (sts == MFX_ERR_NONE && syncp) {
            if (_api->MFXVideoCORE_SyncOperation) {
                mfxStatus sync_sts = _api->MFXVideoCORE_SyncOperation(_session, syncp, 500);
                if (sync_sts == MFX_ERR_NONE && bs.DataLength > 0) {
                    QsvPendingPacket pkt{};
                    pkt.data.assign(bs.Data + bs.DataOffset, bs.Data + bs.DataOffset + bs.DataLength);
                    pkt.desc.payload_size = bs.DataLength;
                    pkt.desc.is_keyframe = ((bs.FrameType & MFX_FRAMETYPE_IDR) || (bs.FrameType & MFX_FRAMETYPE_I)) ? 1 : 0;
                    pkt.desc.is_header_packet = pkt.desc.is_keyframe;
                    _output_queue.push(std::move(pkt));
                }
            }
        } else if (sts == MFX_ERR_MORE_DATA) {
            break;
        } else if (sts < MFX_ERR_NONE) {
            break;
        }
    }

    for (auto& slot : _surface_pool) {
        slot.in_use = false;
        slot.d3d_texture = nullptr;
    }

    return true;
}

bool QsvSession::flush() {
    if (!_session || !_api) return false;
    std::lock_guard<std::mutex> lock(_mutex);

    while (!_output_queue.empty()) {
        _output_queue.pop();
    }

    for (auto& slot : _surface_pool) {
        slot.in_use = false;
        slot.d3d_texture = nullptr;
    }

    if (_api->MFXVideoENCODE_Reset) {
        mfxStatus sts = _api->MFXVideoENCODE_Reset(_session, &_params);
        if (sts >= MFX_ERR_NONE) {
            _last_status = sts;
            return true;
        }
    }

    if (_api->MFXVideoENCODE_Close && _api->MFXVideoENCODE_Init) {
        _api->MFXVideoENCODE_Close(_session);
        mfxStatus sts = _api->MFXVideoENCODE_Init(_session, &_params);
        _last_status = sts;
        return sts >= MFX_ERR_NONE;
    }
    return false;
}

void QsvSession::close() {
    std::lock_guard<std::mutex> lock(_mutex);
    if (_session && _api) {
        if (_api->MFXVideoENCODE_Close) {
            _api->MFXVideoENCODE_Close(_session);
        }
        if (_api->MFXClose) {
            _api->MFXClose(_session);
        }
        _session = nullptr;
    }
    if (_loader && _api && _api->MFXUnload) {
        _api->MFXUnload(_loader);
        _loader = nullptr;
    }
    while (!_output_queue.empty()) {
        _output_queue.pop();
    }
    _surface_pool.clear();
    _surface_index = 0;
    _d3d_device = nullptr;
    _api = nullptr;
    _is_configured = false;
}

size_t QsvSession::pending_output_count() const noexcept {
    std::lock_guard<std::mutex> lock(_mutex);
    return _output_queue.size();
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

// ============================================================================
// Legacy Intel Media SDK (MSDK) Compatibility Helper Implementation
// ============================================================================

LegacyMfxSession::LegacyMfxSession() = default;

LegacyMfxSession::~LegacyMfxSession() {
    close();
}

LegacyMfxSession::LegacyMfxSession(LegacyMfxSession&& other) noexcept
    : _api(other._api),
      _d3d_device(other._d3d_device),
      _session(other._session),
      _last_status(other._last_status) {
    other._api = nullptr;
    other._d3d_device = nullptr;
    other._session = nullptr;
}

LegacyMfxSession& LegacyMfxSession::operator=(LegacyMfxSession&& other) noexcept {
    if (this != &other) {
        close();
        _api = other._api;
        _d3d_device = other._d3d_device;
        _session = other._session;
        _last_status = other._last_status;

        other._api = nullptr;
        other._d3d_device = nullptr;
        other._session = nullptr;
    }
    return *this;
}

bool LegacyMfxSession::open(QsvApi& api, void* d3d_device) {
    close();

    if (!api.is_loaded() || !d3d_device) {
        _last_status = MFX_ERR_NULL_PTR;
        return false;
    }

    _api = &api;
    _d3d_device = d3d_device;

    if (!api.MFXInitEx || !api.MFXClose || !api.MFXVideoCORE_SetHandle) {
        _last_status = MFX_ERR_UNSUPPORTED;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

    mfxInitParam init_param{};
    init_param.Implementation = MFX_IMPL_HARDWARE_ANY | MFX_IMPL_VIA_D3D11;
    init_param.Version.Major = 1;
    init_param.Version.Minor = 0;
    init_param.GPUCopy = 1;

    mfxStatus sts = api.MFXInitEx(init_param, &_session);
    _last_status = sts;
    if (sts != MFX_ERR_NONE || !_session) {
        _session = nullptr;
        _api = nullptr;
        _d3d_device = nullptr;
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    Microsoft::WRL::ComPtr<ID3D11Multithread> multithread;
    if (SUCCEEDED(dev->QueryInterface(IID_PPV_ARGS(&multithread)))) {
        multithread->SetMultithreadProtected(TRUE);
    }
#endif

    sts = api.MFXVideoCORE_SetHandle(_session, MFX_HANDLE_D3D11_DEVICE, d3d_device);
    _last_status = sts;
    if (sts != MFX_ERR_NONE) {
        close();
        return false;
    }

    return true;
}

void LegacyMfxSession::close() {
    std::lock_guard<std::mutex> lock(_mutex);
    if (_session && _api && _api->MFXClose) {
        _api->MFXClose(_session);
        _session = nullptr;
    }
    _d3d_device = nullptr;
    _api = nullptr;
}

bool LegacyMfxSession::is_open() const noexcept {
    return _session != nullptr;
}

mfxSession LegacyMfxSession::session() const noexcept {
    return _session;
}

mfxStatus LegacyMfxSession::last_status() const noexcept {
    return _last_status;
}

} // namespace moonshine::encoder::qsv
