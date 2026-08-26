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

typedef enum MoonshineErrorCode {
    MOONSHINE_SUCCESS = 0,
    MOONSHINE_ERR_INVALID_ARGUMENT = -1,
    MOONSHINE_ERR_OUT_OF_MEMORY = -2,
    MOONSHINE_ERR_UNSUPPORTED_HARDWARE = -3,
    MOONSHINE_ERR_DEVICE_LOST = -4,
    MOONSHINE_ERR_BUFFER_TOO_SMALL = -5,
    MOONSHINE_ERR_TIMEOUT = -6,
    MOONSHINE_ERR_TRANSIENT_BUSY = -7,
    MOONSHINE_ERR_USE_AFTER_FREE = -8,
    MOONSHINE_ERR_DOUBLE_RELEASE = -9,
    MOONSHINE_ERR_NOT_INITIALIZED = -10,
    MOONSHINE_ERR_FATAL = -11
} MoonshineErrorCode;

#define MOONSHINE_NO_BUFFER_SLOT (-1)

#pragma pack(push, 1)

/**
 * @brief Blittable descriptor representing a raw video packet for FEC and reassembly (32 bytes).
 * 
 * Packed ABI Specification:
 * - Packing: Pack = 1 (#pragma pack(push, 1))
 * - Size: exactly 32 bytes (sizeof == 32)
 * - Alignment: struct alignment is 1 (alignof == 1)
 * - Pointer Layout: payload_ptr is located at byte offset 24 (8-byte address-aligned within the struct)
 */
typedef struct MoonshinePacketDesc {
    uint32_t sequence_number;    // offset 0,  size 4
    uint32_t frame_index;        // offset 4,  size 4
    uint16_t packet_index;       // offset 8,  size 2
    uint16_t total_packets;      // offset 10, size 2
    uint16_t payload_size;       // offset 12, size 2
    uint8_t  packet_type;        // offset 14, size 1 (0: Data, 1: Parity)
    uint8_t  flags;              // offset 15, size 1 (Bit 0: Start, Bit 1: End, Bit 2: Keyframe)
    int32_t  buffer_slot_index;  // offset 16, size 4 (MOONSHINE_NO_BUFFER_SLOT = -1 if unbacked)
    uint32_t stream_packet_index;// offset 20, size 4 (GameStream SPI, zero for non-GameStream packets)
    const uint8_t* payload_ptr;  // offset 24, size 8
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

typedef struct MoonshineQualityMetrics {
    float psnr_y;                       // Peak signal-to-noise ratio (luminance)
    float psnr_rgb;                     // Peak signal-to-noise ratio (all channels)
    float mae;                          // Mean absolute error
    float max_error;                    // Maximum single-pixel channel error
    float pixels_within_tolerance_pct;  // Percentage of pixels within tolerance
    uint32_t width;
    uint32_t height;
    uint32_t reference_format;
    uint32_t decoded_format;
    uint32_t evaluation_mode;           // 0: Fast / Sampled, 1: Full-Frame Exact
    uint8_t  is_full_frame;             // 1 if 100% full frame coverage evaluated, 0 otherwise
    uint8_t  color_range;               // 0: Full dynamic range [0..255], 1: Nominal video range [16..235]
    uint8_t  reserved[2];               // Explicit padding for 4-byte boundary
} MoonshineQualityMetrics;

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

/**
 * @brief Blittable descriptor representing a physical GPU display adapter.
 */
typedef struct MoonshineAdapterInfo {
    uint32_t adapter_index;
    int64_t  adapter_luid;
    char     description[128];
    uint64_t dedicated_video_memory;
    uint8_t  is_hardware;
    uint8_t  reserved[11];
} MoonshineAdapterInfo;

/**
 * @brief Blittable descriptor representing comprehensive physical GPU adapter hardware inventory.
 */
typedef struct MoonshineGpuAdapter {
    uint32_t index;                     // Adapter index in DXGI enumeration (0, 1, ...)
    uint32_t vendor_id;                 // PCI Vendor ID (e.g. 0x10DE = NVIDIA, 0x8086 = Intel, 0x1002 = AMD)
    uint32_t device_id;                 // PCI Device ID
    uint32_t subsystem_id;              // PCI Subsystem ID
    uint32_t revision;                  // PCI Revision number
    uint32_t is_software;               // 1 if software adapter (e.g. Microsoft Basic Render Driver), 0 for physical hardware
    uint32_t has_output;                // 1 if adapter has at least one enumerated DXGI output, 0 if headless/secondary
    uint32_t reserved;                  // Explicit 32-bit padding for strict 64-bit alignment
    uint64_t adapter_luid;              // Locally Unique Identifier (LUID)
    uint64_t dedicated_video_memory;    // Dedicated video memory in bytes
    uint64_t shared_system_memory;       // Shared system memory in bytes
    char     description[128];          // Device description UTF-8 string
} MoonshineGpuAdapter;

/**
 * @brief Blittable descriptor representing granular Intel QuickSync / oneVPL hardware diagnostic report.
 */
typedef struct MoonshineQsvDiagnosticReport {
    uint32_t adapter_found;             // 0: 1 if Intel adapter (0x8086) found in DXGI enumeration
    uint32_t adapter_device_id;         // 4: PCI Device ID of Intel adapter
    uint32_t d3d11_device_created;      // 8: 1 if ID3D11Device successfully created on Intel adapter
    uint32_t d3d11_vendor_verified;     // 12: 1 if D3D11 device QI/GetAdapter reports 0x8086
    uint32_t vpl_dll_loaded;            // 16: 1 if oneVPL dispatcher DLL loaded
    uint32_t vpl_config_created;        // 20: 1 if MFXCreateConfig succeeded
    uint32_t vpl_impl_filter_applied;   // 24: 1 if MFXSetConfigFilterProperty(Impl) succeeded
    uint32_t vpl_accel_filter_applied;  // 28: 1 if MFXSetConfigFilterProperty(AccelerationMode) succeeded
    uint32_t vpl_session_created;       // 32: 1 if modern oneVPL MFXCreateSession succeeded
    uint32_t d3d11_handle_bound;        // 36: 1 if MFXVideoCORE_SetHandle(D3D11) succeeded
    uint32_t h264_queried;              // 40: 1 if H.264 query attempted against active Intel session
    uint32_t hevc_queried;              // 44: 1 if HEVC query attempted against active Intel session
    uint32_t av1_queried;               // 48: 1 if AV1 query attempted against active Intel session
    uint32_t h264_supported;            // 52: 1 if H.264 supported on active Intel session
    uint32_t hevc_supported;            // 56: 1 if HEVC supported on active Intel session
    uint32_t av1_supported;             // 60: 1 if AV1 supported on active Intel session
    uint32_t encoder_configured;        // 64: 1 if MFXVideoENCODE_Init succeeded
    uint32_t frame_encoded;             // 68: 1 if MFXVideoENCODE_EncodeFrameAsync produced bitstream
    uint32_t bitstream_valid;           // 72: 1 if NALU start codes / headers validated
    uint32_t decoder_created;           // 76: 1 if D3D11 video decoder created
    uint32_t decoder_accepted;          // 80: 1 if D3D11 video decoder SubmitFrame accepted frame
    uint32_t decoded_texture_available; // 84: 1 if reconstructed decoded texture available and verified
    uint32_t decoder_loopback_passed;   // 88: 1 if end-to-end loopback decode passed
    uint32_t legacy_mfx_fallback_used;  // 92: 1 if legacy MSDK fallback was used (0 in pure modern oneVPL)
    int32_t  last_mfx_status;           // 96: Last mfxStatus return code from oneVPL API
    int32_t  impl_filter_status;        // 100: mfxStatus from Impl filter property
    int32_t  accel_filter_status;       // 104: mfxStatus from AccelerationMode filter property
    int32_t  last_hresult;              // 108: Last HRESULT return code from DirectX API
    char     adapter_description[128];  // 112: Intel adapter description UTF-8 string
    char     vpl_dll_name[64];          // 240: Resolved DLL name
    char     first_failed_stage[64];    // 304: Human-readable name of first failed stage
    uint32_t reserved[4];               // 368: Reserved padding (384 bytes total, 8-byte aligned)
} MoonshineQsvDiagnosticReport;

/**
 * @brief Blittable descriptor representing a physical display output.
 */
typedef struct MoonshineDisplayInfo {
    uint32_t display_index;
    uint32_t adapter_index;
    uint32_t width;
    uint32_t height;
    uint32_t refresh_rate_num;
    uint32_t refresh_rate_den;
    uint32_t rotation;
    uint8_t  is_attached_to_desktop;
    uint8_t  is_hdr;
    uint8_t  bits_per_color;
    uint8_t  reserved[5];
} MoonshineDisplayInfo;

/**
 * @brief Blittable descriptor representing an available display mode (resolution & refresh rate).
 */
typedef struct MoonshineDisplayModeDesc {
    uint32_t width;
    uint32_t height;
    uint32_t refresh_rate_num;
    uint32_t refresh_rate_den;
    uint32_t format;
    uint32_t scaling;
    uint32_t scanline_ordering;
    uint8_t  is_hdr;
    uint8_t  reserved[3];
} MoonshineDisplayModeDesc;

/**
 * @brief Blittable descriptor representing extended physical display output metadata and Windows desktop topology.
 */
typedef struct MoonshineDisplayExtendedInfo {
    uint32_t display_index;
    uint32_t adapter_index;
    int64_t  monitor_handle;
    int32_t  desktop_left;
    int32_t  desktop_top;
    int32_t  desktop_right;
    int32_t  desktop_bottom;
    uint32_t dpi_scale;
    uint8_t  is_primary;
    uint8_t  is_attached_to_desktop;
    uint8_t  is_hdr;
    uint8_t  bits_per_color;
    char     device_name[32];
    char     friendly_name[64];
    uint8_t  reserved[16];
} MoonshineDisplayExtendedInfo;

#pragma pack(pop)

// ============================================================================
// SIMD Reed-Solomon Forward Error Correction (FEC) APIs
// ============================================================================

/**
 * @brief Encodes parity shards from data shards using the Cauchy systematic generator matrix.
 * @param data_shards Array of pointers to data shards.
 * @param data_shards_count Number of data shards (K <= 64, K + M <= 255).
 * @param parity_shards Array of pointers to parity shards.
 * @param parity_shards_count Number of parity shards (M <= 32, K + M <= 255).
 * @param shard_size Size in bytes of each shard.
 * @return 0 on success, non-zero on error.
 */
MOONSHINE_API int MOONSHINE_CONV moonshine_fec_encode_simd(
    const uint8_t* const* data_shards,
    int data_shards_count,
    uint8_t** parity_shards,
    int parity_shards_count,
    int shard_size
);

/**
 * @brief Reconstructs lost data and parity shards using genuine GF(2^8) Gauss-Jordan matrix inversion.
 * @param shards Array of pointers to all shards (K data followed by M parity shards).
 * @param data_shards_count Number of data shards (K <= 64, K + M <= 255).
 * @param parity_shards_count Number of parity shards (M <= 32, K + M <= 255).
 * @param shard_size Size in bytes of each shard.
 * @param erased_indices Indices of lost shards to reconstruct (must be unique and in [0, K+M)).
 * @param erased_count Number of lost shards (must be <= M).
 * @return 0 on success, non-zero on error (-1: invalid args, -2: unrecoverable, -3: singular matrix).
 */
MOONSHINE_API int MOONSHINE_CONV moonshine_fec_reconstruct_simd(
    uint8_t** shards,
    int data_shards_count,
    int parity_shards_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
);

/**
 * @brief Performs vectorized AVX2/AVX-512 Galois Field GF(2^8) parity recovery (backward compatible).
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
 * @return 0: Scalar, 1: AVX2, 2: AVX-512.
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
// Lock-Free SPSC Slot Return Queue Management APIs
// Single Producer (Stream Native Forward-Queue Consumer) / Single Consumer (Managed Ingestion Loop)
// ============================================================================

/**
 * Creates an unmanaged lock-free SPSC ring buffer for slot index recycling.
 * @param capacity Maximum number of slot indices the return ring can buffer.
 * @return Handle to the unmanaged return queue, or nullptr on allocation failure.
 */
MOONSHINE_API MoonshineRingBufferHandle MOONSHINE_CONV moonshine_slot_return_create(size_t capacity);

/**
 * Destroys an unmanaged slot return queue and frees allocated memory.
 * @param handle Handle to the unmanaged return queue. Safe no-op if nullptr.
 */
MOONSHINE_API void MOONSHINE_CONV moonshine_slot_return_destroy(MoonshineRingBufferHandle handle);

/**
 * Enqueues a recycled slot index.
 * Thread-safety: Each return ring has exactly one producer: the stream's dedicated native
 * forward-queue consumer thread, which is also the sole native consumer of MoonshinePacketDesc for that stream.
 * @param handle Handle to the unmanaged return queue.
 * @param slot_index The recycled buffer slot index to return to the pool.
 * @return 1 on successful enqueue, 0 if queue is full or handle is invalid.
 */
MOONSHINE_API int MOONSHINE_CONV moonshine_slot_return_enqueue(MoonshineRingBufferHandle handle, int32_t slot_index);

/**
 * Dequeues a recycled slot index on the single managed ingestion thread (TryRent).
 * Thread-safety: Strictly Single-Consumer. Must only be invoked from one thread per queue.
 * @param handle Handle to the unmanaged return queue.
 * @param out_slot_index Pointer to receive the dequeued slot index.
 * @return 1 on successful dequeue, 0 if queue is empty or handle/pointer is invalid.
 */
MOONSHINE_API int MOONSHINE_CONV moonshine_slot_return_dequeue(MoonshineRingBufferHandle handle, int32_t* out_slot_index);

/**
 * Returns the current count of elements pending in the slot return queue.
 * @param handle Handle to the unmanaged return queue.
 * @return Current number of queued slot indices, or 0 if handle is invalid.
 */
MOONSHINE_API size_t MOONSHINE_CONV moonshine_slot_return_size(MoonshineRingBufferHandle handle);

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
MOONSHINE_API void* MOONSHINE_CONV moonshine_video_get_texture(MoonshineDecoderHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_reset(MoonshineDecoderHandle handle, uint32_t width, uint32_t height);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_get_dimensions(MoonshineDecoderHandle handle, uint32_t* out_width, uint32_t* out_height);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_verify_decoded_pattern(void* decoder, uint32_t pattern_type, float tolerance);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_compute_quality_metrics(
    const uint8_t* reference_pixels,
    uint32_t reference_format,
    const uint8_t* decoded_pixels,
    uint32_t decoded_format,
    uint32_t width,
    uint32_t height,
    float tolerance,
    uint32_t evaluation_mode,
    MoonshineQualityMetrics* out_metrics
);

// ============================================================================
// Low-Latency DXGI Flip Model Swapchain APIs
// ============================================================================

typedef void* MoonshineSwapchainHandle;
typedef struct MoonshineHdr10Metadata MoonshineHdr10Metadata;

#pragma pack(push, 8)
typedef struct MoonshineSwapchainMetrics {
    uint64_t frames_presented;
    uint64_t presentation_errors;
    uint64_t dropped_frames;
} MoonshineSwapchainMetrics;
#pragma pack(pop)

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
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_present_texture(MoonshineSwapchainHandle handle, void* texture_handle, uint32_t sync_interval, uint32_t flags);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_resize(MoonshineSwapchainHandle handle, uint32_t width, uint32_t height);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr(MoonshineSwapchainHandle handle, uint8_t is_hdr10);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr_metadata(MoonshineSwapchainHandle handle, const MoonshineHdr10Metadata* metadata);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_get_metrics(MoonshineSwapchainHandle handle, MoonshineSwapchainMetrics* out_metrics);
MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_is_tearing_supported(MoonshineSwapchainHandle handle);
MOONSHINE_API void* MOONSHINE_CONV moonshine_swapchain_get_waitable_object(MoonshineSwapchainHandle handle);

// ============================================================================
// Sub-5ms WASAPI Low-Latency Audio APIs
// ============================================================================

typedef void* MoonshineAudioHandle;

MOONSHINE_API MoonshineAudioHandle MOONSHINE_CONV moonshine_audio_create_wasapi(uint32_t sample_rate, uint16_t channels, uint16_t is_exclusive);
MOONSHINE_API void MOONSHINE_CONV moonshine_audio_destroy(MoonshineAudioHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_audio_submit_pcm(MoonshineAudioHandle handle, const float* pcm_data, uint32_t sample_count);
MOONSHINE_API void MOONSHINE_CONV moonshine_audio_get_metrics(MoonshineAudioHandle handle, uint64_t* out_frames_rendered, uint32_t* out_underruns);
MOONSHINE_API int MOONSHINE_CONV moonshine_audio_recover(MoonshineAudioHandle handle);

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

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_recover(
    MoonshineAudioCaptureHandle handle
);


// ============================================================================
// WASAPI Microphone Audio Capture APIs
// ============================================================================

typedef void* MoonshineMicCaptureHandle;

MOONSHINE_API MoonshineMicCaptureHandle MOONSHINE_CONV moonshine_mic_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
);

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_capture_destroy(
    MoonshineMicCaptureHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_read_float(
    MoonshineMicCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_is_active(
    MoonshineMicCaptureHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_recover(
    MoonshineMicCaptureHandle handle
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
// Low-Latency Multi-Channel Opus Audio Decoder APIs
// ============================================================================

typedef void* MoonshineOpusDecoderHandle;

MOONSHINE_API MoonshineOpusDecoderHandle MOONSHINE_CONV moonshine_opus_decoder_create(
    uint32_t sample_rate,
    uint32_t channels
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_destroy(
    MoonshineOpusDecoderHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_decoder_decode_float(
    MoonshineOpusDecoderHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    float* out_pcm_samples,
    uint32_t max_samples,
    uint32_t* out_samples_decoded,
    int32_t decode_fec
);

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_decoder_decode_pcm16(
    MoonshineOpusDecoderHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    int16_t* out_pcm_samples,
    uint32_t max_samples,
    uint32_t* out_samples_decoded,
    int32_t decode_fec
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_reset(
    MoonshineOpusDecoderHandle handle
);

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_get_metrics(
    MoonshineOpusDecoderHandle handle,
    uint64_t* out_frames_decoded,
    uint64_t* out_samples_decoded,
    uint32_t* out_decode_errors,
    uint32_t* out_concealment_frames,
    double* out_avg_decode_time_us,
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
// Dedicated Windows Virtual Audio Driver Controller APIs
// ============================================================================

typedef void* MoonshineVirtualAudioDriverHandle;

typedef struct MoonshineVirtualAudioDriverStatusC {
    uint8_t is_installed;
    uint8_t is_render_endpoint_present;
    uint8_t is_capture_endpoint_present;
    uint8_t reserved;
    uint32_t supported_sample_rates_count;
    uint32_t supported_channels_count;
    char driver_version[32];
} MoonshineVirtualAudioDriverStatusC;

MOONSHINE_API MoonshineVirtualAudioDriverHandle MOONSHINE_CONV moonshine_virtual_audio_driver_create(void);

MOONSHINE_API void MOONSHINE_CONV moonshine_virtual_audio_driver_destroy(
    MoonshineVirtualAudioDriverHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_is_installed(
    MoonshineVirtualAudioDriverHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_status(
    MoonshineVirtualAudioDriverHandle handle,
    MoonshineVirtualAudioDriverStatusC* out_status
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_validate_format(
    MoonshineVirtualAudioDriverHandle handle,
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t format_type
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_endpoint_names(
    MoonshineVirtualAudioDriverHandle handle,
    char* out_render_name,
    uint32_t render_name_max_len,
    char* out_capture_name,
    uint32_t capture_name_max_len
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_enable_mmcss(
    MoonshineVirtualAudioDriverHandle handle,
    void** out_task_handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_disable_mmcss(
    MoonshineVirtualAudioDriverHandle handle,
    void* task_handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_installation_state(
    MoonshineVirtualAudioDriverHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_install(
    MoonshineVirtualAudioDriverHandle handle,
    const char* inf_path
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_remove(
    MoonshineVirtualAudioDriverHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_restart(
    MoonshineVirtualAudioDriverHandle handle
);

// ============================================================================
// Real-Time Shared Memory IPC Bridge APIs
// ============================================================================

typedef void* MoonshineAudioIpcBridgeHandle;

typedef struct MoonshineAudioIpcMetricsC {
    uint32_t render_packets_read;
    uint32_t render_underruns;
    uint32_t render_overruns;
    uint32_t capture_packets_written;
    uint32_t capture_underruns;
    uint32_t capture_overruns;
    uint32_t sample_rate;
    uint32_t channels;
    uint32_t is_connected;
} MoonshineAudioIpcMetricsC;

MOONSHINE_API MoonshineAudioIpcBridgeHandle MOONSHINE_CONV moonshine_audio_ipc_bridge_create(
    int is_host_server,
    uint32_t sample_rate,
    uint32_t channels
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_ipc_bridge_destroy(
    MoonshineAudioIpcBridgeHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_is_connected(
    MoonshineAudioIpcBridgeHandle handle
);

MOONSHINE_API int64_t MOONSHINE_CONV moonshine_audio_ipc_bridge_write_capture_pcm(
    MoonshineAudioIpcBridgeHandle handle,
    const float* pcm_samples,
    uint32_t sample_count
);

MOONSHINE_API int64_t MOONSHINE_CONV moonshine_audio_ipc_bridge_read_render_pcm(
    MoonshineAudioIpcBridgeHandle handle,
    float* out_pcm_samples,
    uint32_t max_samples,
    int wait_event,
    uint32_t timeout_ms
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_wait_render_event(
    MoonshineAudioIpcBridgeHandle handle,
    uint32_t timeout_ms
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_get_metrics(
    MoonshineAudioIpcBridgeHandle handle,
    MoonshineAudioIpcMetricsC* out_metrics
);

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_enable_mmcss(
    MoonshineAudioIpcBridgeHandle handle
);

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_ipc_bridge_revert_mmcss(
    MoonshineAudioIpcBridgeHandle handle
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
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_recover(MoonshineCaptureHandle handle);
MOONSHINE_API void* MOONSHINE_CONV moonshine_capture_get_device(MoonshineCaptureHandle handle);
MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_format(MoonshineCaptureHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_is_hdr(MoonshineCaptureHandle handle);
MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_adapter_count(void);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_adapter_info(
    uint32_t adapter_index,
    MoonshineAdapterInfo* out_info
);
MOONSHINE_API int MOONSHINE_CONV moonshine_gpu_enumerate_adapters(
    MoonshineGpuAdapter* out_adapters,
    uint32_t max_count,
    uint32_t* out_count
);
MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_display_count(uint32_t adapter_index);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_info(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayInfo* out_info
);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_extended_info(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayExtendedInfo* out_info
);
MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_display_mode_count(
    uint32_t adapter_index,
    uint32_t display_index
);
MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_modes(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayModeDesc* out_modes,
    uint32_t max_modes,
    uint32_t* out_mode_count
);

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

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_drain(
    MoonshineEncoderHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_flush(
    MoonshineEncoderHandle handle
);

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_destroy(
    MoonshineEncoderHandle handle
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_encoder_get_state(
    MoonshineEncoderHandle handle
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_encoder_is_healthy(
    MoonshineEncoderHandle handle
);

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_encoder_get_vendor(
    MoonshineEncoderHandle handle
);

// ============================================================================
// Direct3D 11 Hardware Device & Texture Utility APIs
// ============================================================================

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_device(uint32_t vendor_id);

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_device_on_adapter(
    uint32_t vendor_id,
    uint32_t adapter_index
);

MOONSHINE_API void MOONSHINE_CONV moonshine_d3d11_destroy_device(void* d3d_device);
MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_texture(void* d3d_device, uint32_t width, uint32_t height, uint32_t format);
MOONSHINE_API void MOONSHINE_CONV moonshine_d3d11_destroy_texture(void* texture);
MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_pattern_texture(void* d3d_device, uint32_t width, uint32_t height, uint32_t pattern_type, uint32_t frame_index);
MOONSHINE_API int MOONSHINE_CONV moonshine_d3d11_render_pattern(void* d3d_device, void* texture, uint32_t width, uint32_t height, uint32_t pattern_type, uint32_t frame_index);
MOONSHINE_API int MOONSHINE_CONV moonshine_d3d11_readback_pixels(void* d3d_device, void* d3d_texture, uint8_t* out_pixels, uint32_t max_bytes, uint32_t* out_bytes);
MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_shared_texture(void* d3d_device, uint32_t width, uint32_t height, uint32_t format, uint32_t misc_flags, void** out_shared_handle);
MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_open_shared_texture(void* d3d_device, void* shared_handle, uint32_t is_nt_handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_d3d11_cross_adapter_copy(void* src_device, void* src_texture, void* dst_device, void* dst_texture, uint32_t width, uint32_t height);

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

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_drain(
    MoonshineEncoderHandle handle
);

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_flush(
    MoonshineEncoderHandle handle
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

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_run_diagnostics(
    MoonshineQsvDiagnosticReport* out_report
);

// ============================================================================
// Windows Mouse & Keyboard Input Injector APIs
// ============================================================================

typedef struct MoonshineVirtualDesktopBoundsC {
    int32_t x_virtual_screen;
    int32_t y_virtual_screen;
    int32_t cx_virtual_screen;
    int32_t cy_virtual_screen;
} MoonshineVirtualDesktopBoundsC;

MOONSHINE_API void* MOONSHINE_CONV moonshine_input_injector_create(void);

MOONSHINE_API void MOONSHINE_CONV moonshine_input_injector_destroy(void* injector);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_move(
    void* injector,
    int16_t delta_x,
    int16_t delta_y
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_abs(
    void* injector,
    int32_t x,
    int32_t y,
    int32_t client_width,
    int32_t client_height,
    int32_t monitor_offset_x,
    int32_t monitor_offset_y,
    int32_t monitor_width,
    int32_t monitor_height
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_button(
    void* injector,
    uint8_t button_index,
    int32_t is_down
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_scroll(
    void* injector,
    int16_t scroll_delta,
    int32_t is_horizontal
);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_keyboard(
    void* injector,
    int16_t virtual_key_code,
    int16_t scan_code,
    int32_t is_down,
    uint8_t modifiers
);

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_input_inject_batch(
    void* injector,
    const void* inputs,
    uint32_t count
);

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_input_release_all_held(void* injector);

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_get_virtual_desktop_bounds(
    void* injector,
    MoonshineVirtualDesktopBoundsC* bounds
);

MOONSHINE_API void MOONSHINE_CONV moonshine_input_refresh_virtual_desktop_bounds(void* injector);

#ifdef __cplusplus
}

// ============================================================================
// Compile-Time C-ABI Layout and Offset Verification Assertions
// ============================================================================

static_assert(sizeof(MoonshinePacketDesc) == 32, "MoonshinePacketDesc size mismatch");
static_assert(offsetof(MoonshinePacketDesc, sequence_number) == 0, "MoonshinePacketDesc::sequence_number offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, frame_index) == 4, "MoonshinePacketDesc::frame_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, packet_index) == 8, "MoonshinePacketDesc::packet_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, total_packets) == 10, "MoonshinePacketDesc::total_packets offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, payload_size) == 12, "MoonshinePacketDesc::payload_size offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, packet_type) == 14, "MoonshinePacketDesc::packet_type offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, flags) == 15, "MoonshinePacketDesc::flags offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, buffer_slot_index) == 16, "MoonshinePacketDesc::buffer_slot_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, stream_packet_index) == 20, "MoonshinePacketDesc::stream_packet_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, payload_ptr) == 24, "MoonshinePacketDesc::payload_ptr offset mismatch");

static_assert(sizeof(MoonshineFrameDesc) == 24, "MoonshineFrameDesc size mismatch");
static_assert(offsetof(MoonshineFrameDesc, frame_index) == 0, "MoonshineFrameDesc::frame_index offset mismatch");
static_assert(offsetof(MoonshineFrameDesc, total_bytes) == 4, "MoonshineFrameDesc::total_bytes offset mismatch");
static_assert(offsetof(MoonshineFrameDesc, packet_count) == 8, "MoonshineFrameDesc::packet_count offset mismatch");
static_assert(offsetof(MoonshineFrameDesc, is_keyframe) == 12, "MoonshineFrameDesc::is_keyframe offset mismatch");
static_assert(offsetof(MoonshineFrameDesc, reserved) == 13, "MoonshineFrameDesc::reserved offset mismatch");
static_assert(offsetof(MoonshineFrameDesc, frame_buffer) == 16, "MoonshineFrameDesc::frame_buffer offset mismatch");

static_assert(sizeof(MoonshineDecoderCaps) == 20, "MoonshineDecoderCaps size mismatch");
static_assert(offsetof(MoonshineDecoderCaps, max_width) == 0, "MoonshineDecoderCaps::max_width offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, max_height) == 4, "MoonshineDecoderCaps::max_height offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, max_fps) == 8, "MoonshineDecoderCaps::max_fps offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_av1) == 12, "MoonshineDecoderCaps::supports_av1 offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_hevc) == 13, "MoonshineDecoderCaps::supports_hevc offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_h264) == 14, "MoonshineDecoderCaps::supports_h264 offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_hdr10) == 15, "MoonshineDecoderCaps::supports_hdr10 offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_10bit) == 16, "MoonshineDecoderCaps::supports_10bit offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_d3d12) == 17, "MoonshineDecoderCaps::supports_d3d12 offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, supports_vulkan) == 18, "MoonshineDecoderCaps::supports_vulkan offset mismatch");
static_assert(offsetof(MoonshineDecoderCaps, reserved) == 19, "MoonshineDecoderCaps::reserved offset mismatch");

static_assert(sizeof(MoonshineCaptureFrameDesc) == 36, "MoonshineCaptureFrameDesc size mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, texture_handle) == 0, "MoonshineCaptureFrameDesc::texture_handle offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, width) == 8, "MoonshineCaptureFrameDesc::width offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, height) == 12, "MoonshineCaptureFrameDesc::height offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, format) == 16, "MoonshineCaptureFrameDesc::format offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, timestamp_qpc) == 20, "MoonshineCaptureFrameDesc::timestamp_qpc offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, accumulated_frames) == 28, "MoonshineCaptureFrameDesc::accumulated_frames offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, cursor_visible) == 32, "MoonshineCaptureFrameDesc::cursor_visible offset mismatch");
static_assert(offsetof(MoonshineCaptureFrameDesc, reserved) == 33, "MoonshineCaptureFrameDesc::reserved offset mismatch");

static_assert(sizeof(MoonshineHdr10Metadata) == 32, "MoonshineHdr10Metadata size mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, red_primary) == 0, "MoonshineHdr10Metadata::red_primary offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, green_primary) == 4, "MoonshineHdr10Metadata::green_primary offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, blue_primary) == 8, "MoonshineHdr10Metadata::blue_primary offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, white_point) == 12, "MoonshineHdr10Metadata::white_point offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, max_mastering_luminance) == 16, "MoonshineHdr10Metadata::max_mastering_luminance offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, min_mastering_luminance) == 20, "MoonshineHdr10Metadata::min_mastering_luminance offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, max_content_light_level) == 24, "MoonshineHdr10Metadata::max_content_light_level offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, max_frame_average_light_level) == 26, "MoonshineHdr10Metadata::max_frame_average_light_level offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, hdr_enabled) == 28, "MoonshineHdr10Metadata::hdr_enabled offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, color_space) == 29, "MoonshineHdr10Metadata::color_space offset mismatch");
static_assert(offsetof(MoonshineHdr10Metadata, reserved) == 30, "MoonshineHdr10Metadata::reserved offset mismatch");

static_assert(sizeof(MoonshineEncoderCaps) == 32, "MoonshineEncoderCaps size mismatch");
static_assert(offsetof(MoonshineEncoderCaps, supported_codecs_mask) == 0, "MoonshineEncoderCaps::supported_codecs_mask offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, max_width) == 4, "MoonshineEncoderCaps::max_width offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, max_height) == 8, "MoonshineEncoderCaps::max_height offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, max_fps) == 12, "MoonshineEncoderCaps::max_fps offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, supports_10bit) == 16, "MoonshineEncoderCaps::supports_10bit offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, supports_lossless) == 17, "MoonshineEncoderCaps::supports_lossless offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, supports_smart_idr) == 18, "MoonshineEncoderCaps::supports_smart_idr offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, vendor_id) == 19, "MoonshineEncoderCaps::vendor_id offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, min_bitrate_kbps) == 20, "MoonshineEncoderCaps::min_bitrate_kbps offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, max_bitrate_kbps) == 24, "MoonshineEncoderCaps::max_bitrate_kbps offset mismatch");
static_assert(offsetof(MoonshineEncoderCaps, reserved) == 28, "MoonshineEncoderCaps::reserved offset mismatch");

static_assert(sizeof(MoonshineEncoderConfig) == 32, "MoonshineEncoderConfig size mismatch");
static_assert(offsetof(MoonshineEncoderConfig, width) == 0, "MoonshineEncoderConfig::width offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, height) == 4, "MoonshineEncoderConfig::height offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, fps) == 8, "MoonshineEncoderConfig::fps offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, bitrate_kbps) == 12, "MoonshineEncoderConfig::bitrate_kbps offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, peak_bitrate_kbps) == 16, "MoonshineEncoderConfig::peak_bitrate_kbps offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, codec) == 20, "MoonshineEncoderConfig::codec offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, rc_mode) == 24, "MoonshineEncoderConfig::rc_mode offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, gop_length) == 28, "MoonshineEncoderConfig::gop_length offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, enable_intra_refresh) == 30, "MoonshineEncoderConfig::enable_intra_refresh offset mismatch");
static_assert(offsetof(MoonshineEncoderConfig, enable_filler_data) == 31, "MoonshineEncoderConfig::enable_filler_data offset mismatch");

static_assert(sizeof(MoonshineEncodedPacketDesc) == 24, "MoonshineEncodedPacketDesc size mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, frame_index) == 0, "MoonshineEncodedPacketDesc::frame_index offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, timestamp_qpc) == 8, "MoonshineEncodedPacketDesc::timestamp_qpc offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, payload_size) == 16, "MoonshineEncodedPacketDesc::payload_size offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, is_keyframe) == 20, "MoonshineEncodedPacketDesc::is_keyframe offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, is_header_packet) == 21, "MoonshineEncodedPacketDesc::is_header_packet offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, temporal_id) == 22, "MoonshineEncodedPacketDesc::temporal_id offset mismatch");
static_assert(offsetof(MoonshineEncodedPacketDesc, reserved) == 23, "MoonshineEncodedPacketDesc::reserved offset mismatch");

static_assert(sizeof(MoonshineVirtualAudioDriverStatusC) == 44, "MoonshineVirtualAudioDriverStatusC size mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, is_installed) == 0, "MoonshineVirtualAudioDriverStatusC::is_installed offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, is_render_endpoint_present) == 1, "MoonshineVirtualAudioDriverStatusC::is_render_endpoint_present offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, is_capture_endpoint_present) == 2, "MoonshineVirtualAudioDriverStatusC::is_capture_endpoint_present offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, reserved) == 3, "MoonshineVirtualAudioDriverStatusC::reserved offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, supported_sample_rates_count) == 4, "MoonshineVirtualAudioDriverStatusC::supported_sample_rates_count offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, supported_channels_count) == 8, "MoonshineVirtualAudioDriverStatusC::supported_channels_count offset mismatch");
static_assert(offsetof(MoonshineVirtualAudioDriverStatusC, driver_version) == 12, "MoonshineVirtualAudioDriverStatusC::driver_version offset mismatch");

static_assert(sizeof(MoonshineAudioIpcMetricsC) == 36, "MoonshineAudioIpcMetricsC size mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, render_packets_read) == 0, "MoonshineAudioIpcMetricsC::render_packets_read offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, render_underruns) == 4, "MoonshineAudioIpcMetricsC::render_underruns offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, render_overruns) == 8, "MoonshineAudioIpcMetricsC::render_overruns offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, capture_packets_written) == 12, "MoonshineAudioIpcMetricsC::capture_packets_written offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, capture_underruns) == 16, "MoonshineAudioIpcMetricsC::capture_underruns offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, capture_overruns) == 20, "MoonshineAudioIpcMetricsC::capture_overruns offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, sample_rate) == 24, "MoonshineAudioIpcMetricsC::sample_rate offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, channels) == 28, "MoonshineAudioIpcMetricsC::channels offset mismatch");
static_assert(offsetof(MoonshineAudioIpcMetricsC, is_connected) == 32, "MoonshineAudioIpcMetricsC::is_connected offset mismatch");

static_assert(sizeof(MoonshineAdapterInfo) == 160, "MoonshineAdapterInfo size mismatch");
static_assert(offsetof(MoonshineAdapterInfo, adapter_index) == 0, "MoonshineAdapterInfo::adapter_index offset mismatch");
static_assert(offsetof(MoonshineAdapterInfo, adapter_luid) == 4, "MoonshineAdapterInfo::adapter_luid offset mismatch");
static_assert(offsetof(MoonshineAdapterInfo, description) == 12, "MoonshineAdapterInfo::description offset mismatch");
static_assert(offsetof(MoonshineAdapterInfo, dedicated_video_memory) == 140, "MoonshineAdapterInfo::dedicated_video_memory offset mismatch");
static_assert(offsetof(MoonshineAdapterInfo, is_hardware) == 148, "MoonshineAdapterInfo::is_hardware offset mismatch");
static_assert(offsetof(MoonshineAdapterInfo, reserved) == 149, "MoonshineAdapterInfo::reserved offset mismatch");

static_assert(sizeof(MoonshineDisplayInfo) == 36, "MoonshineDisplayInfo size mismatch");
static_assert(offsetof(MoonshineDisplayInfo, display_index) == 0, "MoonshineDisplayInfo::display_index offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, adapter_index) == 4, "MoonshineDisplayInfo::adapter_index offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, width) == 8, "MoonshineDisplayInfo::width offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, height) == 12, "MoonshineDisplayInfo::height offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, refresh_rate_num) == 16, "MoonshineDisplayInfo::refresh_rate_num offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, refresh_rate_den) == 20, "MoonshineDisplayInfo::refresh_rate_den offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, rotation) == 24, "MoonshineDisplayInfo::rotation offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, is_attached_to_desktop) == 28, "MoonshineDisplayInfo::is_attached_to_desktop offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, is_hdr) == 29, "MoonshineDisplayInfo::is_hdr offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, bits_per_color) == 30, "MoonshineDisplayInfo::bits_per_color offset mismatch");
static_assert(offsetof(MoonshineDisplayInfo, reserved) == 31, "MoonshineDisplayInfo::reserved offset mismatch");

static_assert(sizeof(MoonshineDisplayModeDesc) == 32, "MoonshineDisplayModeDesc size mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, width) == 0, "MoonshineDisplayModeDesc::width offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, height) == 4, "MoonshineDisplayModeDesc::height offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, refresh_rate_num) == 8, "MoonshineDisplayModeDesc::refresh_rate_num offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, refresh_rate_den) == 12, "MoonshineDisplayModeDesc::refresh_rate_den offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, format) == 16, "MoonshineDisplayModeDesc::format offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, scaling) == 20, "MoonshineDisplayModeDesc::scaling offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, scanline_ordering) == 24, "MoonshineDisplayModeDesc::scanline_ordering offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, is_hdr) == 28, "MoonshineDisplayModeDesc::is_hdr offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, reserved) == 29, "MoonshineDisplayModeDesc::reserved offset mismatch");

static_assert(sizeof(MoonshineDisplayExtendedInfo) == 152, "MoonshineDisplayExtendedInfo size mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, display_index) == 0, "MoonshineDisplayExtendedInfo::display_index offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, adapter_index) == 4, "MoonshineDisplayExtendedInfo::adapter_index offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, monitor_handle) == 8, "MoonshineDisplayExtendedInfo::monitor_handle offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_left) == 16, "MoonshineDisplayExtendedInfo::desktop_left offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_top) == 20, "MoonshineDisplayExtendedInfo::desktop_top offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_right) == 24, "MoonshineDisplayExtendedInfo::desktop_right offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_bottom) == 28, "MoonshineDisplayExtendedInfo::desktop_bottom offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, dpi_scale) == 32, "MoonshineDisplayExtendedInfo::dpi_scale offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_primary) == 36, "MoonshineDisplayExtendedInfo::is_primary offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_attached_to_desktop) == 37, "MoonshineDisplayExtendedInfo::is_attached_to_desktop offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_hdr) == 38, "MoonshineDisplayExtendedInfo::is_hdr offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, bits_per_color) == 39, "MoonshineDisplayExtendedInfo::bits_per_color offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, device_name) == 40, "MoonshineDisplayExtendedInfo::device_name offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, friendly_name) == 72, "MoonshineDisplayExtendedInfo::friendly_name offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, reserved) == 136, "MoonshineDisplayExtendedInfo::reserved offset mismatch");

static_assert(sizeof(MoonshineVirtualDesktopBoundsC) == 16, "MoonshineVirtualDesktopBoundsC size mismatch");
static_assert(offsetof(MoonshineVirtualDesktopBoundsC, x_virtual_screen) == 0, "MoonshineVirtualDesktopBoundsC::x_virtual_screen offset mismatch");
static_assert(offsetof(MoonshineVirtualDesktopBoundsC, y_virtual_screen) == 4, "MoonshineVirtualDesktopBoundsC::y_virtual_screen offset mismatch");
static_assert(offsetof(MoonshineVirtualDesktopBoundsC, cx_virtual_screen) == 8, "MoonshineVirtualDesktopBoundsC::cx_virtual_screen offset mismatch");
static_assert(offsetof(MoonshineVirtualDesktopBoundsC, cy_virtual_screen) == 12, "MoonshineVirtualDesktopBoundsC::cy_virtual_screen offset mismatch");

static_assert(sizeof(MoonshineSwapchainMetrics) == 24, "MoonshineSwapchainMetrics size mismatch");
static_assert(offsetof(MoonshineSwapchainMetrics, frames_presented) == 0, "MoonshineSwapchainMetrics::frames_presented offset mismatch");
static_assert(offsetof(MoonshineSwapchainMetrics, presentation_errors) == 8, "MoonshineSwapchainMetrics::presentation_errors offset mismatch");
static_assert(offsetof(MoonshineSwapchainMetrics, dropped_frames) == 16, "MoonshineSwapchainMetrics::dropped_frames offset mismatch");

static_assert(sizeof(MoonshineGpuAdapter) == 184, "MoonshineGpuAdapter size mismatch");
static_assert(offsetof(MoonshineGpuAdapter, index) == 0, "MoonshineGpuAdapter::index offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, vendor_id) == 4, "MoonshineGpuAdapter::vendor_id offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, device_id) == 8, "MoonshineGpuAdapter::device_id offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, subsystem_id) == 12, "MoonshineGpuAdapter::subsystem_id offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, revision) == 16, "MoonshineGpuAdapter::revision offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, is_software) == 20, "MoonshineGpuAdapter::is_software offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, has_output) == 24, "MoonshineGpuAdapter::has_output offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, reserved) == 28, "MoonshineGpuAdapter::reserved offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, adapter_luid) == 32, "MoonshineGpuAdapter::adapter_luid offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, dedicated_video_memory) == 40, "MoonshineGpuAdapter::dedicated_video_memory offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, shared_system_memory) == 48, "MoonshineGpuAdapter::shared_system_memory offset mismatch");
static_assert(offsetof(MoonshineGpuAdapter, description) == 56, "MoonshineGpuAdapter::description offset mismatch");

static_assert(sizeof(MoonshineQsvDiagnosticReport) == 384, "MoonshineQsvDiagnosticReport size mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, adapter_found) == 0, "MoonshineQsvDiagnosticReport::adapter_found offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, adapter_device_id) == 4, "MoonshineQsvDiagnosticReport::adapter_device_id offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, d3d11_device_created) == 8, "MoonshineQsvDiagnosticReport::d3d11_device_created offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, d3d11_vendor_verified) == 12, "MoonshineQsvDiagnosticReport::d3d11_vendor_verified offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_dll_loaded) == 16, "MoonshineQsvDiagnosticReport::vpl_dll_loaded offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_config_created) == 20, "MoonshineQsvDiagnosticReport::vpl_config_created offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_impl_filter_applied) == 24, "MoonshineQsvDiagnosticReport::vpl_impl_filter_applied offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_accel_filter_applied) == 28, "MoonshineQsvDiagnosticReport::vpl_accel_filter_applied offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_session_created) == 32, "MoonshineQsvDiagnosticReport::vpl_session_created offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, d3d11_handle_bound) == 36, "MoonshineQsvDiagnosticReport::d3d11_handle_bound offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, h264_queried) == 40, "MoonshineQsvDiagnosticReport::h264_queried offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, hevc_queried) == 44, "MoonshineQsvDiagnosticReport::hevc_queried offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, av1_queried) == 48, "MoonshineQsvDiagnosticReport::av1_queried offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, h264_supported) == 52, "MoonshineQsvDiagnosticReport::h264_supported offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, hevc_supported) == 56, "MoonshineQsvDiagnosticReport::hevc_supported offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, av1_supported) == 60, "MoonshineQsvDiagnosticReport::av1_supported offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, encoder_configured) == 64, "MoonshineQsvDiagnosticReport::encoder_configured offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, frame_encoded) == 68, "MoonshineQsvDiagnosticReport::frame_encoded offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, bitstream_valid) == 72, "MoonshineQsvDiagnosticReport::bitstream_valid offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, decoder_created) == 76, "MoonshineQsvDiagnosticReport::decoder_created offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, decoder_accepted) == 80, "MoonshineQsvDiagnosticReport::decoder_accepted offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, decoded_texture_available) == 84, "MoonshineQsvDiagnosticReport::decoded_texture_available offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, decoder_loopback_passed) == 88, "MoonshineQsvDiagnosticReport::decoder_loopback_passed offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, legacy_mfx_fallback_used) == 92, "MoonshineQsvDiagnosticReport::legacy_mfx_fallback_used offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, last_mfx_status) == 96, "MoonshineQsvDiagnosticReport::last_mfx_status offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, impl_filter_status) == 100, "MoonshineQsvDiagnosticReport::impl_filter_status offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, accel_filter_status) == 104, "MoonshineQsvDiagnosticReport::accel_filter_status offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, last_hresult) == 108, "MoonshineQsvDiagnosticReport::last_hresult offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, adapter_description) == 112, "MoonshineQsvDiagnosticReport::adapter_description offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, vpl_dll_name) == 240, "MoonshineQsvDiagnosticReport::vpl_dll_name offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, first_failed_stage) == 304, "MoonshineQsvDiagnosticReport::first_failed_stage offset mismatch");
static_assert(offsetof(MoonshineQsvDiagnosticReport, reserved) == 368, "MoonshineQsvDiagnosticReport::reserved offset mismatch");

#endif

#endif // MOONSHINE_NATIVE_API_H
