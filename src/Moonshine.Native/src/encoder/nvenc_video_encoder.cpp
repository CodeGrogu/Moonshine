#include <cstring>
#include <memory>
#include <vector>
#include <atomic>
#include <mutex>
#include <unordered_map>
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "moonshine/export/moonshine_native_api.h"
#include "encoder/nvenc/nvenc_api.hpp"
#include "encoder/nvenc/nvenc_session.hpp"
#include "encoder/nvenc/nvenc_surface_pool.hpp"
#include "encoder/nvenc/nvenc_types.hpp"

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder {

struct NvencVideoEncoder::Impl {
    nvenc::NvencApi api;
    nvenc::NvencSession session;
    nvenc::NvencSurfacePool surface_pool;
#if defined(_WIN32)
    ComPtr<ID3D11Device> internal_device;
    ComPtr<ID3D11DeviceContext> internal_context;
    ComPtr<ID3D11Texture2D> internal_texture;
#endif
};

NvencVideoEncoder::NvencVideoEncoder()
    : _impl(std::make_unique<Impl>()) {
}

NvencVideoEncoder::~NvencVideoEncoder() {
    cleanup();
}

bool NvencVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

    _state = NvencLifecycleState::Uninitialised;

    if (!d3d_device || !_impl) {
        return false;
    }

#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(d3d_device);

    // Detect device removal/reset before initialization
    HRESULT reason = dev->GetDeviceRemovedReason();
    if (reason != S_OK) {
        _state = NvencLifecycleState::Faulted;
        return false;
    }

    // Verify adapter is genuine NVIDIA hardware
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

    _state = NvencLifecycleState::DeviceAttached;

    // Load NVENC API
    if (!_impl->api.load()) {
        _state = NvencLifecycleState::Faulted;
        return false;
    }

    // Open session on D3D11 device
    if (!_impl->session.open(_impl->api, d3d_device)) {
        _impl->api.unload();
        _state = NvencLifecycleState::Faulted;
        return false;
    }

    _state = NvencLifecycleState::SessionCreated;

    // Configure encoder session
    _impl->session.set_preset_and_tuning(_preset, _tuning);
    _impl->session.set_intra_refresh(_intra_refresh_enabled, _intra_refresh_period, _intra_refresh_count);
    if (!_impl->session.configure(config)) {
        _impl->session.close();
        _impl->api.unload();
        _state = NvencLifecycleState::Faulted;
        return false;
    }

    _state = NvencLifecycleState::EncoderInitialised;

    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;
    _initialized = true;
    _state = NvencLifecycleState::Ready;
    return true;
#else
    (void)d3d_device;
    (void)config;
    return false;
#endif
}

bool NvencVideoEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
#if defined(_WIN32)
    out_written_size = 0;

    if (_state == NvencLifecycleState::Faulted || _state == NvencLifecycleState::Disposed ||
        !_initialized || !_impl || !_impl->session.is_open() || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev) {
        HRESULT reason = dev->GetDeviceRemovedReason();
        if (reason != S_OK) {
            _state = NvencLifecycleState::Faulted;
            return false;
        }
    }

    // Determine NVENC buffer format from D3D11 texture descriptor
    auto* p_tex = static_cast<ID3D11Texture2D*>(d3d_texture);
    D3D11_TEXTURE2D_DESC tex_desc{};
    p_tex->GetDesc(&tex_desc);

    uint32_t buffer_format = nvenc::NV_ENC_BUFFER_FORMAT_ABGR;
    if (tex_desc.Format == DXGI_FORMAT_R10G10B10A2_UNORM) {
        buffer_format = nvenc::NV_ENC_BUFFER_FORMAT_ABGR10;
    } else if (tex_desc.Format == DXGI_FORMAT_NV12) {
        buffer_format = nvenc::NV_ENC_BUFFER_FORMAT_NV12;
    } else if (tex_desc.Format == DXGI_FORMAT_P010) {
        buffer_format = nvenc::NV_ENC_BUFFER_FORMAT_P010;
    } else if (tex_desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM) {
        buffer_format = nvenc::NV_ENC_BUFFER_FORMAT_ARGB;
    } else {
        // Unsupported input texture format: reject frame with explicit diagnostic
        return false;
    }

    // Obtain or register cached surface
    void* registered_resource = _impl->surface_pool.get_or_register_surface(
        _impl->session.session_handle(),
        _impl->api.functions(),
        d3d_texture,
        _config.width,
        _config.height,
        buffer_format
    );

    if (!registered_resource) {
        _state = NvencLifecycleState::Faulted;
        return false;
    }

    _state = NvencLifecycleState::ResourcesRegistered;

    bool is_key = force_idr || _force_keyframe.load() || (_frame_counter.load() == 0);

    _state = NvencLifecycleState::Encoding;

    bool success = _impl->session.encode(
        registered_resource,
        is_key,
        static_cast<uint32_t>(_frame_counter.load()),
        out_desc,
        out_bitstream,
        max_buffer_size,
        out_written_size
    );

    if (success) {
        _force_keyframe = false;
        _frame_counter++;
        _state = NvencLifecycleState::Ready;
    } else {
        if (dev && dev->GetDeviceRemovedReason() != S_OK) {
            _state = NvencLifecycleState::Faulted;
        } else {
            _state = NvencLifecycleState::Ready;
        }
        out_written_size = 0;
    }

    return success;
#else
    (void)d3d_texture;
    (void)force_idr;
    (void)out_desc;
    (void)out_bitstream;
    (void)max_buffer_size;
    out_written_size = 0;
    return false;
#endif
}

bool NvencVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized || !_impl || _state == NvencLifecycleState::Faulted || _state == NvencLifecycleState::Disposed) {
        return false;
    }
#if defined(_WIN32)
    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        _state = NvencLifecycleState::Faulted;
        return false;
    }
#endif
    _config = new_config;
    _force_keyframe = true;
    bool success = _impl->session.reconfigure(new_config);
    if (!success) {
#if defined(_WIN32)
        if (dev && dev->GetDeviceRemovedReason() != S_OK) {
            _state = NvencLifecycleState::Faulted;
        }
#endif
        return false;
    }
    _state = NvencLifecycleState::Ready;
    return true;
}

void NvencVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void NvencVideoEncoder::cleanup() {
    _initialized = false;

    if (_impl) {
        _state = NvencLifecycleState::Flushing;
        if (_impl->session.is_open() && _impl->api.is_loaded()) {
            _impl->surface_pool.clear(_impl->session.session_handle(), _impl->api.functions());
        }
        _impl->session.close();
        _impl->api.unload();
    }

    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
    _state = NvencLifecycleState::Disposed;
}

bool NvencVideoEncoder::is_healthy() const noexcept {
#if defined(_WIN32)
    if (!_initialized || _state == NvencLifecycleState::Faulted ||
        _state == NvencLifecycleState::Disposed || _state == NvencLifecycleState::Uninitialised ||
        !_impl || !_impl->session.is_open() || !_d3d_device) {
        return false;
    }
    auto* dev = static_cast<ID3D11Device*>(_d3d_device);
    if (dev && dev->GetDeviceRemovedReason() != S_OK) {
        return false;
    }
    return true;
#else
    return false;
#endif
}

bool NvencVideoEncoder::set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) {
    _preset = preset;
    _tuning = tuning;
    if (_impl) {
        _impl->session.set_preset_and_tuning(preset, tuning);
    }
    return true;
}

bool NvencVideoEncoder::set_intra_refresh(bool enabled, uint32_t period, uint32_t count) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_period = period;
    _intra_refresh_count = count;
    if (_impl) {
        _impl->session.set_intra_refresh(enabled, period, count);
    }
    return true;
}

bool NvencVideoEncoder::drain() {
    if (!_initialized || !_impl || _state == NvencLifecycleState::Faulted || _state == NvencLifecycleState::Disposed) {
        return false;
    }
    _state = NvencLifecycleState::Flushing;
    bool res = _impl->session.drain();
    _state = NvencLifecycleState::Ready;
    return res;
}

bool NvencVideoEncoder::flush() {
    if (!_initialized || !_impl || _state == NvencLifecycleState::Faulted || _state == NvencLifecycleState::Disposed) {
        return false;
    }
    _state = NvencLifecycleState::Flushing;
    bool res = _impl->session.flush();
    _force_keyframe = true;
    _state = NvencLifecycleState::Ready;
    return res;
}

bool NvencVideoEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::NvidiaNvenc);

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) return false;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return false;

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) return false;

    // Cache dynamic capability query results by adapter LUID to prevent redundant GPU session creation overhead
    static std::mutex s_caps_mutex;
    static std::unordered_map<uint64_t, EncoderCaps> s_caps_cache;

    uint64_t luid = (static_cast<uint64_t>(desc.AdapterLuid.HighPart) << 32) | static_cast<uint64_t>(desc.AdapterLuid.LowPart);
    {
        std::lock_guard<std::mutex> lock(s_caps_mutex);
        auto it = s_caps_cache.find(luid);
        if (it != s_caps_cache.end()) {
            out_caps = it->second;
            return true;
        }
    }

    nvenc::NvencApi api;
    if (!api.load()) {
        return false;
    }

    const auto& fn = api.functions();
    if (!fn.nvEncOpenEncodeSessionEx || !fn.nvEncGetEncodeGUIDCount || !fn.nvEncGetEncodeGUIDs || !fn.nvEncDestroyEncoder) {
        api.unload();
        return false;
    }

    nvenc::NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS session_params{};
    session_params.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    session_params.deviceType = nvenc::NV_ENC_DEVICE_TYPE_DIRECTX;
    session_params.device = d3d_device;
    session_params.apiVersion = nvenc::NVENCAPI_VERSION;

    void* session = nullptr;
    auto pfn_open_session_ex = reinterpret_cast<nvenc::PNVENCOPENENCODESESSIONEX>(fn.nvEncOpenEncodeSessionEx);
    if (!pfn_open_session_ex || pfn_open_session_ex(&session_params, &session) != nvenc::NV_ENC_SUCCESS || !session) {
        api.unload();
        return false;
    }

    auto pfn_get_guid_count = reinterpret_cast<nvenc::PNVENCGETENCODEGUIDCOUNT>(fn.nvEncGetEncodeGUIDCount);
    auto pfn_get_guids = reinterpret_cast<nvenc::PNVENCGETENCODEGUIDS>(fn.nvEncGetEncodeGUIDs);
    auto pfn_get_profile_guid_count = reinterpret_cast<nvenc::PNVENCGETENCODEPROFILEGUIDCOUNT>(fn.nvEncGetEncodeProfileGUIDCount);
    auto pfn_get_profile_guids = reinterpret_cast<nvenc::PNVENCGETENCODEPROFILEGUIDS>(fn.nvEncGetEncodeProfileGUIDs);
    auto pfn_get_encode_caps = reinterpret_cast<nvenc::PNVENCGETENCODECAPS>(fn.nvEncGetEncodeCaps);
    auto pfn_destroy_encoder = reinterpret_cast<nvenc::PNVENCDESTROYENCODER>(fn.nvEncDestroyEncoder);

    uint32_t guid_count = 0;
    uint32_t supported_codecs_mask = 0;
    bool has_h264 = false;
    bool has_hevc = false;
    bool has_av1 = false;

    if (pfn_get_guid_count && pfn_get_guids && pfn_get_guid_count(session, &guid_count) == nvenc::NV_ENC_SUCCESS && guid_count > 0) {
        std::vector<GUID> guids(guid_count);
        uint32_t retrieved_count = 0;
        if (pfn_get_guids(session, guids.data(), guid_count, &retrieved_count) == nvenc::NV_ENC_SUCCESS) {
            for (uint32_t i = 0; i < retrieved_count; ++i) {
                if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_H264_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::H264));
                    has_h264 = true;
                } else if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::Hevc));
                    has_hevc = true;
                } else if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_AV1_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::Av1));
                    has_av1 = true;
                }
            }
        }
    }

    // Helper lambda to query specific dynamic capability values via nvEncGetEncodeCaps
    auto query_cap_value = [&](GUID codec_guid, nvenc::NV_ENC_CAPS cap) -> int32_t {
        if (!pfn_get_encode_caps) return 0;
        nvenc::NV_ENC_CAPS_PARAM param{};
        param.version = NV_ENC_CAPS_PARAM_VER;
        param.capsToQuery = cap;
        int32_t val = 0;
        if (pfn_get_encode_caps(session, codec_guid, &param, &val) == nvenc::NV_ENC_SUCCESS) {
            return val;
        }
        return 0;
    };

    // Determine 10-bit support and HevcMain10 profile availability
    bool supports_10bit_hevc = false;
    if (has_hevc) {
        int32_t cap_10bit = query_cap_value(nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL, nvenc::NV_ENC_CAPS_SUPPORT_10BIT_ENCODE);
        bool has_main10_profile = false;

        uint32_t profile_count = 0;
        if (pfn_get_profile_guid_count && pfn_get_profile_guids &&
            pfn_get_profile_guid_count(session, nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL, &profile_count) == nvenc::NV_ENC_SUCCESS && profile_count > 0) {
            std::vector<GUID> profile_guids(profile_count);
            uint32_t actual_profiles = 0;
            if (pfn_get_profile_guids(session, nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL, profile_guids.data(), profile_count, &actual_profiles) == nvenc::NV_ENC_SUCCESS) {
                for (uint32_t p = 0; p < actual_profiles; ++p) {
                    if (std::memcmp(&profile_guids[p], &nvenc::NV_ENC_HEVC_PROFILE_MAIN10_GUID_LOCAL, sizeof(GUID)) == 0) {
                        has_main10_profile = true;
                        break;
                    }
                }
            }
        }

        if (cap_10bit > 0 && has_main10_profile) {
            supports_10bit_hevc = true;
            supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::HevcMain10));
        }
    }

    out_caps.supported_codecs_mask = supported_codecs_mask;

    // Query authoritative dynamic hardware limits using the primary codec GUID
    GUID primary_guid = has_hevc ? nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL :
                        (has_h264 ? nvenc::NV_ENC_CODEC_H264_GUID_LOCAL : nvenc::NV_ENC_CODEC_AV1_GUID_LOCAL);

    int32_t max_w = query_cap_value(primary_guid, nvenc::NV_ENC_CAPS_WIDTH_MAX);
    int32_t max_h = query_cap_value(primary_guid, nvenc::NV_ENC_CAPS_HEIGHT_MAX);
    int32_t cap_lossless = query_cap_value(primary_guid, nvenc::NV_ENC_CAPS_SUPPORT_LOSSLESS_ENCODE);
    int32_t cap_intra_refresh = query_cap_value(primary_guid, nvenc::NV_ENC_CAPS_SUPPORT_INTRA_REFRESH);

    out_caps.max_width = (max_w > 0) ? static_cast<uint32_t>(max_w) : 8192;
    out_caps.max_height = (max_h > 0) ? static_cast<uint32_t>(max_h) : 8192;
    out_caps.supports_10bit = supports_10bit_hevc ? 1 : 0;
    out_caps.supports_lossless = (cap_lossless > 0) ? 1 : 0;
    out_caps.supports_smart_idr = (cap_intra_refresh > 0) ? 1 : 1;

    // Discover maximum refresh rate across active attached physical displays
    uint32_t max_fps = 0;
    UINT out_idx = 0;
    ComPtr<IDXGIOutput> output;
    while (adapter->EnumOutputs(out_idx++, &output) != DXGI_ERROR_NOT_FOUND) {
        if (!output) continue;
        DXGI_OUTPUT_DESC out_desc{};
        if (SUCCEEDED(output->GetDesc(&out_desc)) && out_desc.AttachedToDesktop) {
            UINT num_modes = 0;
            if (SUCCEEDED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &num_modes, nullptr)) && num_modes > 0) {
                std::vector<DXGI_MODE_DESC> modes(num_modes);
                if (SUCCEEDED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &num_modes, modes.data()))) {
                    for (const auto& mode : modes) {
                        if (mode.RefreshRate.Denominator > 0) {
                            uint32_t fps = (mode.RefreshRate.Numerator + (mode.RefreshRate.Denominator / 2)) / mode.RefreshRate.Denominator;
                            if (fps > max_fps) {
                                max_fps = fps;
                            }
                        }
                    }
                }
            }
        }
        output.Reset();
    }
    out_caps.max_fps = (max_fps > 0) ? max_fps : 240;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 200000;

    if (pfn_destroy_encoder) {
        pfn_destroy_encoder(session);
    }
    api.unload();

    {
        std::lock_guard<std::mutex> lock(s_caps_mutex);
        s_caps_cache[luid] = out_caps;
    }
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool NvencVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    static std::atomic<int> s_cached_mask{-1};
    int cached_mask = s_cached_mask.load(std::memory_order_acquire);
    if (cached_mask != -1) {
        uint32_t codec_idx = static_cast<uint32_t>(codec);
        return (cached_mask & (1 << codec_idx)) != 0;
    }

    void* dev = moonshine_d3d11_create_device(0x10DE);
    if (!dev) {
        s_cached_mask.store(0, std::memory_order_release);
        return false;
    }

    EncoderCaps caps{};
    bool ok = query_capabilities(dev, caps);
    moonshine_d3d11_destroy_device(dev);

    int mask = ok ? static_cast<int>(caps.supported_codecs_mask) : 0;
    s_cached_mask.store(mask, std::memory_order_release);

    uint32_t codec_idx = static_cast<uint32_t>(codec);
    return (mask & (1 << codec_idx)) != 0;
#else
    (void)codec;
    return false;
#endif
}

} // namespace moonshine::encoder
