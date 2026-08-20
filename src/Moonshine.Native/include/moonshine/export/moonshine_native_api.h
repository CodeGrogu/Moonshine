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
MOONSHINE_API void MOONSHINE_CONV moonshine_video_destroy(MoonshineDecoderHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_video_submit_frame(MoonshineDecoderHandle handle, const MoonshineFrameDesc* frame);

// ============================================================================
// Sub-5ms WASAPI Low-Latency Audio APIs
// ============================================================================

typedef void* MoonshineAudioHandle;

MOONSHINE_API MoonshineAudioHandle MOONSHINE_CONV moonshine_audio_create_wasapi(uint32_t sample_rate, uint16_t channels, uint16_t is_exclusive);
MOONSHINE_API void MOONSHINE_CONV moonshine_audio_destroy(MoonshineAudioHandle handle);
MOONSHINE_API int MOONSHINE_CONV moonshine_audio_submit_pcm(MoonshineAudioHandle handle, const float* pcm_data, uint32_t sample_count);

#ifdef __cplusplus
}
#endif

#endif // MOONSHINE_NATIVE_API_H
