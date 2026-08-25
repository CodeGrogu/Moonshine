#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include "moonshine/encoder/qsv_video_encoder.hpp"
#include "encoder/qsv/qsv_types.hpp"
#include "encoder/qsv/qsv_api.hpp"
#include <mutex>
#include <vector>
#include <queue>

namespace moonshine::encoder::qsv {

/**
 * Output packet descriptor and buffer for queued packets produced during drain or multi-frame pipelining.
 */
struct QsvPendingPacket {
    std::vector<uint8_t> data;
    EncodedPacketDesc desc{};
};

/**
 * TrackedSurface Pool Architecture:
 * - Current Moonshine oneVPL architecture operates synchronously with AsyncDepth=1.
 * - Each submission takes an available surface slot from the pool, binds the Direct3D 11 texture
 *   using mfxHDLPair, submits via MFXVideoENCODE_EncodeFrameAsync, and synchronises immediately
 *   via MFXVideoCORE_SyncOperation before releasing the surface back to the pool.
 * - This fail-closed, synchronous design guarantees deterministic ordering and zero race conditions
 *   on streaming pipelines.
 * - Future evolution can expand to deeper asynchronous pipelining (AsyncDepth > 1) with deferred
 *   surface release upon sync point completion, whilst preserving the exact same C-ABI boundary.
 */
struct TrackedSurface {
    mfxFrameSurface1 surface{};
    mfxHDLPair hdl_pair{};
    void* d3d_texture{nullptr};
    bool in_use{false};
    uint64_t frame_id{0};
};

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

    EncodeResult encode(
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
    [[nodiscard]] mfxStatus last_status() const noexcept;
    [[nodiscard]] mfxStatus impl_filter_status() const noexcept;
    [[nodiscard]] mfxStatus accel_filter_status() const noexcept;
    [[nodiscard]] mfxSession session() const noexcept;
    [[nodiscard]] size_t pending_output_count() const noexcept;

    void set_target_usage(QsvTargetUsage usage, bool low_power_vdenc) noexcept;
    void set_intra_refresh(bool enabled, uint32_t cycle_size, int32_t qp_delta) noexcept;

private:
    QsvApi* _api{nullptr};
    void* _d3d_device{nullptr};
    mfxLoader _loader{nullptr};
    mfxSession _session{nullptr};
    mfxStatus _last_status{MFX_ERR_NONE};
    mfxStatus _status_impl_filter{MFX_ERR_NOT_INITIALIZED};
    mfxStatus _status_accel_filter{MFX_ERR_NOT_INITIALIZED};
    mfxVideoParam _params{};
    mfxExtCodingOption _ext_opt{};
    mfxExtCodingOption2 _ext_opt2{};
    mfxExtBuffer* _ext_buffers[2]{nullptr};
    std::vector<uint8_t> _bitstream_buffer;
    std::vector<TrackedSurface> _surface_pool;
    size_t _surface_index{0};
    std::queue<QsvPendingPacket> _output_queue;
    EncoderConfig _config{};
    QsvTargetUsage _usage{QsvTargetUsage::BestSpeed};
    bool _low_power_vdenc{true};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_cycle_size{0};
    int32_t _intra_refresh_qp_delta{0};
    bool _is_configured{false};
    mutable std::mutex _mutex;
};

/**
 * Explicit Legacy Intel Media SDK (MSDK) session compatibility helper.
 * Strictly isolated for legacy hardware backends when explicitly requested.
 * Does not silently emulate in software (no MFX_IMPL_AUTO_ANY).
 */
class LegacyMfxSession {
public:
    LegacyMfxSession();
    ~LegacyMfxSession();

    LegacyMfxSession(const LegacyMfxSession&) = delete;
    LegacyMfxSession& operator=(const LegacyMfxSession&) = delete;

    LegacyMfxSession(LegacyMfxSession&& other) noexcept;
    LegacyMfxSession& operator=(LegacyMfxSession&& other) noexcept;

    bool open(QsvApi& api, void* d3d_device);
    void close();

    [[nodiscard]] bool is_open() const noexcept;
    [[nodiscard]] mfxSession session() const noexcept;
    [[nodiscard]] mfxStatus last_status() const noexcept;

private:
    QsvApi* _api{nullptr};
    void* _d3d_device{nullptr};
    mfxSession _session{nullptr};
    mfxStatus _last_status{MFX_ERR_NONE};
    mutable std::mutex _mutex;
};

} // namespace moonshine::encoder::qsv
