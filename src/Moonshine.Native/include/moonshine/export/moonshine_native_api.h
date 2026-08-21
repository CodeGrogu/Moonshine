#ifndef MOONSHINE_NATIVE_API_H
#define MOONSHINE_NATIVE_API_H

#include <stdint.h>
#include <stddef.h>

#if defined(_WIN32) || defined(__CYGWIN__)
    #ifdef MOONSHINE_NATIVE_EXPORTS
        #define MOONSHINE_API __declspec(dllexport)
    #else
        #define MOONSHINE_API __declspec(dllimport)
    #endif
    #define MOONSHINE_CONV __cdecl
#else
    #ifdef MOONSHINE_NATIVE_EXPORTS
        #define MOONSHINE_API __attribute__((visibility("default")))
    #else
        #define MOONSHINE_API
    #endif
    #define MOONSHINE_CONV
#endif

#ifdef __cplusplus
extern "C" {
#endif

#pragma pack(push, 1)

/**
 * @brief Blittable descriptor representing a raw video packet for FEC and reassembly.
 */
typedef struct MoonshinePacketDesc {
    uint32_t sequence_number;
    uint32_t frame_index;
    uint16_t packet_index;
    uint16_t total_packets;
    uint16_t payload_size;
    uint8_t  packet_type;    // 0: Data, 1: Parity (FEC)
    uint8_t  flags;          // Bit 0: Frame Start, Bit 1: Frame End, Bit 2: Keyframe
    const uint8_t* payload_ptr;
} MoonshinePacketDesc;

/**
 * @brief Blittable descriptor for a completed reconstructed video frame.
 */
typedef struct MoonshineFrameDesc {
    uint32_t frame_index;
    uint32_t total_bytes;
    uint32_t packet_count;
    uint8_t  is_keyframe;
    uint8_t  reserved[3];
    uint8_t* frame_buffer;
} MoonshineFrameDesc;

/**
 * @brief Hardware Video Decoder Capabilities.
 */
typedef struct MoonshineDecoderCaps {
    uint32_t max_width;
    uint32_t max_height;
    uint32_t max_fps;
    uint8_t  supports_av1;
    uint8_t  supports_hevc;
    uint8_t  supports_h264;
    uint8_t  supports_hdr10;
    uint8_t  supports_10bit;
    uint8_t  supports_d3d12;
    uint8_t  supports_vulkan;
    uint8_t  reserved[1];
} MoonshineDecoderCaps;

/**
 * @brief Blittable descriptor representing a captured desktop frame.
 */
typedef struct MoonshineCaptureFrameDesc {
    void*    texture_handle;
    uint32_t width;
    uint32_t height;
    uint32_t format;
    uint64_t timestamp_qpc;
    uint32_t accumulated_frames;
    uint8_t  cursor_visible;
    uint8_t  reserved[3];
} MoonshineCaptureFrameDesc;

#pragma pack(pop)

// ============================================================================
// SIMD Reed-Solomon Forward Error Correction (FEC) APIs
// ============================================================================

/**
 * @brief Performs vectorized AVX2/AVX-512 Galois Field GF(2^8) XOR parity recovery.
 * @param shards Array of pointers to shard buffers (data shards followed by parity shards).
 * @param shard_count Total number of shards (data + parity).
 * @param shard_size Size in bytes of each shard.
 * @param erased_indices Indices of shards that were lost and need recovery.
 * @param erased_count Number of lost shards.
 * @return 0 on success, non-zero on unrecoverable error.
 */
MOONSHINE_API int MOONSHINE_CONV moonshine_fec_recover_simd(
    uint8_t** shards,
    int shard_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
);

/**
 * @brief Fast SIMD 256-bit/512-bit Vectorized XOR for packet accumulation.
 */
MOONSHINE_API void MOONSHINE_CONV moonshine_vector_xor(
    uint8_t* dest,
    const uint8_t* src,
    size_t length
);

/**
 * @brief Queries the runtime SIMD instruction set architecture utilized for Galois Field FEC.
 * @return 0: Scalar, 1: AVX2, 2: AVX-512, 3: GFNI + AVX-512.
 */
MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_fec_get_simd_architecture(void);

// ============================================================================
// Lock-Free SPSC Queue Management APIs
// ============================================================================

typedef void* MoonshineRingBufferHandle;

MOONSHINE_API MoonshineRingBufferHandle MOONSHINE_CONV moonshine_spsc_create(size_t capacity);
MOONSHINE_API void MOONSHINE_CONV moonshine_spsc_destroy(MoonshineRingBufferHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_enqueue(MoonshineRingBufferHandle handle, const MoonshinePacketDesc* packet);
MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_dequeue(MoonshineRingBufferHandle handle, MoonshinePacketDesc* packet);
MOONSHINE_API size_t MOONSHINE_CONV moonshine_spsc_size(MoonshineRingBufferHandle handle);

// ============================================================================
// Sub-Millisecond Jitter Buffer & Frame Reassembler APIs
// ============================================================================

typedef void* MoonshineJitterBufferHandle;

MOONSHINE_API MoonshineJitterBufferHandle MOONSHINE_CONV moonshine_jitter_create(size_t max_frames);
MOONSHINE_API void MOONSHINE_CONV moonshine_jitter_destroy(MoonshineJitterBufferHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_push_packet(MoonshineJitterBufferHandle handle, const MoonshinePacketDesc* packet);
MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_pop_frame(MoonshineJitterBufferHandle handle, MoonshineFrameDesc* out_frame);

// ============================================================================
// Hardware Video Decoder APIs (Direct3D 11/12 & Vulkan)
// ============================================================================

typedef void* MoonshineDecoderHandle;

MOONSHINE_API int MOONSHINE_CONV moonshine_video_query_caps(MoonshineDecoderCaps* out_caps);
MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d11(void* hwnd, uint32_t width, uint32_t height, uint32_t codec);
MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d12(void* hwnd, uint32_t width, uint32_t height, uint32_t codec);
MOONSHINE_API void MOONSHINE_CONV moonshine_video_destroy(MoonshineDecoderHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_submit_frame(MoonshineDecoderHandle handle, const MoonshineFrameDesc* frame);

// ============================================================================
// Low-Latency DXGI Flip Model Swapchain APIs
// ============================================================================

typedef void* MoonshineSwapchainHandle;

MOONSHINE_API MoonshineSwapchainHandle MOONSHINE_CONV moonshine_swapchain_create(
    void* hwnd,
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t buffer_count,
    uint8_t is_hdr10
);
MOONSHINE_API void MOONSHINE_CONV moonshine_swapchain_destroy(MoonshineSwapchainHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_present(MoonshineSwapchainHandle handle, uint32_t sync_interval, uint32_t flags);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_resize(MoonshineSwapchainHandle handle, uint32_t width, uint32_t height);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr(MoonshineSwapchainHandle handle, uint8_t is_hdr10);

// ============================================================================
// Sub-5ms WASAPI Low-Latency Audio APIs
// ============================================================================

typedef void* MoonshineAudioHandle;

MOONSHINE_API MoonshineAudioHandle MOONSHINE_CONV moonshine_audio_create_wasapi(uint32_t sample_rate, uint16_t channels, uint16_t is_exclusive);
MOONSHINE_API void MOONSHINE_CONV moonshine_audio_destroy(MoonshineAudioHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_audio_submit_pcm(MoonshineAudioHandle handle, const float* pcm_data, uint32_t sample_count);
MOONSHINE_API void MOONSHINE_CONV moonshine_audio_get_metrics(MoonshineAudioHandle handle, uint64_t* out_frames_rendered, uint32_t* out_underruns);

// ============================================================================
// WASAPI Master Loopback Audio Capture APIs
// ============================================================================

typedef void* MoonshineAudioCaptureHandle;

MOONSHINE_API MoonshineAudioCaptureHandle MOONSHINE_CONV moonshine_audio_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_destroy(
    MoonshineAudioCaptureHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_float(
    MoonshineAudioCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_pcm16(
    MoonshineAudioCaptureHandle handle,
    int16_t* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_get_metrics(
    MoonshineAudioCaptureHandle handle,
    uint64_t* out_frames_captured,
    uint64_t* out_samples_captured,
    uint32_t* out_underruns,
    uint32_t* out_overruns
);

// ============================================================================
// Low-Latency Multi-Channel Opus Audio Encoder APIs
// ============================================================================

typedef void* MoonshineOpusEncoderHandle;

MOONSHINE_API MoonshineOpusEncoderHandle MOONSHINE_CONV moonshine_opus_encoder_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t bitrate,
    uint32_t frame_duration_ms,
    uint32_t complexity,
    int32_t use_vbr
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_destroy(
    MoonshineOpusEncoderHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_float(
    MoonshineOpusEncoderHandle handle,
    const float* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_pcm16(
    MoonshineOpusEncoderHandle handle,
    const int16_t* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_bitrate(
    MoonshineOpusEncoderHandle handle,
    uint32_t bitrate
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_complexity(
    MoonshineOpusEncoderHandle handle,
    uint32_t complexity
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_get_metrics(
    MoonshineOpusEncoderHandle handle,
    uint64_t* out_frames_encoded,
    uint64_t* out_bytes_encoded,
    double* out_avg_encode_time_us,
    uint32_t* out_bitrate,
    uint32_t* out_streams_count
);

// ============================================================================
// Low-Latency Client-to-Host Microphone Virtual Audio Sink APIs
// ============================================================================

typedef void* MoonshineMicSinkHandle;

MOONSHINE_API MoonshineMicSinkHandle MOONSHINE_CONV moonshine_mic_sink_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t target_latency_ms,
    float gain_multiplier,
    float noise_gate_threshold_db,
    uint8_t is_muted
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_destroy(
    MoonshineMicSinkHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_push_opus_packet(
    MoonshineMicSinkHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_len,
    uint32_t timestamp,
    uint16_t sequence_number
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_pull_pcm(
    MoonshineMicSinkHandle handle,
    float* out_pcm,
    uint32_t max_samples,
    uint32_t* out_samples_read
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_gain(
    MoonshineMicSinkHandle handle,
    float gain
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_mute(
    MoonshineMicSinkHandle handle,
    uint8_t is_muted
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_get_metrics(
    MoonshineMicSinkHandle handle,
    uint64_t* out_packets_received,
    uint64_t* out_samples_rendered,
    uint32_t* out_loss_count,
    uint32_t* out_drift_corrections,
    double* out_jitter_ms
);

// ============================================================================
// Zero-Copy Direct3D Desktop Capture APIs
// ============================================================================

typedef void* MoonshineCaptureHandle;

MOONSHINE_API MoonshineCaptureHandle MOONSHINE_CONV moonshine_capture_create_dxgi(
    uint32_t adapter_index,
    uint32_t output_index,
    uint32_t* out_width,
    uint32_t* out_height
);
MOONSHINE_API MoonshineCaptureHandle MOONSHINE_CONV moonshine_capture_create_wgc(
    void* hmonitor,
    uint32_t target_fps,
    uint32_t* out_width,
    uint32_t* out_height
);
MOONSHINE_API void MOONSHINE_CONV moonshine_capture_destroy(MoonshineCaptureHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_acquire_frame(
    MoonshineCaptureHandle handle,
    uint32_t timeout_ms,
    MoonshineCaptureFrameDesc* out_frame
);
MOONSHINE_API void MOONSHINE_CONV moonshine_capture_release_frame(MoonshineCaptureHandle handle);

// ============================================================================
// HDR10 Metadata Extraction & Real-Time Color Space Conversion APIs
// ============================================================================

typedef struct MoonshineHdr10Metadata {
    uint16_t red_primary[2];                // BT.2020 Red coordinates (scaled by 50000)
    uint16_t green_primary[2];              // BT.2020 Green coordinates (scaled by 50000)
    uint16_t blue_primary[2];               // BT.2020 Blue coordinates (scaled by 50000)
    uint16_t white_point[2];                // D65 White Point coordinates (scaled by 50000)
    uint32_t max_mastering_luminance;       // Max mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    uint32_t min_mastering_luminance;       // Min mastering luminance in 0.0001 cd/m^2 (nits * 10000)
    uint16_t max_content_light_level;       // MaxCLL in nits
    uint16_t max_frame_average_light_level; // MaxFALL in nits
    uint8_t  hdr_enabled;                   // 1 if HDR10 active, 0 for SDR
    uint8_t  color_space;                   // 0 for BT.709, 1 for BT.2020
    uint8_t  reserved[2];                   // Padding for strict 32-byte alignment
} MoonshineHdr10Metadata;

typedef void* MoonshineColorConverterHandle;

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_extract_metadata(
    void* hmonitor,
    MoonshineHdr10Metadata* out_metadata
);

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_parse_capabilities(
    uint32_t color_space_dxgi,
    MoonshineHdr10Metadata* out_metadata
);

MOONSHINE_API MoonshineColorConverterHandle MOONSHINE_CONV moonshine_color_converter_create(
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t in_format,
    uint32_t out_format
);

MOONSHINE_API int MOONSHINE_CONV moonshine_color_converter_convert(
    MoonshineColorConverterHandle handle,
    void* in_texture,
    void* out_texture
);

MOONSHINE_API void MOONSHINE_CONV moonshine_color_converter_destroy(
    MoonshineColorConverterHandle handle
);

// ============================================================================
// Multi-Vendor Hardware Video Encoder APIs (NVENC, AMF, QuickSync, D3D11)
// ============================================================================

typedef struct MoonshineEncoderCaps {
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
    uint32_t reserved;              // Padding for strict 32-byte alignment
} MoonshineEncoderCaps;

typedef struct MoonshineEncoderConfig {
    uint32_t width;                 // Frame width in pixels
    uint32_t height;                // Frame height in pixels
    uint32_t fps;                   // Target framerate
    uint32_t bitrate_kbps;          // Target bitrate in kbps
    uint32_t peak_bitrate_kbps;     // Peak bitrate for VBR / bursts
    uint32_t codec;                 // 0: H264, 1: HEVC, 2: HEVC Main10, 3: AV1
    uint32_t rc_mode;               // 0: CBR, 1: VBR, 2: CQP
    uint16_t gop_length;            // GOP size
    uint8_t  enable_intra_refresh;  // 1 to enable progressive intra-refresh
    uint8_t  enable_filler_data;    // 1 to emit filler for strict CBR
} MoonshineEncoderConfig;

typedef struct MoonshineEncodedPacketDesc {
    uint64_t frame_index;           // Monotonically increasing frame index
    int64_t  timestamp_qpc;         // High-precision QPC timestamp
    uint32_t payload_size;          // Total size of encoded NAL / OBU bytes
    uint8_t  is_keyframe;           // 1 if IDR / SPS / PPS keyframe
    uint8_t  is_header_packet;      // 1 if packet contains VPS/SPS/PPS parameter sets
    uint8_t  temporal_id;           // Temporal layer ID
    uint8_t  reserved;              // Padding for strict 24-byte alignment
} MoonshineEncodedPacketDesc;

typedef void* MoonshineEncoderHandle;

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_query_caps(
    uint32_t vendor,
    void* d3d_device,
    MoonshineEncoderCaps* out_caps
);

MOONSHINE_API MoonshineEncoderHandle MOONSHINE_CONV moonshine_encoder_create(
    uint32_t vendor,
    void* d3d_device,
    const MoonshineEncoderConfig* config
);

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_encode_frame(
    MoonshineEncoderHandle handle,
    void* d3d_texture,
    int force_idr,
    MoonshineEncodedPacketDesc* out_desc,
    uint8_t* out_buffer,
    uint32_t max_buffer_size,
    uint32_t* out_size
);

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_reconfigure(
    MoonshineEncoderHandle handle,
    const MoonshineEncoderConfig* new_config
);

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_request_keyframe(
    MoonshineEncoderHandle handle
);

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_destroy(
    MoonshineEncoderHandle handle
);

// ============================================================================
// NVIDIA NVENC Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t tuning
);

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t period,
    uint32_t count
);

// ============================================================================
// AMD AMF Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t usage
);

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t mbs_per_slot
);

// ============================================================================
// Intel QuickSync / oneVPL Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
);

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t target_usage,
    int low_power_vdenc
);

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t cycle_size,
    int32_t qp_delta
);

#ifdef __cplusplus
}
#endif

#endif // MOONSHINE_NATIVE_API_H
