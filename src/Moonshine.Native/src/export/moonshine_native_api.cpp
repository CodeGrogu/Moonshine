#define MOONSHINE_NATIVE_EXPORTS
#include "moonshine/export/moonshine_native_api.h"
#include "moonshine/fec/reed_solomon_simd.hpp"
#include "moonshine/ring_buffer/spsc_ring_buffer.hpp"
#include "moonshine/jitter_buffer/jitter_buffer.hpp"
#include "moonshine/video/video_decoder_interface.hpp"
#include "moonshine/audio/wasapi_renderer.hpp"

using namespace moonshine;

extern "C" {

// ============================================================================
// SIMD FEC APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_fec_recover_simd(
    uint8_t** shards,
    int shard_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
) {
    if (!shards || shard_count <= 0 || shard_size <= 0) return -1;
    if (!erased_indices && erased_count > 0) return -1;
    if (erased_count == 0) return 0;

    static fec::ReedSolomonSimd fec_engine;
    return fec_engine.Reconstruct(shards, shard_count, shard_size, erased_indices, erased_count);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_vector_xor(
    uint8_t* dest,
    const uint8_t* src,
    size_t length
) {
    if (!dest || !src || length == 0) return;
    fec::ReedSolomonSimd::VectorXor(dest, src, length);
}

// ============================================================================
// Lock-Free SPSC Queue Management APIs
// ============================================================================

MOONSHINE_API MoonshineRingBufferHandle MOONSHINE_CONV moonshine_spsc_create(size_t capacity) {
    auto* ring = new ring_buffer::SpscRingBuffer<MoonshinePacketDesc>(capacity);
    return static_cast<MoonshineRingBufferHandle>(ring);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_spsc_destroy(MoonshineRingBufferHandle handle) {
    if (!handle) return;
    auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
    delete ring;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_enqueue(MoonshineRingBufferHandle handle, const MoonshinePacketDesc* packet) {
    if (!handle || !packet) return 0;
    auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
    return ring->TryEnqueue(*packet) ? 1 : 0;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_dequeue(MoonshineRingBufferHandle handle, MoonshinePacketDesc* packet) {
    if (!handle || !packet) return 0;
    auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
    return ring->TryDequeue(*packet) ? 1 : 0;
}

MOONSHINE_API size_t MOONSHINE_CONV moonshine_spsc_size(MoonshineRingBufferHandle handle) {
    if (!handle) return 0;
    auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
    return ring->Size();
}

// ============================================================================
// Sub-Millisecond Jitter Buffer APIs
// ============================================================================

MOONSHINE_API MoonshineJitterBufferHandle MOONSHINE_CONV moonshine_jitter_create(size_t max_frames) {
    auto* jitter = new jitter::JitterBuffer(max_frames);
    return static_cast<MoonshineJitterBufferHandle>(jitter);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_jitter_destroy(MoonshineJitterBufferHandle handle) {
    if (!handle) return;
    auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
    delete jitter;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_push_packet(MoonshineJitterBufferHandle handle, const MoonshinePacketDesc* packet) {
    if (!handle || !packet) return -1;
    auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
    return jitter->PushPacket(*packet);
}

MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_pop_frame(MoonshineJitterBufferHandle handle, MoonshineFrameDesc* out_frame) {
    if (!handle || !out_frame) return 0;
    auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
    return jitter->PopFrame(*out_frame);
}

// ============================================================================
// Hardware Video Decoder APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_video_query_caps(MoonshineDecoderCaps* out_caps) {
    if (!out_caps) return -1;
    video::D3D11VideoDecoder::QueryCaps(*out_caps);
    return 0;
}

MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d11(void* hwnd, uint32_t width, uint32_t height, uint32_t codec) {
    auto* dec = new video::D3D11VideoDecoder();
    if (dec->Initialize(hwnd, width, height, static_cast<video::VideoCodec>(codec)) != 0) {
        delete dec;
        return nullptr;
    }
    return static_cast<MoonshineDecoderHandle>(dec);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_video_destroy(MoonshineDecoderHandle handle) {
    if (!handle) return;
    auto* dec = static_cast<video::D3D11VideoDecoder*>(handle);
    delete dec;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_submit_frame(MoonshineDecoderHandle handle, const MoonshineFrameDesc* frame) {
    if (!handle || !frame) return -1;
    auto* dec = static_cast<video::D3D11VideoDecoder*>(handle);
    return dec->SubmitFrame(*frame);
}

// ============================================================================
// Audio Subsystem APIs
// ============================================================================

MOONSHINE_API MoonshineAudioHandle MOONSHINE_CONV moonshine_audio_create_wasapi(uint32_t sample_rate, uint16_t channels, uint16_t is_exclusive) {
    auto* audio = new audio::WasapiRenderer(sample_rate, channels, is_exclusive != 0);
    if (audio->Initialize() != 0) {
        delete audio;
        return nullptr;
    }
    return static_cast<MoonshineAudioHandle>(audio);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_destroy(MoonshineAudioHandle handle) {
    if (!handle) return;
    auto* audio = static_cast<audio::WasapiRenderer*>(handle);
    delete audio;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_submit_pcm(MoonshineAudioHandle handle, const float* pcm_data, uint32_t sample_count) {
    if (!handle || !pcm_data) return -1;
    auto* audio = static_cast<audio::WasapiRenderer*>(handle);
    return audio->SubmitPcm(pcm_data, sample_count);
}

} // extern "C"
