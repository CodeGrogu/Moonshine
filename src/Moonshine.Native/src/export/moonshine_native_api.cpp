#define MOONSHINE_NATIVE_EXPORTS
#include "moonshine/export/moonshine_native_api.h"
#include <cstring>
#include "moonshine/fec/reed_solomon_simd.hpp"
#include "moonshine/ring_buffer/spsc_ring_buffer.hpp"
#include "moonshine/jitter_buffer/jitter_buffer.hpp"
#include "moonshine/video/video_decoder_interface.hpp"
#include "moonshine/video/dxgi_swapchain.hpp"
#include "moonshine/audio/wasapi_renderer.hpp"
#include "moonshine/audio/wasapi_loopback_capture.hpp"
#include "moonshine/capture/dxgi_desktop_duplicator.hpp"
#include "moonshine/capture/wgc_desktop_capture.hpp"
#include "moonshine/color/hdr_metadata_extractor.hpp"
#include "moonshine/color/d3d_color_converter.hpp"
#include "moonshine/encoder/unified_video_encoder.hpp"
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "moonshine/encoder/amf_video_encoder.hpp"
#include "moonshine/encoder/qsv_video_encoder.hpp"

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

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_fec_get_simd_architecture(void) {
    return static_cast<uint32_t>(fec::ReedSolomonSimd::GetDetectedArchitecture());
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

MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d12(void* hwnd, uint32_t width, uint32_t height, uint32_t codec) {
    auto* dec = new video::D3D12VideoDecoder();
    if (dec->Initialize(hwnd, width, height, static_cast<video::VideoCodec>(codec)) != 0) {
        delete dec;
        return nullptr;
    }
    return static_cast<MoonshineDecoderHandle>(dec);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_video_destroy(MoonshineDecoderHandle handle) {
    if (!handle) return;
    auto* dec = static_cast<video::IVideoDecoder*>(handle);
    delete dec;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_submit_frame(MoonshineDecoderHandle handle, const MoonshineFrameDesc* frame) {
    if (!handle || !frame) return -1;
    auto* dec = static_cast<video::IVideoDecoder*>(handle);
    return dec->SubmitFrame(*frame);
}

// ============================================================================
// Low-Latency DXGI Flip Model Swapchain APIs
// ============================================================================

MOONSHINE_API MoonshineSwapchainHandle MOONSHINE_CONV moonshine_swapchain_create(
    void* hwnd,
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t buffer_count,
    uint8_t is_hdr10
) {
    auto* swapchain = new video::DxgiSwapchain();
    if (swapchain->Initialize(hwnd, d3d11_device, width, height, buffer_count, is_hdr10 != 0) != 0) {
        delete swapchain;
        return nullptr;
    }
    return static_cast<MoonshineSwapchainHandle>(swapchain);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_swapchain_destroy(MoonshineSwapchainHandle handle) {
    if (!handle) return;
    auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
    delete swapchain;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_present(MoonshineSwapchainHandle handle, uint32_t sync_interval, uint32_t flags) {
    if (!handle) return -1;
    auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
    return swapchain->Present(sync_interval, flags);
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_resize(MoonshineSwapchainHandle handle, uint32_t width, uint32_t height) {
    if (!handle) return -1;
    auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
    return swapchain->Resize(width, height);
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr(MoonshineSwapchainHandle handle, uint8_t is_hdr10) {
    if (!handle) return -1;
    auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
    return swapchain->SetHdr(is_hdr10 != 0);
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

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_get_metrics(MoonshineAudioHandle handle, uint64_t* out_frames_rendered, uint32_t* out_underruns) {
    if (!handle) return;
    auto* audio = static_cast<audio::WasapiRenderer*>(handle);
    uint64_t frames = 0;
    uint32_t underruns = 0;
    audio->GetMetrics(frames, underruns);
    if (out_frames_rendered) *out_frames_rendered = frames;
    if (out_underruns) *out_underruns = underruns;
}

// ============================================================================
// WASAPI Master Loopback Audio Capture APIs
// ============================================================================

MOONSHINE_API MoonshineAudioCaptureHandle MOONSHINE_CONV moonshine_audio_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
) {
    auto* capture = new audio::WasapiLoopbackCapture(sample_rate, channels, buffer_duration_ms);
    if (!capture->initialize()) {
        delete capture;
        return nullptr;
    }
    return static_cast<MoonshineAudioCaptureHandle>(capture);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_destroy(
    MoonshineAudioCaptureHandle handle
) {
    if (!handle) return;
    auto* capture = static_cast<audio::WasapiLoopbackCapture*>(handle);
    delete capture;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_float(
    MoonshineAudioCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
) {
    if (!handle || !out_buffer || !out_samples_read || !out_timestamp_qpc) return 0;
    auto* capture = static_cast<audio::WasapiLoopbackCapture*>(handle);
    uint32_t read = 0;
    uint64_t qpc = 0;
    if (!capture->read_samples_float(out_buffer, max_samples, read, qpc)) {
        return 0;
    }
    *out_samples_read = read;
    *out_timestamp_qpc = qpc;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_pcm16(
    MoonshineAudioCaptureHandle handle,
    int16_t* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
) {
    if (!handle || !out_buffer || !out_samples_read || !out_timestamp_qpc) return 0;
    auto* capture = static_cast<audio::WasapiLoopbackCapture*>(handle);
    uint32_t read = 0;
    uint64_t qpc = 0;
    if (!capture->read_samples_pcm16(out_buffer, max_samples, read, qpc)) {
        return 0;
    }
    *out_samples_read = read;
    *out_timestamp_qpc = qpc;
    return 1;
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_get_metrics(
    MoonshineAudioCaptureHandle handle,
    uint64_t* out_frames_captured,
    uint64_t* out_samples_captured,
    uint32_t* out_underruns,
    uint32_t* out_overruns
) {
    if (!handle) return;
    auto* capture = static_cast<audio::WasapiLoopbackCapture*>(handle);
    audio::AudioCaptureMetrics metrics{};
    capture->get_metrics(metrics);
    if (out_frames_captured) *out_frames_captured = metrics.total_frames_captured;
    if (out_samples_captured) *out_samples_captured = metrics.total_samples_captured;
    if (out_underruns) *out_underruns = metrics.underruns;
    if (out_overruns) *out_overruns = metrics.overruns;
}

// ============================================================================
// Zero-Copy Direct3D Desktop Capture APIs
// ============================================================================

MOONSHINE_API MoonshineCaptureHandle MOONSHINE_CONV moonshine_capture_create_dxgi(
    uint32_t adapter_index,
    uint32_t output_index,
    uint32_t* out_width,
    uint32_t* out_height
) {
    auto* cap = new capture::DxgiDesktopDuplicator(adapter_index, output_index);
    if (!cap->initialize()) {
        delete cap;
        return nullptr;
    }
    if (out_width) *out_width = cap->width();
    if (out_height) *out_height = cap->height();
    return static_cast<MoonshineCaptureHandle>(cap);
}

MOONSHINE_API MoonshineCaptureHandle MOONSHINE_CONV moonshine_capture_create_wgc(
    void* hmonitor,
    uint32_t target_fps,
    uint32_t* out_width,
    uint32_t* out_height
) {
    auto* cap = new capture::WgcDesktopCapture(hmonitor, target_fps);
    if (!cap->initialize()) {
        delete cap;
        return nullptr;
    }
    if (out_width) *out_width = cap->width();
    if (out_height) *out_height = cap->height();
    return static_cast<MoonshineCaptureHandle>(cap);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_capture_destroy(MoonshineCaptureHandle handle) {
    if (!handle) return;
    auto* cap = static_cast<capture::IDesktopCapture*>(handle);
    delete cap;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_acquire_frame(
    MoonshineCaptureHandle handle,
    uint32_t timeout_ms,
    MoonshineCaptureFrameDesc* out_frame
) {
    if (!handle || !out_frame) return -1;
    auto* cap = static_cast<capture::IDesktopCapture*>(handle);
    capture::CaptureFrame frame = {};
    if (!cap->acquire_frame(timeout_ms, frame)) {
        return 0; // Timeout or no new frame
    }
    out_frame->texture_handle = frame.texture_handle;
    out_frame->width = frame.width;
    out_frame->height = frame.height;
    out_frame->format = frame.format;
    out_frame->timestamp_qpc = frame.timestamp_qpc;
    out_frame->accumulated_frames = frame.accumulated_frames;
    out_frame->cursor_visible = frame.cursor_visible ? 1 : 0;
    return 1; // Success
}

MOONSHINE_API void MOONSHINE_CONV moonshine_capture_release_frame(MoonshineCaptureHandle handle) {
    if (!handle) return;
    auto* cap = static_cast<capture::IDesktopCapture*>(handle);
    cap->release_frame();
}

// ============================================================================
// HDR10 Metadata Extraction & Real-Time Color Space Conversion APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_extract_metadata(
    void* hmonitor,
    MoonshineHdr10Metadata* out_metadata
) {
    if (!out_metadata) return -1;
    color::Hdr10Metadata meta = {};
    if (!color::HdrMetadataExtractor::extract_display_metadata(hmonitor, meta)) {
        return 0;
    }
    std::memcpy(out_metadata->red_primary, meta.red_primary, sizeof(meta.red_primary));
    std::memcpy(out_metadata->green_primary, meta.green_primary, sizeof(meta.green_primary));
    std::memcpy(out_metadata->blue_primary, meta.blue_primary, sizeof(meta.blue_primary));
    std::memcpy(out_metadata->white_point, meta.white_point, sizeof(meta.white_point));
    out_metadata->max_mastering_luminance = meta.max_mastering_luminance;
    out_metadata->min_mastering_luminance = meta.min_mastering_luminance;
    out_metadata->max_content_light_level = meta.max_content_light_level;
    out_metadata->max_frame_average_light_level = meta.max_frame_average_light_level;
    out_metadata->hdr_enabled = meta.hdr_enabled;
    out_metadata->color_space = meta.color_space;
    out_metadata->reserved[0] = 0;
    out_metadata->reserved[1] = 0;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_parse_capabilities(
    uint32_t color_space_dxgi,
    MoonshineHdr10Metadata* out_metadata
) {
    if (!out_metadata) return -1;
    color::Hdr10Metadata meta = {};
    if (!color::HdrMetadataExtractor::parse_hdr_capabilities(color_space_dxgi, meta)) {
        return 0;
    }
    std::memcpy(out_metadata->red_primary, meta.red_primary, sizeof(meta.red_primary));
    std::memcpy(out_metadata->green_primary, meta.green_primary, sizeof(meta.green_primary));
    std::memcpy(out_metadata->blue_primary, meta.blue_primary, sizeof(meta.blue_primary));
    std::memcpy(out_metadata->white_point, meta.white_point, sizeof(meta.white_point));
    out_metadata->max_mastering_luminance = meta.max_mastering_luminance;
    out_metadata->min_mastering_luminance = meta.min_mastering_luminance;
    out_metadata->max_content_light_level = meta.max_content_light_level;
    out_metadata->max_frame_average_light_level = meta.max_frame_average_light_level;
    out_metadata->hdr_enabled = meta.hdr_enabled;
    out_metadata->color_space = meta.color_space;
    out_metadata->reserved[0] = 0;
    out_metadata->reserved[1] = 0;
    return 1;
}

MOONSHINE_API MoonshineColorConverterHandle MOONSHINE_CONV moonshine_color_converter_create(
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t in_format,
    uint32_t out_format
) {
    auto* conv = new color::D3DColorConverter(width, height, in_format, out_format);
    if (!conv->initialize(d3d11_device)) {
        delete conv;
        return nullptr;
    }
    return static_cast<MoonshineColorConverterHandle>(conv);
}

MOONSHINE_API int MOONSHINE_CONV moonshine_color_converter_convert(
    MoonshineColorConverterHandle handle,
    void* in_texture,
    void* out_texture
) {
    if (!handle || !in_texture || !out_texture) return -1;
    auto* conv = static_cast<color::D3DColorConverter*>(handle);
    return conv->convert(in_texture, out_texture) ? 1 : 0;
}

MOONSHINE_API void MOONSHINE_CONV moonshine_color_converter_destroy(
    MoonshineColorConverterHandle handle
) {
    if (!handle) return;
    auto* conv = static_cast<color::D3DColorConverter*>(handle);
    delete conv;
}

// ============================================================================
// Multi-Vendor Hardware Video Encoder APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_query_caps(
    uint32_t vendor,
    void* d3d_device,
    MoonshineEncoderCaps* out_caps
) {
    if (!out_caps) return 0;
    encoder::EncoderCaps caps{};
    bool res = encoder::UnifiedVideoEncoder::query_capabilities(
        static_cast<encoder::EncoderVendor>(vendor),
        d3d_device,
        caps
    );
    if (!res) return 0;

    out_caps->supported_codecs_mask = caps.supported_codecs_mask;
    out_caps->max_width = caps.max_width;
    out_caps->max_height = caps.max_height;
    out_caps->max_fps = caps.max_fps;
    out_caps->supports_10bit = caps.supports_10bit;
    out_caps->supports_lossless = caps.supports_lossless;
    out_caps->supports_smart_idr = caps.supports_smart_idr;
    out_caps->vendor_id = caps.vendor_id;
    out_caps->min_bitrate_kbps = caps.min_bitrate_kbps;
    out_caps->max_bitrate_kbps = caps.max_bitrate_kbps;
    out_caps->reserved = 0;
    return 1;
}

MOONSHINE_API MoonshineEncoderHandle MOONSHINE_CONV moonshine_encoder_create(
    uint32_t vendor,
    void* d3d_device,
    const MoonshineEncoderConfig* config
) {
    if (!config) return nullptr;

    auto encoder = std::make_unique<encoder::UnifiedVideoEncoder>(
        static_cast<encoder::EncoderVendor>(vendor)
    );

    encoder::EncoderConfig cfg{};
    cfg.width = config->width;
    cfg.height = config->height;
    cfg.fps = config->fps;
    cfg.bitrate_kbps = config->bitrate_kbps;
    cfg.peak_bitrate_kbps = config->peak_bitrate_kbps;
    cfg.codec = config->codec;
    cfg.rc_mode = config->rc_mode;
    cfg.gop_length = config->gop_length;
    cfg.enable_intra_refresh = config->enable_intra_refresh;
    cfg.enable_filler_data = config->enable_filler_data;

    if (!encoder->initialize(d3d_device, cfg)) {
        return nullptr;
    }

    return static_cast<MoonshineEncoderHandle>(encoder.release());
}

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_encode_frame(
    MoonshineEncoderHandle handle,
    void* d3d_texture,
    int force_idr,
    MoonshineEncodedPacketDesc* out_desc,
    uint8_t* out_buffer,
    uint32_t max_buffer_size,
    uint32_t* out_size
) {
    if (!handle || !out_desc || !out_buffer || !out_size) return 0;
    auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);

    encoder::EncodedPacketDesc desc{};
    uint32_t written = 0;
    bool res = encoder->encode_frame(
        d3d_texture,
        force_idr != 0,
        desc,
        out_buffer,
        max_buffer_size,
        written
    );

    if (!res) return 0;

    out_desc->frame_index = desc.frame_index;
    out_desc->timestamp_qpc = desc.timestamp_qpc;
    out_desc->payload_size = desc.payload_size;
    out_desc->is_keyframe = desc.is_keyframe;
    out_desc->is_header_packet = desc.is_header_packet;
    out_desc->temporal_id = desc.temporal_id;
    out_desc->reserved = 0;
    *out_size = written;

    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_reconfigure(
    MoonshineEncoderHandle handle,
    const MoonshineEncoderConfig* new_config
) {
    if (!handle || !new_config) return 0;
    auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);

    encoder::EncoderConfig cfg{};
    cfg.width = new_config->width;
    cfg.height = new_config->height;
    cfg.fps = new_config->fps;
    cfg.bitrate_kbps = new_config->bitrate_kbps;
    cfg.peak_bitrate_kbps = new_config->peak_bitrate_kbps;
    cfg.codec = new_config->codec;
    cfg.rc_mode = new_config->rc_mode;
    cfg.gop_length = new_config->gop_length;
    cfg.enable_intra_refresh = new_config->enable_intra_refresh;
    cfg.enable_filler_data = new_config->enable_filler_data;

    return encoder->reconfigure(cfg) ? 1 : 0;
}

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_request_keyframe(
    MoonshineEncoderHandle handle
) {
    if (!handle) return;
    auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
    encoder->request_keyframe();
}

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_destroy(
    MoonshineEncoderHandle handle
) {
    if (!handle) return;
    auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
    delete encoder;
}

// ============================================================================
// NVIDIA NVENC Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    if (!out_supported) return 0;
    bool supported = encoder::NvencVideoEncoder::query_codec_support(
        static_cast<encoder::VideoCodec>(codec)
    );
    *out_supported = supported ? 1 : 0;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t tuning
) {
    if (!handle) return 0;
    (void)preset;
    (void)tuning;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t period,
    uint32_t count
) {
    if (!handle) return 0;
    (void)enable;
    (void)period;
    (void)count;
    return 1;
}

// ============================================================================
// AMD AMF Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    if (!out_supported) return 0;
    bool supported = encoder::AmfVideoEncoder::query_codec_support(
        static_cast<encoder::VideoCodec>(codec)
    );
    *out_supported = supported ? 1 : 0;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t usage
) {
    if (!handle) return 0;
    (void)preset;
    (void)usage;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t mbs_per_slot
) {
    if (!handle) return 0;
    (void)enable;
    (void)mbs_per_slot;
    return 1;
}

// ============================================================================
// Intel QuickSync / oneVPL Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    if (!out_supported) return 0;
    bool supported = encoder::QsvVideoEncoder::query_codec_support(
        static_cast<encoder::VideoCodec>(codec)
    );
    *out_supported = supported ? 1 : 0;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t target_usage,
    int low_power_vdenc
) {
    if (!handle) return 0;
    (void)target_usage;
    (void)low_power_vdenc;
    return 1;
}

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t cycle_size,
    int32_t qp_delta
) {
    if (!handle) return 0;
    (void)enable;
    (void)cycle_size;
    (void)qp_delta;
    return 1;
}

} // extern "C"
