#include "moonshine/encoder/unified_video_encoder.hpp"
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "moonshine/encoder/amf_video_encoder.hpp"
#include "moonshine/encoder/qsv_video_encoder.hpp"
#include "moonshine/encoder/d3d11_hardware_encoder.hpp"

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

namespace moonshine::encoder {

UnifiedVideoEncoder::UnifiedVideoEncoder(EncoderVendor preferred_vendor)
    : _preferred_vendor(preferred_vendor) {
}

UnifiedVideoEncoder::~UnifiedVideoEncoder() {
    cleanup();
}

std::unique_ptr<IVideoEncoder> UnifiedVideoEncoder::create_encoder(EncoderVendor vendor) {
    switch (vendor) {
        case EncoderVendor::NvidiaNvenc:
            return std::make_unique<NvencVideoEncoder>();
        case EncoderVendor::AmdAmf:
            return std::make_unique<AmfVideoEncoder>();
        case EncoderVendor::IntelQuickSync:
            return std::make_unique<QsvVideoEncoder>();
        case EncoderVendor::Direct3D11Hardware:
        default:
            return std::make_unique<D3D11HardwareEncoder>();
    }
}

bool UnifiedVideoEncoder::query_capabilities(EncoderVendor vendor, void* d3d_device, EncoderCaps& out_caps) {
    if (vendor == EncoderVendor::NvidiaNvenc) {
        return NvencVideoEncoder::query_capabilities(d3d_device, out_caps);
    }
    if (vendor == EncoderVendor::AmdAmf) {
        return AmfVideoEncoder::query_capabilities(d3d_device, out_caps);
    }
    if (vendor == EncoderVendor::IntelQuickSync) {
        return QsvVideoEncoder::query_capabilities(d3d_device, out_caps);
    }
    if (vendor == EncoderVendor::Direct3D11Hardware) {
        return D3D11HardwareEncoder::query_capabilities(d3d_device, out_caps);
    }

    // Auto detection
#if defined(_WIN32)
    if (d3d_device) {
        auto* dev = static_cast<ID3D11Device*>(d3d_device);
        ComPtr<IDXGIDevice> dxgi_dev;
        if (SUCCEEDED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) {
            ComPtr<IDXGIAdapter> adapter;
            if (SUCCEEDED(dxgi_dev->GetAdapter(&adapter))) {
                DXGI_ADAPTER_DESC desc;
                if (SUCCEEDED(adapter->GetDesc(&desc))) {
                    if (desc.VendorId == 0x10DE) { // NVIDIA
                        return NvencVideoEncoder::query_capabilities(d3d_device, out_caps);
                    }
                    if (desc.VendorId == 0x1002) { // AMD
                        return AmfVideoEncoder::query_capabilities(d3d_device, out_caps);
                    }
                    if (desc.VendorId == 0x8086) { // Intel
                        return QsvVideoEncoder::query_capabilities(d3d_device, out_caps);
                    }
                    return D3D11HardwareEncoder::query_capabilities(d3d_device, out_caps);
                }
            }
        }
    }
#endif

    return false;
}

bool UnifiedVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

    if (_preferred_vendor != EncoderVendor::Auto) {
        _active_encoder = create_encoder(_preferred_vendor);
        if (_active_encoder && _active_encoder->initialize(d3d_device, config)) {
            return true;
        }
        _active_encoder.reset();
        return false;
    }

    // Auto detection fallback chain: NVENC -> AMF -> QSV -> D3D11
    const EncoderVendor fallback_order[] = {
        EncoderVendor::NvidiaNvenc,
        EncoderVendor::AmdAmf,
        EncoderVendor::IntelQuickSync,
        EncoderVendor::Direct3D11Hardware
    };

    for (auto vendor : fallback_order) {
        auto candidate = create_encoder(vendor);
        if (candidate && candidate->initialize(d3d_device, config)) {
            _active_encoder = std::move(candidate);
            return true;
        }
    }

    return false;
}

bool UnifiedVideoEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    if (!_active_encoder) return false;
    return _active_encoder->encode_frame(d3d_texture, force_idr, out_desc, out_bitstream, max_buffer_size, out_written_size);
}

bool UnifiedVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_active_encoder) return false;
    return _active_encoder->reconfigure(new_config);
}

void UnifiedVideoEncoder::request_keyframe() {
    if (_active_encoder) {
        _active_encoder->request_keyframe();
    }
}

bool UnifiedVideoEncoder::drain() {
    if (_active_encoder) {
        return _active_encoder->drain();
    }
    return false;
}

bool UnifiedVideoEncoder::flush() {
    if (_active_encoder) {
        return _active_encoder->flush();
    }
    return false;
}

void UnifiedVideoEncoder::cleanup() {
    if (_active_encoder) {
        _active_encoder->cleanup();
        _active_encoder.reset();
    }
}

EncoderVendor UnifiedVideoEncoder::vendor() const noexcept {
    return _active_encoder ? _active_encoder->vendor() : _preferred_vendor;
}

bool UnifiedVideoEncoder::is_initialized() const noexcept {
    return _active_encoder && _active_encoder->is_initialized();
}

uint32_t UnifiedVideoEncoder::get_state() const noexcept {
    return _active_encoder ? _active_encoder->get_state() : 0;
}

bool UnifiedVideoEncoder::is_healthy() const noexcept {
    return _active_encoder && _active_encoder->is_healthy();
}

} // namespace moonshine::encoder
