#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "encoder/nvenc/nvenc_types.hpp"
#include "encoder/nvenc/nvenc_api.hpp"
#include "encoder/nvenc/nvenc_bitstream_pool.hpp"
#include <deque>
#include <mutex>

namespace moonshine::encoder::nvenc {

struct NvencInFlightFrame {
    uint64_t frame_id{0};
    uint64_t timestamp_us{0};
    void* surface{nullptr};
    void* registered_resource{nullptr};
    void* bitstream_buffer{nullptr};
    bool submitted{false};
    bool completed{false};
    bool keyframe{false};
};

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
        uint64_t frame_id,
        uint64_t timestamp_us,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    );

    bool encode(
        void* registered_resource,
        bool force_idr,
        uint32_t frame_idx,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    );

    bool submit_frame(
        void* registered_resource,
        bool force_idr,
        uint64_t frame_id,
        uint64_t timestamp_us,
        void* surface = nullptr
    );

    bool poll_packet(
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        EncodedPacketDesc& out_desc,
        uint32_t& out_written_size
    );

    bool reconfigure(const EncoderConfig& new_config);
    bool drain();
    void close();

    [[nodiscard]] bool is_open() const noexcept;
    [[nodiscard]] bool is_configured() const noexcept;
    [[nodiscard]] void* session_handle() const noexcept;
    [[nodiscard]] void* bitstream_buffer() const noexcept;
    [[nodiscard]] const EncoderConfig& config() const noexcept;
    [[nodiscard]] NvencBitstreamPool& bitstream_pool() noexcept;

    void set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) noexcept;
    void set_intra_refresh(bool enabled, uint32_t period, uint32_t count) noexcept;

private:
    NvencApi* _api{nullptr};
    void* _d3d_device{nullptr};
    void* _session{nullptr};
    EncoderConfig _config{};
    NvencPreset _preset{NvencPreset::P1_UltraFast};
    NvencTuning _tuning{NvencTuning::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_period{0};
    uint32_t _intra_refresh_count{0};
    bool _is_configured{false};

    NvencBitstreamPool _bitstream_pool;
    mutable std::mutex _in_flight_mutex;
    std::deque<NvencInFlightFrame> _in_flight_frames;
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencSession;
using nvenc::NvencInFlightFrame;
} // namespace moonshine::encoder
