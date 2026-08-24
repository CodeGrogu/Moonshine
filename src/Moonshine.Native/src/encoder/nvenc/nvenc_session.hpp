#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "encoder/nvenc/nvenc_types.hpp"
#include "encoder/nvenc/nvenc_api.hpp"

namespace moonshine::encoder::nvenc {

class NvencSession {
public:
    NvencSession();
    ~NvencSession();

    NvencSession(const NvencSession&) = delete;
    NvencSession& operator=(const NvencSession&) = delete;

    NvencSession(NvencSession&& other) noexcept;
    NvencSession& operator=(NvencSession&& other) noexcept;

    bool open(NvencApi& api, void* d3d_device);
    bool configure(const EncoderConfig& config);
    bool encode(
        void* registered_resource,
        bool force_idr,
        uint32_t frame_idx,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    );
    bool reconfigure(const EncoderConfig& new_config);
    void close();

    [[nodiscard]] bool is_open() const noexcept;
    [[nodiscard]] bool is_configured() const noexcept;
    [[nodiscard]] void* session_handle() const noexcept;
    [[nodiscard]] void* bitstream_buffer() const noexcept;
    [[nodiscard]] const EncoderConfig& config() const noexcept;

    void set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) noexcept;
    void set_intra_refresh(bool enabled, uint32_t period, uint32_t count) noexcept;

private:
    NvencApi* _api{nullptr};
    void* _d3d_device{nullptr};
    void* _session{nullptr};
    void* _bitstream_buffer{nullptr};
    EncoderConfig _config{};
    NvencPreset _preset{NvencPreset::P1_UltraFast};
    NvencTuning _tuning{NvencTuning::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_period{0};
    uint32_t _intra_refresh_count{0};
    bool _is_configured{false};
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencSession;
} // namespace moonshine::encoder
