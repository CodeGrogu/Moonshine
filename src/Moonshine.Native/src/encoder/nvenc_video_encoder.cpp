#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "encoder/nvenc/nvenc_api.hpp"
#include "encoder/nvenc/nvenc_session.hpp"
#include "encoder/nvenc/nvenc_surface_pool.hpp"
#include "encoder/nvenc/nvenc_types.hpp"
#include <cstring>
#include <memory>
#include <vector>

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
        !_initialized || !_impl || !_impl->session.is_open() || !out_bitstream || max_buffer_size == 0) {
        return false;
    }

    if (!d3d_texture && _d3d_device) {
        if (!_impl->internal_texture) {
            auto* dev_tex = static_cast<ID3D11Device*>(_d3d_device);
            D3D11_TEXTURE2D_DESC tex_init_desc{};
            tex_init_desc.Width = _config.width;
            tex_init_desc.Height = _config.height;
            tex_init_desc.MipLevels = 1;
            tex_init_desc.ArraySize = 1;
            tex_init_desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            tex_init_desc.SampleDesc.Count = 1;
            tex_init_desc.SampleDesc.Quality = 0;
            tex_init_desc.Usage = D3D11_USAGE_DEFAULT;
            tex_init_desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            dev_tex->CreateTexture2D(&tex_init_desc, nullptr, &_impl->internal_texture);
        }
        d3d_texture = _impl->internal_texture.Get();
    }

    if (!d3d_texture) {
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
        buffer_format = (static_cast<VideoCodec>(_config.codec) == VideoCodec::HevcMain10)
            ? nvenc::NV_ENC_BUFFER_FORMAT_ABGR10
            : nvenc::NV_ENC_BUFFER_FORMAT_ABGR;
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

    bool is_key = force_idr || _force_keyframe.exchange(false) || (_frame_counter == 0);

    _state = NvencLifecycleState::Encoding;

    bool success = _impl->session.encode(
        registered_resource,
        is_key,
        static_cast<uint32_t>(_frame_counter),
        out_desc,
        out_bitstream,
        max_buffer_size,
        out_written_size
    );

    if (success) {
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
    auto pfn_destroy_encoder = reinterpret_cast<nvenc::PNVENCDESTROYENCODER>(fn.nvEncDestroyEncoder);

    uint32_t guid_count = 0;
    uint32_t supported_codecs_mask = 0;
    bool has_hevc = false;

    if (pfn_get_guid_count && pfn_get_guids && pfn_get_guid_count(session, &guid_count) == nvenc::NV_ENC_SUCCESS && guid_count > 0) {
        std::vector<GUID> guids(guid_count);
        uint32_t retrieved_count = 0;
        if (pfn_get_guids(session, guids.data(), guid_count, &retrieved_count) == nvenc::NV_ENC_SUCCESS) {
            for (uint32_t i = 0; i < retrieved_count; ++i) {
                if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_H264_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::H264));
                } else if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_HEVC_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::Hevc));
                    has_hevc = true;
                } else if (std::memcmp(&guids[i], &nvenc::NV_ENC_CODEC_AV1_GUID_LOCAL, sizeof(GUID)) == 0) {
                    supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::Av1));
                }
            }
        }
    }

    if (has_hevc) {
        supported_codecs_mask |= (1 << static_cast<uint32_t>(VideoCodec::HevcMain10));
    }

    if (pfn_destroy_encoder) {
        pfn_destroy_encoder(session);
    }

    out_caps.supported_codecs_mask = supported_codecs_mask;
    out_caps.max_width = 8192;
    out_caps.max_height = 8192;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = has_hevc ? 1 : 0;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 200000;

    api.unload();
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool NvencVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    nvenc::NvencApi api;
    if (!api.load()) {
        return false;
    }
    api.unload();

    return codec == VideoCodec::H264 || codec == VideoCodec::Hevc ||
           codec == VideoCodec::HevcMain10 || codec == VideoCodec::Av1;
#else
    (void)codec;
    return false;
#endif
}

} // namespace moonshine::encoder
