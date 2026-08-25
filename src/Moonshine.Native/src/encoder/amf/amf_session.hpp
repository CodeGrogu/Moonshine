#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include "moonshine/encoder/amf_video_encoder.hpp"
#include "encoder/amf/amf_types.hpp"
#include "encoder/amf/amf_api.hpp"
#include <mutex>
#include <vector>
#include <queue>

namespace moonshine::encoder::amf {

class AmfSession {
public:
    AmfSession();
    ~AmfSession();

    AmfSession(const AmfSession&) = delete;
    AmfSession& operator=(const AmfSession&) = delete;

    AmfSession(AmfSession&& other) noexcept;
    AmfSession& operator=(AmfSession&& other) noexcept;

    bool open(AmfApi& api, void* d3d_device);
    bool configure(const EncoderConfig& config);

    bool encode(
        void* d3d_texture,
        bool force_idr,
        uint64_t frame_id,
        uint64_t timestamp_us,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    );

    bool reconfigure(const EncoderConfig& new_config);
    bool drain();
    bool flush();
    void close();

    [[nodiscard]] bool is_open() const noexcept;
    [[nodiscard]] bool is_configured() const noexcept;
    [[nodiscard]] const EncoderConfig& config() const noexcept;

    void set_preset_and_usage(AmfQualityPreset preset, AmfUsage usage) noexcept;
    void set_intra_refresh(bool enabled, uint32_t num_mbs_per_slot) noexcept;

private:
    AmfApi* _api{nullptr};
    void* _d3d_device{nullptr};
    AMFContext* _context{nullptr};
    AMFComponent* _encoder{nullptr};
    EncoderConfig _config{};
    AmfQualityPreset _preset{AmfQualityPreset::Speed};
    AmfUsage _usage{AmfUsage::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_num_mbs_per_slot{0};
    bool _is_configured{false};
    std::queue<AMFData*> _output_queue;
    mutable std::mutex _mutex;
};

} // namespace moonshine::encoder::amf
