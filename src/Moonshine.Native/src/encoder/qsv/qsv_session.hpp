#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include "moonshine/encoder/qsv_video_encoder.hpp"
#include "encoder/qsv/qsv_types.hpp"
#include "encoder/qsv/qsv_api.hpp"
#include <mutex>
#include <vector>

namespace moonshine::encoder::qsv {

class QsvSession {
public:
    QsvSession();
    ~QsvSession();

    QsvSession(const QsvSession&) = delete;
    QsvSession& operator=(const QsvSession&) = delete;

    QsvSession(QsvSession&& other) noexcept;
    QsvSession& operator=(QsvSession&& other) noexcept;

    bool open(QsvApi& api, void* d3d_device);
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
    void close();

    [[nodiscard]] bool is_open() const noexcept;
    [[nodiscard]] bool is_configured() const noexcept;
    [[nodiscard]] const EncoderConfig& config() const noexcept;

    void set_target_usage(QsvTargetUsage usage, bool low_power_vdenc) noexcept;
    void set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta) noexcept;

private:
    QsvApi* _api{nullptr};
    void* _d3d_device{nullptr};
    mfxSession _session{nullptr};
    mfxVideoParam _params{};
    mfxExtCodingOption _ext_opt{};
    mfxExtCodingOption2 _ext_opt2{};
    mfxExtBuffer* _ext_buffers[2]{nullptr};
    std::vector<uint8_t> _bitstream_buffer;
    EncoderConfig _config{};
    QsvTargetUsage _usage{QsvTargetUsage::BestSpeed};
    bool _low_power_vdenc{true};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_cycle_size{0};
    int32_t _intra_refresh_qp_delta{0};
    bool _is_configured{false};
    mutable std::mutex _mutex;
};

} // namespace moonshine::encoder::qsv
