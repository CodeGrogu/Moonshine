#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>

namespace moonshine::encoder {

enum class AmfQualityPreset : uint32_t {
    Speed = 1,
    Balanced = 2,
    Quality = 3
};

enum class AmfUsage : uint32_t {
    Transcoding = 0,
    UltraLowLatency = 1,
    LowLatency = 2,
    Webcam = 3
};

class AmfVideoEncoder final : public IVideoEncoder {
public:
    AmfVideoEncoder();
    ~AmfVideoEncoder() override;

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

    [[nodiscard]] EncoderVendor vendor() const noexcept override { return EncoderVendor::AmdAmf; }
    [[nodiscard]] bool is_initialized() const noexcept override { return _initialized; }

    bool set_preset_and_usage(AmfQualityPreset preset, AmfUsage usage);
    bool set_intra_refresh(bool enabled, uint32_t num_mbs_per_slot);

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);
    static bool query_codec_support(VideoCodec codec);

private:
    bool _initialized{false};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    AmfQualityPreset _preset{AmfQualityPreset::Speed};
    AmfUsage _usage{AmfUsage::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_num_mbs_per_slot{0};
    std::atomic<bool> _force_keyframe{true};
    uint64_t _frame_counter{0};
    std::vector<uint8_t> _header_cache;
};

} // namespace moonshine::encoder
