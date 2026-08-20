#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>

namespace moonshine::encoder {

enum class NvencPreset : uint32_t {
    P1_UltraFast = 1,
    P2_Fast = 2,
    P3_Medium = 3,
    P4_Default = 4,
    P5_Slow = 5,
    P6_Slower = 6,
    P7_Slowest = 7
};

enum class NvencTuning : uint32_t {
    HighQuality = 0,
    LowLatency = 1,
    UltraLowLatency = 2,
    Lossless = 3
};

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

    bool set_preset_and_tuning(NvencPreset preset, NvencTuning tuning);
    bool set_intra_refresh(bool enabled, uint32_t period, uint32_t count);

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);
    static bool query_codec_support(VideoCodec codec);

private:
    bool _initialized{false};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    NvencPreset _preset{NvencPreset::P1_UltraFast};
    NvencTuning _tuning{NvencTuning::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_period{0};
    uint32_t _intra_refresh_count{0};
    std::atomic<bool> _force_keyframe{true};
    uint64_t _frame_counter{0};
    std::vector<uint8_t> _header_cache;
};

} // namespace moonshine::encoder
