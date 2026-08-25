#pragma once

#include <cstdint>
#include <cstddef>

namespace moonshine::encoder {

enum class EncoderVendor : uint32_t {
    Auto = 0,
    NvidiaNvenc = 1,
    AmdAmf = 2,
    IntelQuickSync = 3,
    Direct3D11Hardware = 4
};

enum class VideoCodec : uint32_t {
    H264 = 0,
    Hevc = 1,
    HevcMain10 = 2,
    Av1 = 3
};

enum class RateControlMode : uint32_t {
    ConstantBitrate = 0,
    VariableBitrate = 1,
    ConstrainedQuality = 2
};

#pragma pack(push, 1)
struct EncoderCaps {
    uint32_t supported_codecs_mask; // Bit 0: H264, Bit 1: HEVC, Bit 2: HEVC Main10, Bit 3: AV1
    uint32_t max_width;             // e.g. 4096 / 8192
    uint32_t max_height;            // e.g. 4096 / 8192
    uint32_t max_fps;               // e.g. 240
    uint8_t  supports_10bit;        // 1 if 10-bit HDR encoding supported
    uint8_t  supports_lossless;     // 1 if lossless encoding supported
    uint8_t  supports_smart_idr;    // 1 if dynamic IDR injection without full reset supported
    uint8_t  vendor_id;             // 1: NVENC, 2: AMF, 3: QSV, 4: D3D11
    uint32_t min_bitrate_kbps;      // Minimum bitrate (e.g. 500 kbps)
    uint32_t max_bitrate_kbps;      // Maximum bitrate (e.g. 200000 kbps)
    uint32_t reserved;              // Padding for 32-byte alignment
};

struct EncoderConfig {
    uint32_t width;                 // Frame width in pixels
    uint32_t height;                // Frame height in pixels
    uint32_t fps;                   // Target framerate
    uint32_t bitrate_kbps;          // Target bitrate in kbps
    uint32_t peak_bitrate_kbps;     // Peak bitrate for VBR / bursts
    uint32_t codec;                 // VideoCodec
    uint32_t rc_mode;               // RateControlMode
    uint16_t gop_length;            // GOP size (e.g. infinite or 0)
    uint8_t  enable_intra_refresh;  // 1 to enable progressive intra-refresh
    uint8_t  enable_filler_data;    // 1 to emit filler for strict CBR
};

struct EncodedPacketDesc {
    uint64_t frame_index;           // Monotonically increasing frame index
    int64_t  timestamp_qpc;         // High-precision QPC timestamp
    uint32_t payload_size;          // Total size of encoded NAL / OBU bytes
    uint8_t  is_keyframe;           // 1 if IDR / SPS / PPS keyframe
    uint8_t  is_header_packet;      // 1 if packet contains VPS/SPS/PPS parameter sets
    uint8_t  temporal_id;           // Temporal layer ID
    uint8_t  reserved;              // Padding for strict 24-byte alignment
};
#pragma pack(pop)

class IVideoEncoder {
public:
    virtual ~IVideoEncoder() = default;

    virtual bool initialize(void* d3d_device, const EncoderConfig& config) = 0;
    virtual bool encode_frame(
        void* d3d_texture,
        bool force_idr,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    ) = 0;
    virtual bool reconfigure(const EncoderConfig& new_config) = 0;
    virtual void request_keyframe() = 0;
    
    /// Stop accepting new frames and wait until all previously submitted frames
    /// have produced their encoded output. Returns true when all pending output
    /// has been collected. Used for: session shutdown, pre-reconfiguration flush.
    virtual bool drain() = 0;

    /// Discard or reset pending encoder state. Establish a clean random-access
    /// boundary so the next submitted frame will be an IDR/CRA/key frame.
    /// Returns true when encoder is ready to accept new input.
    /// Used for: error recovery, stream discontinuity.
    virtual bool flush() = 0;
    
    virtual void cleanup() = 0;
    [[nodiscard]] virtual EncoderVendor vendor() const noexcept = 0;
    [[nodiscard]] virtual bool is_initialized() const noexcept = 0;
    [[nodiscard]] virtual uint32_t get_state() const noexcept { return is_initialized() ? 5 : 0; }
    [[nodiscard]] virtual bool is_healthy() const noexcept { return is_initialized(); }
};

} // namespace moonshine::encoder
