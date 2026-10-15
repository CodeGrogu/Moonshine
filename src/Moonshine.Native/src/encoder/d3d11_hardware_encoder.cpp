#include "moonshine/encoder/d3d11_hardware_encoder.hpp"
#include <cstring>

namespace moonshine::encoder {

D3D11HardwareEncoder::D3D11HardwareEncoder() = default;

D3D11HardwareEncoder::~D3D11HardwareEncoder() {
    cleanup();
}

bool D3D11HardwareEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    (void)d3d_device;
    (void)config;
    cleanup();
    // Fail-closed: Generic Direct3D 11 Media Foundation Transform (IMFTransform) hardware encoding
    // is not wired to an active MFT pipeline in this build. Vendor-native hardware encoders
    // (NVENC, AMD AMF, Intel oneVPL QuickSync) must be used instead of synthetic bitstreams.
    return false;
}

bool D3D11HardwareEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
    (void)d3d_texture;
    (void)force_idr;
    (void)out_bitstream;
    (void)max_buffer_size;
    out_desc = {};
    out_written_size = 0;
    return false;
}

bool D3D11HardwareEncoder::reconfigure(const EncoderConfig& new_config) {
    (void)new_config;
    return false;
}

void D3D11HardwareEncoder::request_keyframe() {
    _force_keyframe.store(true);
}

bool D3D11HardwareEncoder::drain() {
    return false;
}

bool D3D11HardwareEncoder::flush() {
    return false;
}

void D3D11HardwareEncoder::cleanup() {
    _initialized = false;
    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
    _header_cache.clear();
}

bool D3D11HardwareEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    (void)d3d_device;
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::Direct3D11Hardware);
    // Fail-closed: Do not advertise capabilities for an unimplemented MFT transform backend.
    return false;
}

} // namespace moonshine::encoder
