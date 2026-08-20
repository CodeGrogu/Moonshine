#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>

namespace moonshine::encoder {

class NvencVideoEncoder final : public IVideoEncoder {
public:
    NvencVideoEncoder();
    ~NvencVideoEncoder() override;

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

    [[nodiscard]] EncoderVendor vendor() const noexcept override { return EncoderVendor::NvidiaNvenc; }
    [[nodiscard]] bool is_initialized() const noexcept override { return _initialized; }

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);

private:
    bool _initialized{false};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    std::atomic<bool> _force_keyframe{true};
    uint64_t _frame_counter{0};
    std::vector<uint8_t> _header_cache;
};

} // namespace moonshine::encoder
