#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <memory>

namespace moonshine::encoder {

class UnifiedVideoEncoder final : public IVideoEncoder {
public:
    explicit UnifiedVideoEncoder(EncoderVendor preferred_vendor = EncoderVendor::Auto);
    ~UnifiedVideoEncoder() override;

    bool initialize(void* d3d_device, const EncoderConfig& config) override;
    bool encode_frame(
        void* d3d_texture,
        bool force_idr,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    ) override;
    bool reconfigure(const EncoderConfig& new_config) override;
    void request_keyframe() override;
    void cleanup() override;

    [[nodiscard]] EncoderVendor vendor() const noexcept override;
    [[nodiscard]] bool is_initialized() const noexcept override;
    [[nodiscard]] uint32_t get_state() const noexcept override;
    [[nodiscard]] bool is_healthy() const noexcept override;

    static bool query_capabilities(EncoderVendor vendor, void* d3d_device, EncoderCaps& out_caps);
    static std::unique_ptr<IVideoEncoder> create_encoder(EncoderVendor vendor);

private:
    EncoderVendor _preferred_vendor{EncoderVendor::Auto};
    std::unique_ptr<IVideoEncoder> _active_encoder;
};

} // namespace moonshine::encoder
