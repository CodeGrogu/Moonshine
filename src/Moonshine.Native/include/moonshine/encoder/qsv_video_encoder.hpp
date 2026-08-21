#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>

namespace moonshine::encoder {

enum class QsvTargetUsage : uint32_t {
    BestSpeed = 1,
    Balanced = 4,
    BestQuality = 7
};

class QsvVideoEncoder final : public IVideoEncoder {
public:
    QsvVideoEncoder();
    ~QsvVideoEncoder() override;

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

    [[nodiscard]] EncoderVendor vendor() const noexcept override { return EncoderVendor::IntelQuickSync; }
    [[nodiscard]] bool is_initialized() const noexcept override { return _initialized; }

    bool set_target_usage(QsvTargetUsage usage, bool low_power_vdenc);
    bool set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta);

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);
    static bool query_codec_support(VideoCodec codec);

private:
    bool _initialized{false};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    QsvTargetUsage _usage{QsvTargetUsage::BestSpeed};
    bool _low_power_vdenc{true};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_cycle_size{0};
    int32_t _intra_refresh_qp_delta{0};
    std::atomic<bool> _force_keyframe{true};
    uint64_t _frame_counter{0};
    std::vector<uint8_t> _header_cache;
};

} // namespace moonshine::encoder
