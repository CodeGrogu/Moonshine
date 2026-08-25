#define MOONSHINE_NATIVE_EXPORTS
#include "moonshine/export/moonshine_native_api.h"
#include <cstring>
#include <vector>
#include <algorithm>
#include <cmath>
#if defined(_WIN32)
#include <dxgi1_6.h>
#endif
#include "moonshine/fec/reed_solomon_simd.hpp"
#include "moonshine/ring_buffer/spsc_ring_buffer.hpp"
#include "moonshine/jitter_buffer/jitter_buffer.hpp"
#include "moonshine/video/video_decoder_interface.hpp"
#include "moonshine/video/dxgi_swapchain.hpp"
#include "moonshine/audio/wasapi_renderer.hpp"
#include "moonshine/audio/wasapi_loopback_capture.hpp"
#include "moonshine/audio/wasapi_mic_capture.hpp"
#include "moonshine/audio/opus_audio_encoder.hpp"
#include "moonshine/audio/opus_audio_decoder.hpp"
#include "moonshine/audio/mic_audio_sink.hpp"
#include "moonshine/audio/virtual_audio_driver.hpp"
#include "moonshine/audio/virtual_audio_ipc.hpp"
#include "moonshine/capture/dxgi_desktop_duplicator.hpp"
#include "moonshine/capture/wgc_desktop_capture.hpp"
#include "moonshine/color/hdr_metadata_extractor.hpp"
#include "moonshine/color/d3d_color_converter.hpp"
#include "moonshine/encoder/unified_video_encoder.hpp"
#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include "moonshine/encoder/amf_video_encoder.hpp"
#include "moonshine/encoder/qsv_video_encoder.hpp"
#include "encoder/qsv/qsv_api.hpp"
#include "encoder/qsv/qsv_session.hpp"
#include "encoder/qsv/qsv_diagnostic.hpp"
#include "moonshine/input/windows_input_injector.h"

#include <unordered_map>
#include <shared_mutex>
#include <memory>

using namespace moonshine;

namespace {
    // Thread-safe handle store using shared_ptr to prevent use-after-free.
    // acquire() returns a shared_ptr that keeps the object alive for the
    // duration of the caller's operation, even if release() is called
    // concurrently by another thread. The actual deallocation is deferred
    // until the last shared_ptr copy goes out of scope.
    template <typename T>
    class SafeHandleStore {
    public:
        void register_handle(T* handle) {
            if (!handle) return;
            std::unique_lock<std::shared_mutex> lock(_mutex);
            _handles.emplace(handle, std::shared_ptr<T>(handle));
        }

        // Returns a shared_ptr that keeps the handle alive until the caller
        // drops the returned guard. Returns nullptr if the handle has been
        // released or was never registered.
        std::shared_ptr<T> acquire(T* handle) {
            if (!handle) return nullptr;
            std::shared_lock<std::shared_mutex> lock(_mutex);
            auto it = _handles.find(handle);
            if (it == _handles.end()) return nullptr;
            return it->second;
        }

        // Removes the handle from the store. The actual delete is deferred
        // until any in-flight acquire() guards go out of scope.
        void release(T* handle) {
            if (!handle) return;
            std::unique_lock<std::shared_mutex> lock(_mutex);
            _handles.erase(handle);
        }

    private:
        mutable std::shared_mutex _mutex;
        std::unordered_map<T*, std::shared_ptr<T>> _handles;
    };

    SafeHandleStore<audio::OpusAudioEncoder> g_encoder_store;
    SafeHandleStore<audio::OpusAudioDecoder> g_decoder_store;
    SafeHandleStore<audio::WasapiLoopbackCapture> g_capture_store;
    SafeHandleStore<audio::WasapiMicCapture> g_mic_capture_store;
    SafeHandleStore<audio::WasapiRenderer> g_renderer_store;
}

static_assert(sizeof(MoonshinePacketDesc) == 32, "MoonshinePacketDesc must be exactly 32 bytes");
static_assert(alignof(MoonshinePacketDesc) == 1, "MoonshinePacketDesc packed alignment is 1");
static_assert(offsetof(MoonshinePacketDesc, sequence_number) == 0, "sequence_number offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, frame_index) == 4, "frame_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, packet_index) == 8, "packet_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, total_packets) == 10, "total_packets offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, payload_size) == 12, "payload_size offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, packet_type) == 14, "packet_type offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, flags) == 15, "flags offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, buffer_slot_index) == 16, "buffer_slot_index offset mismatch");
static_assert(offsetof(MoonshinePacketDesc, payload_ptr) == 24, "payload_ptr offset mismatch");

static_assert(sizeof(MoonshineDisplayModeDesc) == 32, "MoonshineDisplayModeDesc must be exactly 32 bytes");
static_assert(alignof(MoonshineDisplayModeDesc) == 1, "MoonshineDisplayModeDesc packed alignment is 1");
static_assert(offsetof(MoonshineDisplayModeDesc, width) == 0, "width offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, height) == 4, "height offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, refresh_rate_num) == 8, "refresh_rate_num offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, refresh_rate_den) == 12, "refresh_rate_den offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, format) == 16, "format offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, scaling) == 20, "scaling offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, scanline_ordering) == 24, "scanline_ordering offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, is_hdr) == 28, "is_hdr offset mismatch");
static_assert(offsetof(MoonshineDisplayModeDesc, reserved) == 29, "reserved offset mismatch");

static_assert(sizeof(MoonshineDisplayExtendedInfo) == 152, "MoonshineDisplayExtendedInfo must be exactly 152 bytes");
static_assert(alignof(MoonshineDisplayExtendedInfo) == 1, "MoonshineDisplayExtendedInfo packed alignment is 1");
static_assert(offsetof(MoonshineDisplayExtendedInfo, display_index) == 0, "display_index offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, adapter_index) == 4, "adapter_index offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, monitor_handle) == 8, "monitor_handle offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_left) == 16, "desktop_left offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_top) == 20, "desktop_top offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_right) == 24, "desktop_right offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, desktop_bottom) == 28, "desktop_bottom offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, dpi_scale) == 32, "dpi_scale offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_primary) == 36, "is_primary offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_attached_to_desktop) == 37, "is_attached_to_desktop offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, is_hdr) == 38, "is_hdr offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, bits_per_color) == 39, "bits_per_color offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, device_name) == 40, "device_name offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, friendly_name) == 72, "friendly_name offset mismatch");
static_assert(offsetof(MoonshineDisplayExtendedInfo, reserved) == 136, "reserved offset mismatch");

extern "C" {

// ============================================================================
// SIMD FEC APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_fec_encode_simd(
    const uint8_t* const* data_shards,
    int data_shards_count,
    uint8_t** parity_shards,
    int parity_shards_count,
    int shard_size
) {
    try {
        if (!data_shards || !parity_shards || shard_size <= 0) return -1;
        if (data_shards_count <= 0 || data_shards_count > fec::kMaxDataShards) return -1;
        if (parity_shards_count <= 0 || parity_shards_count > fec::kMaxParityShards) return -1;
        if ((data_shards_count + parity_shards_count) > 255) return -1;

        static fec::ReedSolomonSimd fec_engine;
        return fec_engine.Encode(data_shards, data_shards_count, parity_shards, parity_shards_count, shard_size);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_fec_reconstruct_simd(
    uint8_t** shards,
    int data_shards_count,
    int parity_shards_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
) {
    try {
        if (!shards || shard_size <= 0) return -1;
        if (data_shards_count <= 0 || data_shards_count > fec::kMaxDataShards) return -1;
        if (parity_shards_count <= 0 || parity_shards_count > fec::kMaxParityShards) return -1;
        if ((data_shards_count + parity_shards_count) > 255) return -1;
        if (!erased_indices && erased_count > 0) return -1;
        if (erased_count == 0) return 0;
        if (erased_count > parity_shards_count) return -2;

        static fec::ReedSolomonSimd fec_engine;
        return fec_engine.Reconstruct(shards, data_shards_count, parity_shards_count, shard_size, erased_indices, erased_count);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_fec_recover_simd(
    uint8_t** shards,
    int shard_count,
    int shard_size,
    const int* erased_indices,
    int erased_count
) {
    try {
        if (!shards || shard_count <= 0 || shard_size <= 0) return -1;
        if (!erased_indices && erased_count > 0) return -1;
        if (erased_count == 0) return 0;

        static fec::ReedSolomonSimd fec_engine;
        return fec_engine.Reconstruct(shards, shard_count, shard_size, erased_indices, erased_count);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_vector_xor(
    uint8_t* dest,
    const uint8_t* src,
    size_t length
) {
    try {
        if (!dest || !src || length == 0) return;
        fec::ReedSolomonSimd::VectorXor(dest, src, length);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_fec_get_simd_architecture(void) {
    try {
        return static_cast<uint32_t>(fec::ReedSolomonSimd::GetDetectedArchitecture());
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

// ============================================================================
// Lock-Free SPSC Queue Management APIs
// ============================================================================

MOONSHINE_API MoonshineRingBufferHandle MOONSHINE_CONV moonshine_spsc_create(size_t capacity) {
    try {
        auto* ring = new ring_buffer::SpscRingBuffer<MoonshinePacketDesc>(capacity);
        return static_cast<MoonshineRingBufferHandle>(ring);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_spsc_destroy(MoonshineRingBufferHandle handle) {
    try {
        if (!handle) return;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
        delete ring;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_enqueue(MoonshineRingBufferHandle handle, const MoonshinePacketDesc* packet) {
    try {
        if (!handle || !packet) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
        return ring->TryEnqueue(*packet) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_spsc_dequeue(MoonshineRingBufferHandle handle, MoonshinePacketDesc* packet) {
    try {
        if (!handle || !packet) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
        return ring->TryDequeue(*packet) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API size_t MOONSHINE_CONV moonshine_spsc_size(MoonshineRingBufferHandle handle) {
    try {
        if (!handle) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<MoonshinePacketDesc>*>(handle);
        return ring->Size();
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

// ============================================================================
// Lock-Free SPSC Slot Return Queue Management APIs
// ============================================================================

MOONSHINE_API MoonshineRingBufferHandle MOONSHINE_CONV moonshine_slot_return_create(size_t capacity) {
    try {
        auto* ring = new ring_buffer::SpscRingBuffer<int32_t>(capacity);
        return static_cast<MoonshineRingBufferHandle>(ring);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_slot_return_destroy(MoonshineRingBufferHandle handle) {
    try {
        if (!handle) return;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<int32_t>*>(handle);
        delete ring;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_slot_return_enqueue(MoonshineRingBufferHandle handle, int32_t slot_index) {
    try {
        if (!handle) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<int32_t>*>(handle);
        return ring->TryEnqueue(slot_index) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_slot_return_dequeue(MoonshineRingBufferHandle handle, int32_t* out_slot_index) {
    try {
        if (!handle || !out_slot_index) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<int32_t>*>(handle);
        return ring->TryDequeue(*out_slot_index) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API size_t MOONSHINE_CONV moonshine_slot_return_size(MoonshineRingBufferHandle handle) {
    try {
        if (!handle) return 0;
        auto* ring = static_cast<ring_buffer::SpscRingBuffer<int32_t>*>(handle);
        return ring->Size();
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

// ============================================================================
// Sub-Millisecond Jitter Buffer APIs
// ============================================================================

MOONSHINE_API MoonshineJitterBufferHandle MOONSHINE_CONV moonshine_jitter_create(size_t max_frames) {
    try {
        auto* jitter = new jitter::JitterBuffer(max_frames);
        return static_cast<MoonshineJitterBufferHandle>(jitter);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_jitter_destroy(MoonshineJitterBufferHandle handle) {
    try {
        if (!handle) return;
        auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
        delete jitter;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_push_packet(MoonshineJitterBufferHandle handle, const MoonshinePacketDesc* packet) {
    try {
        if (!handle || !packet) return -1;
        auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
        return jitter->PushPacket(*packet);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_jitter_pop_frame(MoonshineJitterBufferHandle handle, MoonshineFrameDesc* out_frame) {
    try {
        if (!handle || !out_frame) return 0;
        auto* jitter = static_cast<jitter::JitterBuffer*>(handle);
        return jitter->PopFrame(*out_frame);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// Hardware Video Decoder APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_video_query_caps(MoonshineDecoderCaps* out_caps) {
    try {
        if (!out_caps) return -1;
        video::D3D11VideoDecoder::QueryCaps(*out_caps);
        return 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d11(void* hwnd, uint32_t width, uint32_t height, uint32_t codec) {
    try {
        auto* dec = new video::D3D11VideoDecoder();
        if (dec->Initialize(hwnd, width, height, static_cast<video::VideoCodec>(codec)) != 0) {
            delete dec;
            return nullptr;
        }
        return static_cast<MoonshineDecoderHandle>(dec);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API MoonshineDecoderHandle MOONSHINE_CONV moonshine_video_create_d3d12(void* hwnd, uint32_t width, uint32_t height, uint32_t codec) {
    try {
        auto* dec = new video::D3D12VideoDecoder();
        if (dec->Initialize(hwnd, width, height, static_cast<video::VideoCodec>(codec)) != 0) {
            delete dec;
            return nullptr;
        }
        return static_cast<MoonshineDecoderHandle>(dec);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_video_destroy(MoonshineDecoderHandle handle) {
    try {
        if (!handle) return;
        auto* dec = static_cast<video::IVideoDecoder*>(handle);
        delete dec;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_submit_frame(MoonshineDecoderHandle handle, const MoonshineFrameDesc* frame) {
    try {
        if (!handle || !frame) return -1;
        auto* dec = static_cast<video::IVideoDecoder*>(handle);
        return dec->SubmitFrame(*frame);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void* MOONSHINE_CONV moonshine_video_get_texture(MoonshineDecoderHandle handle) {
    try {
        if (!handle) return nullptr;
        auto* dec = static_cast<video::IVideoDecoder*>(handle);
        return dec->GetTextureHandle();
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_reset(MoonshineDecoderHandle handle, uint32_t width, uint32_t height) {
    try {
        if (!handle) return -1;
        auto* dec = static_cast<video::IVideoDecoder*>(handle);
        return dec->Reset(width, height);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_get_dimensions(MoonshineDecoderHandle handle, uint32_t* out_width, uint32_t* out_height) {
    try {
        if (!handle || !out_width || !out_height) return -1;
        auto* dec = static_cast<video::IVideoDecoder*>(handle);
        return dec->GetDimensions(*out_width, *out_height);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
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
    try {
        auto* swapchain = new video::DxgiSwapchain();
        if (swapchain->Initialize(hwnd, d3d11_device, width, height, buffer_count, is_hdr10 != 0) != 0) {
            delete swapchain;
            return nullptr;
        }
        return static_cast<MoonshineSwapchainHandle>(swapchain);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_swapchain_destroy(MoonshineSwapchainHandle handle) {
    try {
        if (!handle) return;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        delete swapchain;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_present(MoonshineSwapchainHandle handle, uint32_t sync_interval, uint32_t flags) {
    try {
        if (!handle) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->Present(sync_interval, flags);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_present_texture(MoonshineSwapchainHandle handle, void* texture_handle, uint32_t sync_interval, uint32_t flags) {
    try {
        if (!handle) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->PresentTexture(texture_handle, sync_interval, flags);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_resize(MoonshineSwapchainHandle handle, uint32_t width, uint32_t height) {
    try {
        if (!handle) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->Resize(width, height);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr(MoonshineSwapchainHandle handle, uint8_t is_hdr10) {
    try {
        if (!handle) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->SetHdr(is_hdr10 != 0);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_set_hdr_metadata(MoonshineSwapchainHandle handle, const MoonshineHdr10Metadata* metadata) {
    try {
        if (!handle || !metadata) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->SetHdrMetadata(metadata);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_get_metrics(MoonshineSwapchainHandle handle, MoonshineSwapchainMetrics* out_metrics) {
    try {
        if (!handle || !out_metrics) return -1;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        swapchain->GetMetrics(*out_metrics);
        return 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_swapchain_is_tearing_supported(MoonshineSwapchainHandle handle) {
    try {
        if (!handle) return 0;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->IsTearingSupported() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void* MOONSHINE_CONV moonshine_swapchain_get_waitable_object(MoonshineSwapchainHandle handle) {
    try {
        if (!handle) return nullptr;
        auto* swapchain = static_cast<video::DxgiSwapchain*>(handle);
        return swapchain->GetFrameLatencyWaitableObject();
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

// ============================================================================
// Audio Subsystem APIs
// ============================================================================

MOONSHINE_API MoonshineAudioHandle MOONSHINE_CONV moonshine_audio_create_wasapi(uint32_t sample_rate, uint16_t channels, uint16_t is_exclusive) {
    try {
        auto* audio = new audio::WasapiRenderer(sample_rate, channels, is_exclusive != 0);
        if (audio->Initialize() != 0) {
            delete audio;
            return nullptr;
        }
        g_renderer_store.register_handle(audio);
        return static_cast<MoonshineAudioHandle>(audio);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_destroy(MoonshineAudioHandle handle) {
    try {
        if (!handle) return;
        auto* audio = static_cast<audio::WasapiRenderer*>(handle);
        g_renderer_store.release(audio);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_submit_pcm(MoonshineAudioHandle handle, const float* pcm_data, uint32_t sample_count) {
    try {
        if (!handle || !pcm_data || sample_count == 0) return -1;
        auto guard = g_renderer_store.acquire(static_cast<audio::WasapiRenderer*>(handle));
        if (!guard) return -1;
        return guard->SubmitPcm(pcm_data, sample_count);
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_get_metrics(MoonshineAudioHandle handle, uint64_t* out_frames_rendered, uint32_t* out_underruns) {
    try {
        if (!handle) return;
        auto guard = g_renderer_store.acquire(static_cast<audio::WasapiRenderer*>(handle));
        if (!guard) return;
        uint64_t frames = 0;
        uint32_t underruns = 0;
        guard->GetMetrics(frames, underruns);
        if (out_frames_rendered) *out_frames_rendered = frames;
        if (out_underruns) *out_underruns = underruns;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_recover(MoonshineAudioHandle handle) {
    try {
        if (!handle) return -1;
        auto guard = g_renderer_store.acquire(static_cast<audio::WasapiRenderer*>(handle));
        if (!guard) return -1;
        return guard->Recover();
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// WASAPI Master Loopback Audio Capture APIs
// ============================================================================

MOONSHINE_API MoonshineAudioCaptureHandle MOONSHINE_CONV moonshine_audio_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
) {
    try {
        auto* capture = new audio::WasapiLoopbackCapture(sample_rate, channels, buffer_duration_ms);
        if (!capture->initialize()) {
            delete capture;
            return nullptr;
        }
        g_capture_store.register_handle(capture);
        return static_cast<MoonshineAudioCaptureHandle>(capture);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_destroy(
    MoonshineAudioCaptureHandle handle
) {
    try {
        if (!handle) return;
        auto* capture = static_cast<audio::WasapiLoopbackCapture*>(handle);
        g_capture_store.release(capture);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_float(
    MoonshineAudioCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
) {
    try {
        if (!handle || !out_buffer || !out_samples_read || !out_timestamp_qpc || max_samples == 0) return 0;
        auto guard = g_capture_store.acquire(static_cast<audio::WasapiLoopbackCapture*>(handle));
        if (!guard) return 0;
        uint32_t read = 0;
        uint64_t qpc = 0;
        if (!guard->read_samples_float(out_buffer, max_samples, read, qpc)) {
            return 0;
        }
        *out_samples_read = read;
        *out_timestamp_qpc = qpc;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_read_pcm16(
    MoonshineAudioCaptureHandle handle,
    int16_t* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
) {
    try {
        if (!handle || !out_buffer || !out_samples_read || !out_timestamp_qpc || max_samples == 0) return 0;
        auto guard = g_capture_store.acquire(static_cast<audio::WasapiLoopbackCapture*>(handle));
        if (!guard) return 0;
        uint32_t read = 0;
        uint64_t qpc = 0;
        if (!guard->read_samples_pcm16(out_buffer, max_samples, read, qpc)) {
            return 0;
        }
        *out_samples_read = read;
        *out_timestamp_qpc = qpc;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_capture_get_metrics(
    MoonshineAudioCaptureHandle handle,
    uint64_t* out_frames_captured,
    uint64_t* out_samples_captured,
    uint32_t* out_underruns,
    uint32_t* out_overruns
) {
    try {
        if (!handle) return;
        auto guard = g_capture_store.acquire(static_cast<audio::WasapiLoopbackCapture*>(handle));
        if (!guard) return;
        audio::AudioCaptureMetrics metrics{};
        guard->get_metrics(metrics);
        if (out_frames_captured) *out_frames_captured = metrics.total_frames_captured;
        if (out_samples_captured) *out_samples_captured = metrics.total_samples_captured;
        if (out_underruns) *out_underruns = metrics.underruns;
        if (out_overruns) *out_overruns = metrics.overruns;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_capture_recover(
    MoonshineAudioCaptureHandle handle
) {
    try {
        if (!handle) return 0;
        auto guard = g_capture_store.acquire(static_cast<audio::WasapiLoopbackCapture*>(handle));
        if (!guard) return 0;
        return guard->recover() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}


// ============================================================================
// WASAPI Microphone Audio Capture APIs
// ============================================================================

MOONSHINE_API MoonshineMicCaptureHandle MOONSHINE_CONV moonshine_mic_capture_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t buffer_duration_ms
) {
    try {
        auto* capture = new audio::WasapiMicCapture(sample_rate, channels, buffer_duration_ms);
        if (!capture->initialize()) {
            delete capture;
            return nullptr;
        }
        g_mic_capture_store.register_handle(capture);
        return static_cast<MoonshineMicCaptureHandle>(capture);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_capture_destroy(
    MoonshineMicCaptureHandle handle
) {
    try {
        if (!handle) return;
        auto* capture = static_cast<audio::WasapiMicCapture*>(handle);
        g_mic_capture_store.release(capture);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_read_float(
    MoonshineMicCaptureHandle handle,
    float* out_buffer,
    uint32_t max_samples,
    uint32_t* out_samples_read,
    uint64_t* out_timestamp_qpc
) {
    try {
        if (!handle || !out_buffer || !out_samples_read || !out_timestamp_qpc) return 0;
        auto guard = g_mic_capture_store.acquire(static_cast<audio::WasapiMicCapture*>(handle));
        if (!guard) return 0;
        uint32_t read = 0;
        uint64_t qpc = 0;
        if (!guard->read_samples_float(out_buffer, max_samples, read, qpc)) {
            return 0;
        }
        *out_samples_read = read;
        *out_timestamp_qpc = qpc;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_is_active(
    MoonshineMicCaptureHandle handle
) {
    try {
        if (!handle) return 0;
        auto guard = g_mic_capture_store.acquire(static_cast<audio::WasapiMicCapture*>(handle));
        if (!guard) return 0;
        return guard->is_active() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_capture_recover(
    MoonshineMicCaptureHandle handle
) {
    try {
        if (!handle) return 0;
        auto guard = g_mic_capture_store.acquire(static_cast<audio::WasapiMicCapture*>(handle));
        if (!guard) return 0;
        return guard->recover() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// Low-Latency Multi-Channel Opus Audio Encoder APIs
// ============================================================================

MOONSHINE_API MoonshineOpusEncoderHandle MOONSHINE_CONV moonshine_opus_encoder_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t bitrate,
    uint32_t frame_duration_ms,
    uint32_t complexity,
    int32_t use_vbr
) {
    try {
        audio::OpusEncoderConfig config{};
        config.sample_rate = sample_rate;
        config.channels = channels;
        config.bitrate = bitrate;
        config.frame_duration_ms = frame_duration_ms;
        config.complexity = complexity;
        config.use_vbr = (use_vbr != 0);
        config.application = audio::OpusApplication::RestrictedLowDelay;

        auto* encoder = new audio::OpusAudioEncoder(config);
        if (!encoder->is_initialized()) {
            delete encoder;
            return nullptr;
        }
        g_encoder_store.register_handle(encoder);
        return static_cast<MoonshineOpusEncoderHandle>(encoder);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_destroy(
    MoonshineOpusEncoderHandle handle
) {
    try {
        if (!handle) return;
        auto* encoder = static_cast<audio::OpusAudioEncoder*>(handle);
        g_encoder_store.release(encoder);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_float(
    MoonshineOpusEncoderHandle handle,
    const float* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
) {
    try {
        if (!handle || !pcm_samples || !out_payload || !out_payload_bytes || frame_samples == 0 || max_payload_bytes == 0) {
            if (out_payload_bytes) *out_payload_bytes = 0;
            return 0;
        }
        auto guard = g_encoder_store.acquire(static_cast<audio::OpusAudioEncoder*>(handle));
        if (!guard) {
            *out_payload_bytes = 0;
            return 0;
        }
        uint32_t bytes_written = 0;
        try {
            if (!guard->encode_float(pcm_samples, frame_samples, out_payload, max_payload_bytes, bytes_written)) {
                *out_payload_bytes = 0;
                return 0;
            }
        } catch (...) {
            *out_payload_bytes = 0;
            return 0;
        }
        *out_payload_bytes = bytes_written;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_encode_pcm16(
    MoonshineOpusEncoderHandle handle,
    const int16_t* pcm_samples,
    uint32_t frame_samples,
    uint8_t* out_payload,
    uint32_t max_payload_bytes,
    uint32_t* out_payload_bytes
) {
    try {
        if (!handle || !pcm_samples || !out_payload || !out_payload_bytes || frame_samples == 0 || max_payload_bytes == 0) {
            if (out_payload_bytes) *out_payload_bytes = 0;
            return 0;
        }
        auto guard = g_encoder_store.acquire(static_cast<audio::OpusAudioEncoder*>(handle));
        if (!guard) {
            *out_payload_bytes = 0;
            return 0;
        }
        uint32_t bytes_written = 0;
        try {
            if (!guard->encode_pcm16(pcm_samples, frame_samples, out_payload, max_payload_bytes, bytes_written)) {
                *out_payload_bytes = 0;
                return 0;
            }
        } catch (...) {
            *out_payload_bytes = 0;
            return 0;
        }
        *out_payload_bytes = bytes_written;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_bitrate(
    MoonshineOpusEncoderHandle handle,
    uint32_t bitrate
) {
    try {
        if (!handle) return 0;
        auto guard = g_encoder_store.acquire(static_cast<audio::OpusAudioEncoder*>(handle));
        if (!guard) return 0;
        return guard->set_bitrate(bitrate) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_encoder_set_complexity(
    MoonshineOpusEncoderHandle handle,
    uint32_t complexity
) {
    try {
        if (!handle) return 0;
        auto guard = g_encoder_store.acquire(static_cast<audio::OpusAudioEncoder*>(handle));
        if (!guard) return 0;
        return guard->set_complexity(complexity) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_encoder_get_metrics(
    MoonshineOpusEncoderHandle handle,
    uint64_t* out_frames_encoded,
    uint64_t* out_bytes_encoded,
    double* out_avg_encode_time_us,
    uint32_t* out_bitrate,
    uint32_t* out_streams_count
) {
    try {
        if (!handle) return;
        auto guard = g_encoder_store.acquire(static_cast<audio::OpusAudioEncoder*>(handle));
        if (!guard) return;
        audio::OpusEncoderMetrics metrics{};
        guard->get_metrics(metrics);
        if (out_frames_encoded) *out_frames_encoded = metrics.total_frames_encoded;
        if (out_bytes_encoded) *out_bytes_encoded = metrics.total_bytes_encoded;
        if (out_avg_encode_time_us) *out_avg_encode_time_us = metrics.avg_encode_time_us;
        if (out_bitrate) *out_bitrate = metrics.current_bitrate;
        if (out_streams_count) *out_streams_count = metrics.streams_count;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

// ============================================================================
// Low-Latency Multi-Channel Opus Audio Decoder APIs
// ============================================================================

MOONSHINE_API MoonshineOpusDecoderHandle MOONSHINE_CONV moonshine_opus_decoder_create(
    uint32_t sample_rate,
    uint32_t channels
) {
    try {
        auto* decoder = new audio::OpusAudioDecoder(sample_rate, channels);
        if (!decoder->is_initialized()) {
            delete decoder;
            return nullptr;
        }
        g_decoder_store.register_handle(decoder);
        return static_cast<MoonshineOpusDecoderHandle>(decoder);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_destroy(
    MoonshineOpusDecoderHandle handle
) {
    try {
        if (!handle) return;
        auto* decoder = static_cast<audio::OpusAudioDecoder*>(handle);
        g_decoder_store.release(decoder);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_decoder_decode_float(
    MoonshineOpusDecoderHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    float* out_pcm_samples,
    uint32_t max_samples,
    uint32_t* out_samples_decoded,
    int32_t decode_fec
) {
    try {
        if (!handle || !out_pcm_samples || !out_samples_decoded) return 0;
        auto guard = g_decoder_store.acquire(static_cast<audio::OpusAudioDecoder*>(handle));
        if (!guard) return 0;
        uint32_t decoded = 0;
        if (!guard->decode_float(opus_payload, payload_bytes, out_pcm_samples, max_samples, decoded, decode_fec)) {
            return 0;
        }
        *out_samples_decoded = decoded;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_opus_decoder_decode_pcm16(
    MoonshineOpusDecoderHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_bytes,
    int16_t* out_pcm_samples,
    uint32_t max_samples,
    uint32_t* out_samples_decoded,
    int32_t decode_fec
) {
    try {
        if (!handle || !out_pcm_samples || !out_samples_decoded) return 0;
        auto guard = g_decoder_store.acquire(static_cast<audio::OpusAudioDecoder*>(handle));
        if (!guard) return 0;
        uint32_t decoded = 0;
        if (!guard->decode_pcm16(opus_payload, payload_bytes, out_pcm_samples, max_samples, decoded, decode_fec)) {
            return 0;
        }
        *out_samples_decoded = decoded;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_reset(
    MoonshineOpusDecoderHandle handle
) {
    try {
        if (!handle) return;
        auto guard = g_decoder_store.acquire(static_cast<audio::OpusAudioDecoder*>(handle));
        if (!guard) return;
        guard->reset();
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_opus_decoder_get_metrics(
    MoonshineOpusDecoderHandle handle,
    uint64_t* out_frames_decoded,
    uint64_t* out_samples_decoded,
    uint32_t* out_decode_errors,
    uint32_t* out_concealment_frames,
    double* out_avg_decode_time_us,
    uint32_t* out_streams_count
) {
    try {
        if (!handle) return;
        auto guard = g_decoder_store.acquire(static_cast<audio::OpusAudioDecoder*>(handle));
        if (!guard) return;
        audio::OpusDecoderMetrics metrics{};
        guard->get_metrics(metrics);
        if (out_frames_decoded) *out_frames_decoded = metrics.total_frames_decoded;
        if (out_samples_decoded) *out_samples_decoded = metrics.total_samples_decoded;
        if (out_decode_errors) *out_decode_errors = metrics.decode_errors;
        if (out_concealment_frames) *out_concealment_frames = metrics.concealment_frames;
        if (out_avg_decode_time_us) *out_avg_decode_time_us = metrics.avg_decode_time_us;
        if (out_streams_count) *out_streams_count = metrics.streams_count;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

// ============================================================================
// Low-Latency Client-to-Host Microphone Virtual Audio Sink APIs
// ============================================================================

MOONSHINE_API MoonshineMicSinkHandle MOONSHINE_CONV moonshine_mic_sink_create(
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t target_latency_ms,
    float gain_multiplier,
    float noise_gate_threshold_db,
    uint8_t is_muted
) {
    try {
        audio::MicSinkConfig config{};
        config.sample_rate = sample_rate;
        config.channels = channels;
        config.target_latency_ms = target_latency_ms;
        config.gain_multiplier = gain_multiplier;
        config.noise_gate_threshold_db = noise_gate_threshold_db;
        config.is_muted = (is_muted != 0);

        auto* sink = new audio::MicAudioSink();
        if (!sink->initialize(config)) {
            delete sink;
            return nullptr;
        }
        return static_cast<MoonshineMicSinkHandle>(sink);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_destroy(
    MoonshineMicSinkHandle handle
) {
    try {
        if (handle) {
            auto* sink = static_cast<audio::MicAudioSink*>(handle);
            delete sink;
        }
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_push_opus_packet(
    MoonshineMicSinkHandle handle,
    const uint8_t* opus_payload,
    uint32_t payload_len,
    uint32_t timestamp,
    uint16_t sequence_number
) {
    try {
        if (!handle) return 0;
        auto* sink = static_cast<audio::MicAudioSink*>(handle);
        return sink->push_opus_packet(opus_payload, payload_len, timestamp, sequence_number) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_mic_sink_pull_pcm(
    MoonshineMicSinkHandle handle,
    float* out_pcm,
    uint32_t max_samples,
    uint32_t* out_samples_read
) {
    try {
        if (!handle) return 0;
        auto* sink = static_cast<audio::MicAudioSink*>(handle);
        uint32_t samples_read = 0;
        bool ok = sink->pull_pcm(out_pcm, max_samples, samples_read);
        if (out_samples_read) *out_samples_read = samples_read;
        return ok ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_gain(
    MoonshineMicSinkHandle handle,
    float gain
) {
    try {
        if (!handle) return;
        auto* sink = static_cast<audio::MicAudioSink*>(handle);
        sink->set_gain(gain);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_set_mute(
    MoonshineMicSinkHandle handle,
    uint8_t is_muted
) {
    try {
        if (!handle) return;
        auto* sink = static_cast<audio::MicAudioSink*>(handle);
        sink->set_mute(is_muted != 0);
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_mic_sink_get_metrics(
    MoonshineMicSinkHandle handle,
    uint64_t* out_packets_received,
    uint64_t* out_samples_rendered,
    uint32_t* out_loss_count,
    uint32_t* out_drift_corrections,
    double* out_jitter_ms
) {
    try {
        if (!handle) return;
        auto* sink = static_cast<audio::MicAudioSink*>(handle);
        audio::MicSinkMetrics metrics{};
        sink->get_metrics(metrics);
        if (out_packets_received) *out_packets_received = metrics.total_packets_received;
        if (out_samples_rendered) *out_samples_rendered = metrics.total_samples_rendered;
        if (out_loss_count) *out_loss_count = metrics.loss_count;
        if (out_drift_corrections) *out_drift_corrections = metrics.drift_corrections;
        if (out_jitter_ms) *out_jitter_ms = metrics.current_jitter_ms;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

// ============================================================================
// Dedicated Windows Virtual Audio Driver Controller APIs
// ============================================================================

MOONSHINE_API MoonshineVirtualAudioDriverHandle MOONSHINE_CONV moonshine_virtual_audio_driver_create(void) {
    try {
        auto* controller = new audio::VirtualAudioDriverController();
        if (!controller->Initialize()) {
            delete controller;
            return nullptr;
        }
        return static_cast<MoonshineVirtualAudioDriverHandle>(controller);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_virtual_audio_driver_destroy(
    MoonshineVirtualAudioDriverHandle handle
) {
    try {
        if (!handle) return;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        delete controller;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_is_installed(
    MoonshineVirtualAudioDriverHandle handle
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->IsDriverInstalled() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_status(
    MoonshineVirtualAudioDriverHandle handle,
    MoonshineVirtualAudioDriverStatusC* out_status
) {
    try {
        if (!handle || !out_status) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        audio::VirtualAudioDriverStatus status = controller->GetStatus();
        out_status->is_installed = status.isInstalled ? 1 : 0;
        out_status->is_render_endpoint_present = status.isRenderEndpointPresent ? 1 : 0;
        out_status->is_capture_endpoint_present = status.isCaptureEndpointPresent ? 1 : 0;
        out_status->supported_sample_rates_count = status.supportedSampleRatesCount;
        out_status->supported_channels_count = status.supportedChannelsCount;
        std::snprintf(out_status->driver_version, sizeof(out_status->driver_version), "%s", status.driverVersion);
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_validate_format(
    MoonshineVirtualAudioDriverHandle handle,
    uint32_t sample_rate,
    uint32_t channels,
    uint32_t format_type
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->ValidateFormat(sample_rate, channels, format_type) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_endpoint_names(
    MoonshineVirtualAudioDriverHandle handle,
    char* out_render_name,
    uint32_t render_name_max_len,
    char* out_capture_name,
    uint32_t capture_name_max_len
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        if (out_render_name && render_name_max_len > 0) {
            std::snprintf(out_render_name, render_name_max_len, "%s", controller->GetRenderEndpointName());
        }
        if (out_capture_name && capture_name_max_len > 0) {
            std::snprintf(out_capture_name, capture_name_max_len, "%s", controller->GetCaptureEndpointName());
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_enable_mmcss(
    MoonshineVirtualAudioDriverHandle handle,
    void** out_task_handle
) {
    try {
        if (!handle || !out_task_handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->EnableMmcssScheduling(out_task_handle) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_disable_mmcss(
    MoonshineVirtualAudioDriverHandle handle,
    void* task_handle
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->DisableMmcssScheduling(task_handle) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_get_installation_state(
    MoonshineVirtualAudioDriverHandle handle
) {
    try {
        if (!handle) return static_cast<int>(audio::DriverInstallationState::Error);
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return static_cast<int>(controller->GetInstallationState());
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_install(
    MoonshineVirtualAudioDriverHandle handle,
    const char* inf_path
) {
    try {
        if (!handle || !inf_path) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->InstallDriver(inf_path) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_remove(
    MoonshineVirtualAudioDriverHandle handle
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->RemoveDriver() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_virtual_audio_driver_restart(
    MoonshineVirtualAudioDriverHandle handle
) {
    try {
        if (!handle) return 0;
        auto* controller = static_cast<audio::VirtualAudioDriverController*>(handle);
        return controller->RestartDriver() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// Real-Time Shared Memory IPC Bridge APIs
// ============================================================================

MOONSHINE_API MoonshineAudioIpcBridgeHandle MOONSHINE_CONV moonshine_audio_ipc_bridge_create(
    int is_host_server,
    uint32_t sample_rate,
    uint32_t channels
) {
    try {
        auto* bridge = new audio::VirtualAudioIpcBridge();
        if (!bridge->Initialize(is_host_server != 0, sample_rate, channels)) {
            delete bridge;
            return nullptr;
        }
        return static_cast<MoonshineAudioIpcBridgeHandle>(bridge);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_ipc_bridge_destroy(
    MoonshineAudioIpcBridgeHandle handle
) {
    try {
        if (handle) {
            auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
            delete bridge;
        }
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_is_connected(
    MoonshineAudioIpcBridgeHandle handle
) {
    try {
        if (!handle) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        return bridge->IsConnected() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int64_t MOONSHINE_CONV moonshine_audio_ipc_bridge_write_capture_pcm(
    MoonshineAudioIpcBridgeHandle handle,
    const float* pcm_samples,
    uint32_t sample_count
) {
    try {
        if (!handle || !pcm_samples) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        return static_cast<int64_t>(bridge->WriteCapturePcm(pcm_samples, sample_count));
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int64_t MOONSHINE_CONV moonshine_audio_ipc_bridge_read_render_pcm(
    MoonshineAudioIpcBridgeHandle handle,
    float* out_pcm_samples,
    uint32_t max_samples,
    int wait_event,
    uint32_t timeout_ms
) {
    try {
        if (!handle || !out_pcm_samples) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        return static_cast<int64_t>(bridge->ReadRenderPcm(out_pcm_samples, max_samples, wait_event != 0, timeout_ms));
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_wait_render_event(
    MoonshineAudioIpcBridgeHandle handle,
    uint32_t timeout_ms
) {
    try {
        if (!handle) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        return bridge->WaitRenderEvent(timeout_ms) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_get_metrics(
    MoonshineAudioIpcBridgeHandle handle,
    MoonshineAudioIpcMetricsC* out_metrics
) {
    try {
        if (!handle || !out_metrics) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        audio::VirtualAudioIpcMetrics metrics = bridge->GetMetrics();
        out_metrics->render_packets_read = metrics.renderPacketsRead;
        out_metrics->render_underruns = metrics.renderUnderruns;
        out_metrics->render_overruns = metrics.renderOverruns;
        out_metrics->capture_packets_written = metrics.capturePacketsWritten;
        out_metrics->capture_underruns = metrics.captureUnderruns;
        out_metrics->capture_overruns = metrics.captureOverruns;
        out_metrics->sample_rate = metrics.sampleRate;
        out_metrics->channels = metrics.channels;
        out_metrics->is_connected = metrics.isConnected;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_audio_ipc_bridge_enable_mmcss(
    MoonshineAudioIpcBridgeHandle handle
) {
    try {
        if (!handle) return 0;
        auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
        return bridge->EnableMmcss() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_audio_ipc_bridge_revert_mmcss(
    MoonshineAudioIpcBridgeHandle handle
) {
    try {
        if (handle) {
            auto* bridge = static_cast<audio::VirtualAudioIpcBridge*>(handle);
            bridge->RevertMmcss();
        }
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
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
    try {
        auto* cap = new capture::DxgiDesktopDuplicator(adapter_index, output_index);
        if (!cap->initialize()) {
            delete cap;
            return nullptr;
        }
        if (out_width) *out_width = cap->width();
        if (out_height) *out_height = cap->height();
        return static_cast<MoonshineCaptureHandle>(cap);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API MoonshineCaptureHandle MOONSHINE_CONV moonshine_capture_create_wgc(
    void* hmonitor,
    uint32_t target_fps,
    uint32_t* out_width,
    uint32_t* out_height
) {
    try {
        auto* cap = new capture::WgcDesktopCapture(hmonitor, target_fps);
        if (!cap->initialize()) {
            delete cap;
            return nullptr;
        }
        if (out_width) *out_width = cap->width();
        if (out_height) *out_height = cap->height();
        return static_cast<MoonshineCaptureHandle>(cap);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_capture_destroy(MoonshineCaptureHandle handle) {
    try {
        if (!handle) return;
        auto* cap = static_cast<capture::IDesktopCapture*>(handle);
        delete cap;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_acquire_frame(
    MoonshineCaptureHandle handle,
    uint32_t timeout_ms,
    MoonshineCaptureFrameDesc* out_frame
) {
    try {
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
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_capture_release_frame(MoonshineCaptureHandle handle) {
    try {
        if (!handle) return;
        auto* cap = static_cast<capture::IDesktopCapture*>(handle);
        cap->release_frame();
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_recover(MoonshineCaptureHandle handle) {
    try {
        if (!handle) return -1;
        auto* cap = static_cast<capture::IDesktopCapture*>(handle);
        return cap->recover() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_format(MoonshineCaptureHandle handle) {
    try {
        if (!handle) return 0;
        auto* cap = static_cast<capture::IDesktopCapture*>(handle);
        return cap->format();
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_is_hdr(MoonshineCaptureHandle handle) {
    try {
        if (!handle) return 0;
        auto* cap = static_cast<capture::IDesktopCapture*>(handle);
        return cap->is_hdr() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_adapter_count(void) {
    try {
    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return 0;
        UINT count = 0;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        while (factory->EnumAdapters1(count, &adapter) != DXGI_ERROR_NOT_FOUND) {
            count++;
            adapter.Reset();
        }
        return count;
    #else
        return 1;
    #endif
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_adapter_info(
    uint32_t adapter_index,
    MoonshineAdapterInfo* out_info
) {
    try {
        if (!out_info) return -1;
        std::memset(out_info, 0, sizeof(MoonshineAdapterInfo));
        out_info->adapter_index = adapter_index;

    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return -1;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return -1;

        DXGI_ADAPTER_DESC1 desc = {};
        if (FAILED(adapter->GetDesc1(&desc))) return -1;

        out_info->adapter_luid = *reinterpret_cast<const int64_t*>(&desc.AdapterLuid);
        out_info->dedicated_video_memory = static_cast<uint64_t>(desc.DedicatedVideoMemory);
        out_info->is_hardware = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ? 0 : 1;

        WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, out_info->description, sizeof(out_info->description) - 1, nullptr, nullptr);
        return 0;
    #else
        out_info->adapter_luid = 1;
        out_info->dedicated_video_memory = 8ULL * 1024 * 1024 * 1024;
        out_info->is_hardware = 1;
        std::strncpy(out_info->description, "Mock Physical GPU Adapter", sizeof(out_info->description) - 1);
        return 0;
    #endif
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_gpu_enumerate_adapters(
    MoonshineGpuAdapter* out_adapters,
    uint32_t max_count,
    uint32_t* out_count
) {
    try {
        if (out_count) *out_count = 0;
    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
            return -1;
        }

        uint32_t count = 0;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
            DXGI_ADAPTER_DESC1 desc{};
            if (FAILED(adapter->GetDesc1(&desc))) {
                continue;
            }

            if (out_adapters && count < max_count) {
                auto& out = out_adapters[count];
                std::memset(&out, 0, sizeof(MoonshineGpuAdapter));
                out.index = i;
                out.vendor_id = desc.VendorId;
                out.device_id = desc.DeviceId;
                out.subsystem_id = desc.SubSysId;
                out.revision = desc.Revision;
                out.is_software = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ? 1 : 0;

                Microsoft::WRL::ComPtr<IDXGIOutput> output;
                out.has_output = (adapter->EnumOutputs(0, &output) != DXGI_ERROR_NOT_FOUND) ? 1 : 0;

                ULARGE_INTEGER luidVal{};
                luidVal.LowPart = desc.AdapterLuid.LowPart;
                luidVal.HighPart = desc.AdapterLuid.HighPart;
                out.adapter_luid = luidVal.QuadPart;

                out.dedicated_video_memory = static_cast<uint64_t>(desc.DedicatedVideoMemory);
                out.shared_system_memory = static_cast<uint64_t>(desc.SharedSystemMemory);

                WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, out.description, sizeof(out.description) - 1, nullptr, nullptr);
                out.description[sizeof(out.description) - 1] = '\0';
            }
            count++;
        }

        if (out_count) *out_count = count;
        return 0;
    #else
        if (out_adapters && max_count >= 1) {
            auto& out = out_adapters[0];
            std::memset(&out, 0, sizeof(MoonshineGpuAdapter));
            out.index = 0;
            out.vendor_id = 0x10DE;
            out.device_id = 0x1F15;
            out.subsystem_id = 0;
            out.revision = 0;
            out.is_software = 0;
            out.has_output = 1;
            out.adapter_luid = 1;
            out.dedicated_video_memory = 8ULL * 1024 * 1024 * 1024;
            out.shared_system_memory = 16ULL * 1024 * 1024 * 1024;
            std::strncpy(out.description, "Mock Physical GPU Adapter", sizeof(out.description) - 1);
        }
        if (out_count) *out_count = 1;
        return 0;
    #endif
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_display_count(uint32_t adapter_index) {
    try {
    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return 0;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return 0;

        UINT count = 0;
        Microsoft::WRL::ComPtr<IDXGIOutput> output;
        while (adapter->EnumOutputs(count, &output) != DXGI_ERROR_NOT_FOUND) {
            count++;
            output.Reset();
        }
        return count;
    #else
        (void)adapter_index;
        return 1;
    #endif
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_info(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayInfo* out_info
) {
    try {
        if (!out_info) return -1;
        std::memset(out_info, 0, sizeof(MoonshineDisplayInfo));
        out_info->adapter_index = adapter_index;
        out_info->display_index = display_index;

    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return -1;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return -1;
        Microsoft::WRL::ComPtr<IDXGIOutput> output;
        if (FAILED(adapter->EnumOutputs(display_index, &output))) return -1;

        DXGI_OUTPUT_DESC desc = {};
        if (FAILED(output->GetDesc(&desc))) return -1;

        out_info->width = static_cast<uint32_t>(desc.DesktopCoordinates.right - desc.DesktopCoordinates.left);
        out_info->height = static_cast<uint32_t>(desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top);
        out_info->rotation = static_cast<uint32_t>(desc.Rotation);
        out_info->is_attached_to_desktop = desc.AttachedToDesktop ? 1 : 0;
        out_info->refresh_rate_num = 60;
        out_info->refresh_rate_den = 1;
        out_info->bits_per_color = 8;
        out_info->is_hdr = 0;

        UINT numModes = 0;
        if (SUCCEEDED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &numModes, nullptr)) && numModes > 0) {
            std::vector<DXGI_MODE_DESC> modes(numModes);
            if (SUCCEEDED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &numModes, modes.data()))) {
                for (const auto& mode : modes) {
                    if (mode.Width == out_info->width && mode.Height == out_info->height) {
                        out_info->refresh_rate_num = mode.RefreshRate.Numerator;
                        out_info->refresh_rate_den = mode.RefreshRate.Denominator;
                        break;
                    }
                }
            }
        }

        Microsoft::WRL::ComPtr<IDXGIOutput6> output6;
        if (SUCCEEDED(output.As(&output6))) {
            DXGI_OUTPUT_DESC1 desc1 = {};
            if (SUCCEEDED(output6->GetDesc1(&desc1))) {
                out_info->bits_per_color = static_cast<uint8_t>(desc1.BitsPerColor);
                out_info->is_hdr = (desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                                    desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709) ? 1 : 0;
            }
        }

        return 0;
    #else
        out_info->width = 1920;
        out_info->height = 1080;
        out_info->refresh_rate_num = 60;
        out_info->refresh_rate_den = 1;
        out_info->rotation = 0;
        out_info->is_attached_to_desktop = 1;
        out_info->is_hdr = 0;
        out_info->bits_per_color = 8;
        return 0;
    #endif
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

#if defined(_WIN32)
static uint32_t GetMonitorDpiScale(HMONITOR hMon) {
    if (!hMon) return 100;
    HMODULE shcore = GetModuleHandleW(L"shcore.dll");
    if (!shcore) shcore = LoadLibraryW(L"shcore.dll");
    if (shcore) {
        typedef HRESULT (WINAPI *GetDpiForMonitorProc)(HMONITOR, int, UINT*, UINT*);
        auto pfn = reinterpret_cast<GetDpiForMonitorProc>(GetProcAddress(shcore, "GetDpiForMonitor"));
        if (pfn) {
            UINT dpiX = 96, dpiY = 96;
            if (SUCCEEDED(pfn(hMon, 0, &dpiX, &dpiY)) && dpiX > 0) {
                return (dpiX * 100) / 96;
            }
        }
    }
    return 100;
}
#endif

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_extended_info(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayExtendedInfo* out_info
) {
    try {
        if (!out_info) return -1;
        std::memset(out_info, 0, sizeof(MoonshineDisplayExtendedInfo));
        out_info->adapter_index = adapter_index;
        out_info->display_index = display_index;

    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return -1;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return -1;
        Microsoft::WRL::ComPtr<IDXGIOutput> output;
        if (FAILED(adapter->EnumOutputs(display_index, &output))) return -1;

        DXGI_OUTPUT_DESC desc = {};
        if (FAILED(output->GetDesc(&desc))) return -1;

        out_info->monitor_handle = reinterpret_cast<int64_t>(desc.Monitor);
        out_info->desktop_left = desc.DesktopCoordinates.left;
        out_info->desktop_top = desc.DesktopCoordinates.top;
        out_info->desktop_right = desc.DesktopCoordinates.right;
        out_info->desktop_bottom = desc.DesktopCoordinates.bottom;
        out_info->is_attached_to_desktop = desc.AttachedToDesktop ? 1 : 0;
        out_info->bits_per_color = 8;
        out_info->is_hdr = 0;
        out_info->dpi_scale = GetMonitorDpiScale(desc.Monitor);

        WideCharToMultiByte(CP_UTF8, 0, desc.DeviceName, -1, out_info->device_name, sizeof(out_info->device_name) - 1, nullptr, nullptr);

        if (desc.Monitor) {
            MONITORINFOEXW mi = {};
            mi.cbSize = sizeof(mi);
            if (GetMonitorInfoW(desc.Monitor, &mi)) {
                out_info->is_primary = (mi.dwFlags & MONITORINFOF_PRIMARY) ? 1 : 0;

                DISPLAY_DEVICEW dispDev = {};
                dispDev.cb = sizeof(dispDev);
                if (EnumDisplayDevicesW(mi.szDevice, 0, &dispDev, 0) && dispDev.DeviceString[0] != L'\0') {
                    WideCharToMultiByte(CP_UTF8, 0, dispDev.DeviceString, -1, out_info->friendly_name, sizeof(out_info->friendly_name) - 1, nullptr, nullptr);
                }
            }
        }

        if (out_info->friendly_name[0] == '\0') {
            std::snprintf(out_info->friendly_name, sizeof(out_info->friendly_name), "%s", "Generic PnP Monitor");
        }

        Microsoft::WRL::ComPtr<IDXGIOutput6> output6;
        if (SUCCEEDED(output.As(&output6))) {
            DXGI_OUTPUT_DESC1 desc1 = {};
            if (SUCCEEDED(output6->GetDesc1(&desc1))) {
                out_info->bits_per_color = static_cast<uint8_t>(desc1.BitsPerColor);
                out_info->is_hdr = (desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                                    desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709) ? 1 : 0;
            }
        }

        return 0;
    #else
        out_info->monitor_handle = 1;
        out_info->desktop_left = 0;
        out_info->desktop_top = 0;
        out_info->desktop_right = 1920;
        out_info->desktop_bottom = 1080;
        out_info->dpi_scale = 100;
        out_info->is_primary = 1;
        out_info->is_attached_to_desktop = 1;
        out_info->is_hdr = 0;
        out_info->bits_per_color = 8;
        std::snprintf(out_info->device_name, sizeof(out_info->device_name), "%s", "\\\\.\\DISPLAY1");
        std::snprintf(out_info->friendly_name, sizeof(out_info->friendly_name), "%s", "Mock Physical Display");
        return 0;
    #endif
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_capture_get_display_mode_count(
    uint32_t adapter_index,
    uint32_t display_index
) {
    try {
    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return 0;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return 0;
        Microsoft::WRL::ComPtr<IDXGIOutput> output;
        if (FAILED(adapter->EnumOutputs(display_index, &output))) return 0;

        UINT numModes = 0;
        if (SUCCEEDED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &numModes, nullptr))) {
            return numModes;
        }
        return 0;
    #else
        (void)adapter_index;
        (void)display_index;
        return 1;
    #endif
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_capture_get_display_modes(
    uint32_t adapter_index,
    uint32_t display_index,
    MoonshineDisplayModeDesc* out_modes,
    uint32_t max_modes,
    uint32_t* out_mode_count
) {
    try {
        if (!out_modes || max_modes == 0 || !out_mode_count) return -1;
        *out_mode_count = 0;

    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return -1;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        if (FAILED(factory->EnumAdapters1(adapter_index, &adapter))) return -1;
        Microsoft::WRL::ComPtr<IDXGIOutput> output;
        if (FAILED(adapter->EnumOutputs(display_index, &output))) return -1;

        UINT numModes = 0;
        if (FAILED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &numModes, nullptr)) || numModes == 0) {
            return -1;
        }

        std::vector<DXGI_MODE_DESC> modes(numModes);
        if (FAILED(output->GetDisplayModeList(DXGI_FORMAT_B8G8R8A8_UNORM, 0, &numModes, modes.data()))) {
            return -1;
        }

        bool is_hdr = false;
        Microsoft::WRL::ComPtr<IDXGIOutput6> output6;
        if (SUCCEEDED(output.As(&output6))) {
            DXGI_OUTPUT_DESC1 desc1 = {};
            if (SUCCEEDED(output6->GetDesc1(&desc1))) {
                is_hdr = (desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 ||
                          desc1.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709);
            }
        }

        uint32_t toCopy = (std::min)(static_cast<uint32_t>(numModes), max_modes);
        for (uint32_t i = 0; i < toCopy; i++) {
            out_modes[i].width = modes[i].Width;
            out_modes[i].height = modes[i].Height;
            out_modes[i].refresh_rate_num = modes[i].RefreshRate.Numerator;
            out_modes[i].refresh_rate_den = modes[i].RefreshRate.Denominator;
            out_modes[i].format = static_cast<uint32_t>(modes[i].Format);
            out_modes[i].scaling = static_cast<uint32_t>(modes[i].Scaling);
            out_modes[i].scanline_ordering = static_cast<uint32_t>(modes[i].ScanlineOrdering);
            out_modes[i].is_hdr = is_hdr ? 1 : 0;
            std::memset(out_modes[i].reserved, 0, sizeof(out_modes[i].reserved));
        }
        *out_mode_count = toCopy;
        return 0;
    #else
        (void)adapter_index;
        (void)display_index;
        out_modes[0].width = 1920;
        out_modes[0].height = 1080;
        out_modes[0].refresh_rate_num = 60;
        out_modes[0].refresh_rate_den = 1;
        out_modes[0].format = 87;
        out_modes[0].scaling = 0;
        out_modes[0].scanline_ordering = 0;
        out_modes[0].is_hdr = 0;
        std::memset(out_modes[0].reserved, 0, sizeof(out_modes[0].reserved));
        *out_mode_count = 1;
        return 0;
    #endif
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// HDR10 Metadata Extraction & Real-Time Color Space Conversion APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_extract_metadata(
    void* hmonitor,
    MoonshineHdr10Metadata* out_metadata
) {
    try {
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
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_hdr_parse_capabilities(
    uint32_t color_space_dxgi,
    MoonshineHdr10Metadata* out_metadata
) {
    try {
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
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API MoonshineColorConverterHandle MOONSHINE_CONV moonshine_color_converter_create(
    void* d3d11_device,
    uint32_t width,
    uint32_t height,
    uint32_t in_format,
    uint32_t out_format
) {
    try {
        auto* conv = new color::D3DColorConverter(width, height, in_format, out_format);
        if (!conv->initialize(d3d11_device)) {
            delete conv;
            return nullptr;
        }
        return static_cast<MoonshineColorConverterHandle>(conv);
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_color_converter_convert(
    MoonshineColorConverterHandle handle,
    void* in_texture,
    void* out_texture
) {
    try {
        if (!handle || !in_texture || !out_texture) return -1;
        auto* conv = static_cast<color::D3DColorConverter*>(handle);
        return conv->convert(in_texture, out_texture) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_color_converter_destroy(
    MoonshineColorConverterHandle handle
) {
    try {
        if (!handle) return;
        auto* conv = static_cast<color::D3DColorConverter*>(handle);
        delete conv;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

// ============================================================================
// Multi-Vendor Hardware Video Encoder APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_query_caps(
    uint32_t vendor,
    void* d3d_device,
    MoonshineEncoderCaps* out_caps
) {
    try {
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
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API MoonshineEncoderHandle MOONSHINE_CONV moonshine_encoder_create(
    uint32_t vendor,
    void* d3d_device,
    const MoonshineEncoderConfig* config
) {
    try {
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
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
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
    try {
        if (out_size) *out_size = 0;
        if (!handle || !out_desc || !out_buffer || !out_size || max_buffer_size == 0) return 0;
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

        if (!res) {
            *out_size = 0;
            return 0;
        }

        out_desc->frame_index = desc.frame_index;
        out_desc->timestamp_qpc = desc.timestamp_qpc;
        out_desc->payload_size = desc.payload_size;
        out_desc->is_keyframe = desc.is_keyframe;
        out_desc->is_header_packet = desc.is_header_packet;
        out_desc->temporal_id = desc.temporal_id;
        out_desc->reserved = 0;
        *out_size = written;

        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_reconfigure(
    MoonshineEncoderHandle handle,
    const MoonshineEncoderConfig* new_config
) {
    try {
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
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_request_keyframe(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        encoder->request_keyframe();
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_drain(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return encoder->drain() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_encoder_flush(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return encoder->flush() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_encoder_destroy(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        delete encoder;
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_encoder_get_state(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return static_cast<int32_t>(encoder->get_state());
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_encoder_is_healthy(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return encoder->is_healthy() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_encoder_get_vendor(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* encoder = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return static_cast<uint32_t>(encoder->vendor());
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

// ============================================================================
// Direct3D 11 Hardware Device & Texture Utility APIs
// ============================================================================

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_device_on_adapter(uint32_t vendor_id, uint32_t adapter_index) {
    try {
    #if defined(_WIN32)
        Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
            return nullptr;
        }

        Microsoft::WRL::ComPtr<IDXGIAdapter1> chosen_adapter;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter;
        uint32_t match_count = 0;
        for (UINT i = 0; factory->EnumAdapters1(i, &adapter) != DXGI_ERROR_NOT_FOUND; ++i) {
            DXGI_ADAPTER_DESC1 desc{};
            if (SUCCEEDED(adapter->GetDesc1(&desc))) {
                if (vendor_id == 0 || desc.VendorId == vendor_id) {
                    if (match_count == adapter_index) {
                        chosen_adapter = adapter;
                        break;
                    }
                    match_count++;
                }
            }
        }

        if (!chosen_adapter) {
            return nullptr;
        }

        const UINT create_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        const D3D_FEATURE_LEVEL feature_levels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0
        };
        D3D_FEATURE_LEVEL fl{};
        Microsoft::WRL::ComPtr<ID3D11Device> device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;

        HRESULT hr = D3D11CreateDevice(
            chosen_adapter.Get(),
            D3D_DRIVER_TYPE_UNKNOWN,
            nullptr,
            create_flags,
            feature_levels,
            static_cast<UINT>(std::size(feature_levels)),
            D3D11_SDK_VERSION,
            &device,
            &fl,
            &context
        );

        if (FAILED(hr) || !device) {
            return nullptr;
        }

        // Defensive post-creation vendor invariant verification
        if (vendor_id != 0) {
            Microsoft::WRL::ComPtr<IDXGIDevice> dxgi_dev;
            if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgi_dev)))) {
                return nullptr;
            }
            Microsoft::WRL::ComPtr<IDXGIAdapter> dev_adapter;
            if (FAILED(dxgi_dev->GetAdapter(&dev_adapter))) {
                return nullptr;
            }
            DXGI_ADAPTER_DESC dev_desc{};
            if (FAILED(dev_adapter->GetDesc(&dev_desc)) || dev_desc.VendorId != vendor_id) {
                return nullptr;
            }
        }

        return device.Detach();
    #else
        (void)vendor_id;
        (void)adapter_index;
        return nullptr;
    #endif
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_device(uint32_t vendor_id) {
    return moonshine_d3d11_create_device_on_adapter(vendor_id, 0);
}

MOONSHINE_API void MOONSHINE_CONV moonshine_d3d11_destroy_device(void* d3d_device) {
    try {
    #if defined(_WIN32)
        if (d3d_device) {
            auto* dev = static_cast<ID3D11Device*>(d3d_device);
            dev->Release();
        }
    #else
        (void)d3d_device;
    #endif
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_texture(
    void* d3d_device,
    uint32_t width,
    uint32_t height,
    uint32_t format
) {
    try {
    #if defined(_WIN32)
        if (!d3d_device || width == 0 || height == 0) return nullptr;
        auto* dev = static_cast<ID3D11Device*>(d3d_device);

        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = (format == 0) ? DXGI_FORMAT_B8G8R8A8_UNORM : static_cast<DXGI_FORMAT>(format);
        desc.SampleDesc.Count = 1;
        desc.SampleDesc.Quality = 0;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

        Microsoft::WRL::ComPtr<ID3D11Texture2D> tex;
        if (FAILED(dev->CreateTexture2D(&desc, nullptr, &tex)) || !tex) {
            return nullptr;
        }
        return tex.Detach();
    #else
        (void)d3d_device; (void)width; (void)height; (void)format;
        return nullptr;
    #endif
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_d3d11_destroy_texture(void* texture) {
    try {
    #if defined(_WIN32)
        if (texture) {
            auto* tex = static_cast<ID3D11Texture2D*>(texture);
            tex->Release();
        }
    #else
        (void)texture;
    #endif
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

namespace {

void generate_test_pattern_bgra(
    uint32_t* out_pixels,
    uint32_t width,
    uint32_t height,
    uint32_t pattern_type,
    uint32_t frame_index
) {
    if (!out_pixels || width == 0 || height == 0) return;

    switch (pattern_type) {
        case 0: { // Black
            for (uint32_t i = 0; i < width * height; ++i) {
                out_pixels[i] = 0xFF000000;
            }
            break;
        }
        case 1: { // Solid Colour (Vibrant Teal: R=32, G=178, B=170, A=255)
            const uint32_t solid_color = 0xFF20B2AA;
            for (uint32_t i = 0; i < width * height; ++i) {
                out_pixels[i] = solid_color;
            }
            break;
        }
        case 2: { // Linear 2D Gradient
            for (uint32_t y = 0; y < height; ++y) {
                for (uint32_t x = 0; x < width; ++x) {
                    uint32_t r = (x * 255) / width;
                    uint32_t g = (y * 255) / height;
                    uint32_t b = ((x + y) * 255) / (width + height);
                    out_pixels[y * width + x] = 0xFF000000 | (r << 16) | (g << 8) | b;
                }
            }
            break;
        }
        case 3: { // Moving Procedural Pattern
            uint32_t box_size = 128;
            uint32_t max_x = width > box_size ? width - box_size : 1;
            uint32_t max_y = height > box_size ? height - box_size : 1;
            uint32_t box_x = (frame_index * 24) % max_x;
            uint32_t box_y = (frame_index * 16) % max_y;

            for (uint32_t y = 0; y < height; ++y) {
                for (uint32_t x = 0; x < width; ++x) {
                    if (x >= box_x && x < box_x + box_size && y >= box_y && y < box_y + box_size) {
                        bool check = (((x - box_x) / 16) + ((y - box_y) / 16)) % 2 == 0;
                        out_pixels[y * width + x] = check ? 0xFFFFFFFF : 0xFF00FF00;
                    } else {
                        float wave_val = std::sin((x + frame_index * 12) * 0.04f) * 0.5f + 0.5f;
                        uint32_t r = static_cast<uint32_t>(wave_val * 255.0f);
                        uint32_t g = (x * 255) / width;
                        uint32_t b = (y * 255) / height;
                        out_pixels[y * width + x] = 0xFF000000 | (r << 16) | (g << 8) | b;
                    }
                }
            }
            break;
        }
        case 4: // SMPTE Colour Bars (Descending luminance: White, Yellow, Cyan, Green, Magenta, Red, Blue)
        default: {
            const uint32_t smpte_colors[7] = {
                0xFFBFBFBF, // 75% White (Y ~ 191)
                0xFFBFBF00, // 75% Yellow (Y ~ 169)
                0xFF00BFBF, // 75% Cyan (Y ~ 133)
                0xFF00BF00, // 75% Green (Y ~ 112)
                0xFFBF00BF, // 75% Magenta (Y ~ 78)
                0xFFBF0000, // 75% Red (Y ~ 57)
                0xFF0000BF  // 75% Blue (Y ~ 21)
            };
            for (uint32_t y = 0; y < height; ++y) {
                for (uint32_t x = 0; x < width; ++x) {
                    uint32_t calc_idx = (x * 7) / width;
                    uint32_t bar_index = (calc_idx < 6) ? calc_idx : 6;
                    out_pixels[y * width + x] = smpte_colors[bar_index];
                }
            }
            break;
        }
    }
}

struct PixelSample {
    float r;
    float g;
    float b;
    float y;
};

PixelSample get_pixel_sample(
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t format,
    uint32_t x,
    uint32_t y
) {
    PixelSample sample{0.0f, 0.0f, 0.0f, 0.0f};
    if (!pixels || x >= width || y >= height) return sample;

#if defined(_WIN32)
    const uint32_t fmt_val = format;
    const bool is_nv12 = (fmt_val == static_cast<uint32_t>(DXGI_FORMAT_NV12) || fmt_val == 0 || fmt_val == 1 || fmt_val == 3);
    const bool is_p010 = (fmt_val == static_cast<uint32_t>(DXGI_FORMAT_P010) || fmt_val == static_cast<uint32_t>(DXGI_FORMAT_P016) || fmt_val == 2);
    const bool is_rgba = (fmt_val == static_cast<uint32_t>(DXGI_FORMAT_R8G8B8A8_UNORM) || fmt_val == static_cast<uint32_t>(DXGI_FORMAT_R8G8B8A8_UNORM_SRGB));

    if (is_nv12) {
        uint8_t y_raw = pixels[y * width + x];
        const uint8_t* uv_plane = pixels + (width * height);
        uint32_t uv_idx = (y / 2) * width + (x / 2) * 2;
        uint8_t u_raw = uv_plane[uv_idx];
        uint8_t v_raw = uv_plane[uv_idx + 1];

        float yf = static_cast<float>(y_raw);
        float uf = static_cast<float>(u_raw) - 128.0f;
        float vf = static_cast<float>(v_raw) - 128.0f;

        sample.r = std::clamp(yf + 1.402f * vf, 0.0f, 255.0f);
        sample.g = std::clamp(yf - 0.344136f * uf - 0.714136f * vf, 0.0f, 255.0f);
        sample.b = std::clamp(yf + 1.772f * uf, 0.0f, 255.0f);
        sample.y = yf;
        return sample;
    } else if (is_p010) {
        const auto* y_plane = reinterpret_cast<const uint16_t*>(pixels);
        uint16_t y_raw = y_plane[y * width + x] >> 6;
        float yf = (static_cast<float>(y_raw) / 1023.0f) * 255.0f;

        const auto* uv_plane = reinterpret_cast<const uint16_t*>(pixels + (width * height * 2));
        uint32_t uv_idx = (y / 2) * width + (x / 2) * 2;
        float uf = ((static_cast<float>(uv_plane[uv_idx] >> 6) / 1023.0f) * 255.0f) - 128.0f;
        float vf = ((static_cast<float>(uv_plane[uv_idx + 1] >> 6) / 1023.0f) * 255.0f) - 128.0f;

        sample.r = std::clamp(yf + 1.402f * vf, 0.0f, 255.0f);
        sample.g = std::clamp(yf - 0.344136f * uf - 0.714136f * vf, 0.0f, 255.0f);
        sample.b = std::clamp(yf + 1.772f * uf, 0.0f, 255.0f);
        sample.y = yf;
        return sample;
    } else if (is_rgba) {
        const uint8_t* p = pixels + (y * width + x) * 4;
        sample.r = static_cast<float>(p[0]);
        sample.g = static_cast<float>(p[1]);
        sample.b = static_cast<float>(p[2]);
        sample.y = 0.299f * sample.r + 0.587f * sample.g + 0.114f * sample.b;
        return sample;
    } else {
        // Default: DXGI_FORMAT_B8G8R8A8_UNORM (BGRA)
        const uint8_t* p = pixels + (y * width + x) * 4;
        sample.b = static_cast<float>(p[0]);
        sample.g = static_cast<float>(p[1]);
        sample.r = static_cast<float>(p[2]);
        sample.y = 0.299f * sample.r + 0.587f * sample.g + 0.114f * sample.b;
        return sample;
    }
#else
    const uint8_t* p = pixels + (y * width + x) * 4;
    sample.b = static_cast<float>(p[0]);
    sample.g = static_cast<float>(p[1]);
    sample.r = static_cast<float>(p[2]);
    sample.y = 0.299f * sample.r + 0.587f * sample.g + 0.114f * sample.b;
    return sample;
#endif
}

int verify_pattern_pixel_data(
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t format,
    uint32_t pattern_type,
    float tolerance
) {
    if (!pixels || width == 0 || height == 0) {
        return MOONSHINE_ERR_INVALID_ARGUMENT;
    }

    const float tol = (tolerance > 0.0f) ? tolerance : 0.0f;

    switch (pattern_type) {
        case 0: { // Pattern 0 (Black): asserts average luminance < 25.0 (near black)
            double total_luma = 0.0;
            uint64_t count = 0;
            const uint32_t min_dim = (width < height) ? width : height;
            const uint32_t step = (min_dim / 128 > 1) ? (min_dim / 128) : 1;
            for (uint32_t y = 0; y < height; y += step) {
                for (uint32_t x = 0; x < width; x += step) {
                    PixelSample s = get_pixel_sample(pixels, width, height, format, x, y);
                    total_luma += s.y;
                    count++;
                }
            }
            if (count == 0) return MOONSHINE_ERR_FATAL;
            float avg_luma = static_cast<float>(total_luma / count);
            float max_allowed = 25.0f + (tol * 25.0f);
            if (avg_luma >= max_allowed) {
                return -2; // Verification failed: average luminance not near black
            }
            return MOONSHINE_SUCCESS;
        }

        case 1: { // Pattern 1 (Teal): asserts green > 60, blue > 60, red < 110
            double total_r = 0.0;
            double total_g = 0.0;
            double total_b = 0.0;
            uint64_t count = 0;
            const uint32_t min_dim = (width < height) ? width : height;
            const uint32_t step = (min_dim / 128 > 1) ? (min_dim / 128) : 1;
            for (uint32_t y = 0; y < height; y += step) {
                for (uint32_t x = 0; x < width; x += step) {
                    PixelSample s = get_pixel_sample(pixels, width, height, format, x, y);
                    total_r += s.r;
                    total_g += s.g;
                    total_b += s.b;
                    count++;
                }
            }
            if (count == 0) return MOONSHINE_ERR_FATAL;
            float avg_r = static_cast<float>(total_r / count);
            float avg_g = static_cast<float>(total_g / count);
            float avg_b = static_cast<float>(total_b / count);

            float min_g = 60.0f - (tol * 35.0f);
            float min_b = 60.0f - (tol * 35.0f);
            float max_r = 110.0f + (tol * 35.0f);

            if (avg_g < min_g || avg_b < min_b || avg_r > max_r) {
                return -2; // Verification failed: teal channel dominance unsatisfied
            }
            return MOONSHINE_SUCCESS;
        }

        case 2: { // Pattern 2 (Gradient): asserts horizontal luminance monotonicity across bars
            const uint32_t num_bars = 8;
            std::vector<double> bar_luma(num_bars, 0.0);
            std::vector<uint64_t> bar_count(num_bars, 0);
            const uint32_t min_dim = (width < height) ? width : height;
            const uint32_t step = (min_dim / 128 > 1) ? (min_dim / 128) : 1;

            for (uint32_t y = 0; y < height; y += step) {
                for (uint32_t x = 0; x < width; x += step) {
                    uint32_t calc_bar = (x * num_bars) / width;
                    uint32_t bar_idx = (calc_bar < num_bars - 1) ? calc_bar : (num_bars - 1);
                    PixelSample s = get_pixel_sample(pixels, width, height, format, x, y);
                    bar_luma[bar_idx] += s.y;
                    bar_count[bar_idx]++;
                }
            }

            std::vector<float> avg_bar_luma(num_bars, 0.0f);
            for (uint32_t i = 0; i < num_bars; ++i) {
                if (bar_count[i] > 0) {
                    avg_bar_luma[i] = static_cast<float>(bar_luma[i] / bar_count[i]);
                }
            }

            float monotonicity_slack = 10.0f + (tol * 25.0f);
            for (uint32_t i = 0; i < num_bars - 1; ++i) {
                if (avg_bar_luma[i] > avg_bar_luma[i + 1] + monotonicity_slack) {
                    return -2; // Verification failed: horizontal luminance not monotonic
                }
            }

            if (avg_bar_luma[num_bars - 1] - avg_bar_luma[0] < (10.0f - tol * 8.0f)) {
                return -2; // Verification failed: insufficient horizontal gradient delta
            }
            return MOONSHINE_SUCCESS;
        }

        case 3: { // Pattern 3 (Moving Pattern): asserts moving block contrast delta against background
            double global_luma = 0.0;
            uint64_t global_count = 0;
            const uint32_t min_dim = (width < height) ? width : height;
            const uint32_t step = (min_dim / 64 > 1) ? (min_dim / 64) : 1;

            const uint32_t grid_x = 8;
            const uint32_t grid_y = 8;
            std::vector<double> block_luma(grid_x * grid_y, 0.0);
            std::vector<uint64_t> block_count(grid_x * grid_y, 0);

            for (uint32_t y = 0; y < height; y += step) {
                uint32_t by_calc = (y * grid_y) / height;
                uint32_t by = (by_calc < grid_y - 1) ? by_calc : (grid_y - 1);
                for (uint32_t x = 0; x < width; x += step) {
                    uint32_t bx_calc = (x * grid_x) / width;
                    uint32_t bx = (bx_calc < grid_x - 1) ? bx_calc : (grid_x - 1);
                    PixelSample s = get_pixel_sample(pixels, width, height, format, x, y);
                    global_luma += s.y;
                    global_count++;

                    uint32_t block_idx = by * grid_x + bx;
                    block_luma[block_idx] += s.y;
                    block_count[block_idx]++;
                }
            }

            if (global_count == 0) return MOONSHINE_ERR_FATAL;
            float max_block_luma = 0.0f;
            float min_block_luma = 255.0f;

            for (uint32_t i = 0; i < grid_x * grid_y; ++i) {
                if (block_count[i] > 0) {
                    float b = static_cast<float>(block_luma[i] / block_count[i]);
                    if (b > max_block_luma) max_block_luma = b;
                    if (b < min_block_luma) min_block_luma = b;
                }
            }

            float contrast_delta = max_block_luma - min_block_luma;
            float min_delta_required = 8.0f - (tol * 6.0f);
            if (contrast_delta < min_delta_required) {
                return -2; // Verification failed: moving pattern contrast delta insufficient
            }
            return MOONSHINE_SUCCESS;
        }

        case 4: // SMPTE Bars (7 vertical bars across width)
        default: {
            const uint32_t num_bars = 7;
            std::vector<double> bar_luma(num_bars, 0.0);
            std::vector<double> bar_r(num_bars, 0.0);
            std::vector<double> bar_g(num_bars, 0.0);
            std::vector<double> bar_b(num_bars, 0.0);
            std::vector<uint64_t> bar_count(num_bars, 0);

            const uint32_t min_dim_smpte = (width < height) ? width : height;
            const uint32_t step = (min_dim_smpte / 128 > 1) ? (min_dim_smpte / 128) : 1;

            for (uint32_t bar = 0; bar < num_bars; ++bar) {
                uint32_t x_start = static_cast<uint32_t>((bar + 0.25f) * width / num_bars);
                uint32_t x_end = static_cast<uint32_t>((bar + 0.75f) * width / num_bars);
                for (uint32_t y = static_cast<uint32_t>(height * 0.2f); y < static_cast<uint32_t>(height * 0.8f); y += step) {
                    for (uint32_t x = x_start; x <= x_end; x += step) {
                        PixelSample s = get_pixel_sample(pixels, width, height, format, x, y);
                        bar_luma[bar] += s.y;
                        bar_r[bar] += s.r;
                        bar_g[bar] += s.g;
                        bar_b[bar] += s.b;
                        bar_count[bar]++;
                    }
                }
            }

            std::vector<float> avg_luma(num_bars, 0.0f);
            std::vector<float> avg_r(num_bars, 0.0f);
            std::vector<float> avg_g(num_bars, 0.0f);
            std::vector<float> avg_b(num_bars, 0.0f);

            for (uint32_t i = 0; i < num_bars; ++i) {
                if (bar_count[i] > 0) {
                    avg_luma[i] = static_cast<float>(bar_luma[i] / bar_count[i]);
                    avg_r[i] = static_cast<float>(bar_r[i] / bar_count[i]);
                    avg_g[i] = static_cast<float>(bar_g[i] / bar_count[i]);
                    avg_b[i] = static_cast<float>(bar_b[i] / bar_count[i]);
                }
            }

            float tol_slack = tol * 40.0f + 12.0f;
            for (uint32_t i = 0; i < num_bars - 1; ++i) {
                if (avg_luma[i] + tol_slack < avg_luma[i + 1]) {
                    return -2; // Verification failed: SMPTE vertical bar luminance order violated
                }
            }

            if (avg_luma[0] - avg_luma[6] < (30.0f - tol * 20.0f)) {
                return -2; // Verification failed: SMPTE dynamic range too low
            }

            return MOONSHINE_SUCCESS;
        }
    }
}

} // namespace

MOONSHINE_API void* MOONSHINE_CONV moonshine_d3d11_create_pattern_texture(
    void* d3d_device,
    uint32_t width,
    uint32_t height,
    uint32_t pattern_type,
    uint32_t frame_index
) {
    try {
    #if defined(_WIN32)
        if (!d3d_device || width == 0 || height == 0) return nullptr;
        auto* dev = static_cast<ID3D11Device*>(d3d_device);

        std::vector<uint32_t> pixels(width * height);
        generate_test_pattern_bgra(pixels.data(), width, height, pattern_type, frame_index);

        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.SampleDesc.Quality = 0;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

        D3D11_SUBRESOURCE_DATA init_data{};
        init_data.pSysMem = pixels.data();
        init_data.SysMemPitch = width * sizeof(uint32_t);

        Microsoft::WRL::ComPtr<ID3D11Texture2D> tex;
        if (FAILED(dev->CreateTexture2D(&desc, &init_data, &tex)) || !tex) {
            return nullptr;
        }
        return tex.Detach();
    #else
        (void)d3d_device; (void)width; (void)height; (void)pattern_type; (void)frame_index;
        return nullptr;
    #endif
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_d3d11_render_pattern(
    void* d3d_device,
    void* texture,
    uint32_t width,
    uint32_t height,
    uint32_t pattern_type,
    uint32_t frame_index
) {
    try {
    #if defined(_WIN32)
        if (!d3d_device || !texture || width == 0 || height == 0) return 0;
        auto* dev = static_cast<ID3D11Device*>(d3d_device);
        auto* p_tex = static_cast<ID3D11Texture2D*>(texture);

        std::vector<uint32_t> pixels(width * height);
        generate_test_pattern_bgra(pixels.data(), width, height, pattern_type, frame_index);

        Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
        dev->GetImmediateContext(&context);
        if (!context) return 0;

        context->UpdateSubresource(p_tex, 0, nullptr, pixels.data(), width * sizeof(uint32_t), 0);
        return 1;
    #else
        (void)d3d_device; (void)texture; (void)width; (void)height; (void)pattern_type; (void)frame_index;
        return 0;
    #endif
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_d3d11_readback_pixels(
    void* d3d_device,
    void* d3d_texture,
    uint8_t* out_pixels,
    uint32_t max_bytes,
    uint32_t* out_bytes
) {
    try {
        if (!d3d_texture || !out_bytes) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

    #if defined(_WIN32)
        auto* src_tex = static_cast<ID3D11Texture2D*>(d3d_texture);

        Microsoft::WRL::ComPtr<ID3D11Device> device;
        if (d3d_device) {
            device = static_cast<ID3D11Device*>(d3d_device);
        } else {
            src_tex->GetDevice(&device);
        }

        if (!device) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

        D3D11_TEXTURE2D_DESC src_desc{};
        src_tex->GetDesc(&src_desc);
        const uint32_t width = src_desc.Width;
        const uint32_t height = src_desc.Height;

        if (width == 0 || height == 0) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

        uint32_t required_bytes = 0;
        switch (src_desc.Format) {
            case DXGI_FORMAT_NV12:
                required_bytes = width * height * 3 / 2;
                break;
            case DXGI_FORMAT_P010:
            case DXGI_FORMAT_P016:
                required_bytes = width * height * 3;
                break;
            case DXGI_FORMAT_R16G16B16A16_UNORM:
            case DXGI_FORMAT_R16G16B16A16_FLOAT:
                required_bytes = width * height * 8;
                break;
            case DXGI_FORMAT_R8_UNORM:
            case DXGI_FORMAT_A8_UNORM:
                required_bytes = width * height;
                break;
            default:
                required_bytes = width * height * 4;
                break;
        }

        *out_bytes = required_bytes;
        if (!out_pixels) {
            return MOONSHINE_SUCCESS;
        }

        if (max_bytes < required_bytes) {
            return MOONSHINE_ERR_BUFFER_TOO_SMALL;
        }

        Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
        device->GetImmediateContext(&context);
        if (!context) {
            return MOONSHINE_ERR_FATAL;
        }

        Microsoft::WRL::ComPtr<ID3D11Texture2D> staging_tex;
        if (src_desc.Usage == D3D11_USAGE_STAGING && (src_desc.CPUAccessFlags & D3D11_CPU_ACCESS_READ)) {
            staging_tex = src_tex;
        } else {
            D3D11_TEXTURE2D_DESC staging_desc{};
            staging_desc.Width = width;
            staging_desc.Height = height;
            staging_desc.MipLevels = 1;
            staging_desc.ArraySize = 1;
            staging_desc.Format = src_desc.Format;
            staging_desc.SampleDesc.Count = 1;
            staging_desc.SampleDesc.Quality = 0;
            staging_desc.Usage = D3D11_USAGE_STAGING;
            staging_desc.BindFlags = 0;
            staging_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            staging_desc.MiscFlags = 0;

            HRESULT hr = device->CreateTexture2D(&staging_desc, nullptr, &staging_tex);
            if (FAILED(hr) || !staging_tex) {
                return MOONSHINE_ERR_FATAL;
            }

            context->CopySubresourceRegion(staging_tex.Get(), 0, 0, 0, 0, src_tex, 0, nullptr);
            context->Flush();
        }

        D3D11_MAPPED_SUBRESOURCE mapped{};
        HRESULT hr = context->Map(staging_tex.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr) || !mapped.pData) {
            return MOONSHINE_ERR_FATAL;
        }

        const uint8_t* src_bytes = static_cast<const uint8_t*>(mapped.pData);

        if (src_desc.Format == DXGI_FORMAT_NV12) {
            for (uint32_t y = 0; y < height; ++y) {
                std::memcpy(out_pixels + y * width, src_bytes + y * mapped.RowPitch, width);
            }
            uint8_t* out_uv = out_pixels + (width * height);
            const uint8_t* src_uv = src_bytes + (mapped.RowPitch * height);
            for (uint32_t y = 0; y < height / 2; ++y) {
                std::memcpy(out_uv + y * width, src_uv + y * mapped.RowPitch, width);
            }
        } else if (src_desc.Format == DXGI_FORMAT_P010 || src_desc.Format == DXGI_FORMAT_P016) {
            for (uint32_t y = 0; y < height; ++y) {
                std::memcpy(out_pixels + y * width * 2, src_bytes + y * mapped.RowPitch, width * 2);
            }
            uint8_t* out_uv = out_pixels + (width * height * 2);
            const uint8_t* src_uv = src_bytes + (mapped.RowPitch * height);
            for (uint32_t y = 0; y < height / 2; ++y) {
                std::memcpy(out_uv + y * width * 2, src_uv + y * mapped.RowPitch, width * 2);
            }
        } else {
            uint32_t tight_pitch = width * 4;
            for (uint32_t y = 0; y < height; ++y) {
                std::memcpy(out_pixels + y * tight_pitch, src_bytes + y * mapped.RowPitch, tight_pitch);
            }
        }

        context->Unmap(staging_tex.Get(), 0);
        return MOONSHINE_SUCCESS;
    #else
        (void)d3d_device; (void)d3d_texture; (void)out_pixels; (void)max_bytes; (void)out_bytes;
        return MOONSHINE_ERR_UNSUPPORTED_HARDWARE;
    #endif
    } catch (const std::exception&) {
        return MOONSHINE_ERR_FATAL;
    } catch (...) {
        return MOONSHINE_ERR_FATAL;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_verify_decoded_pattern(
    void* decoder,
    uint32_t pattern_type,
    float tolerance
) {
    try {
        if (!decoder) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

    #if defined(_WIN32)
        auto* dec = static_cast<video::IVideoDecoder*>(decoder);
        uint32_t width = 0;
        uint32_t height = 0;
        if (dec->GetDimensions(width, height) != 0 || width == 0 || height == 0) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

        uint32_t pixel_size = 0;
        const uint8_t* dec_pixels = dec->GetDecodedPixels(pixel_size);
        if (dec_pixels && pixel_size > 0) {
            auto* src_tex = static_cast<ID3D11Texture2D*>(dec->GetTextureHandle());
            DXGI_FORMAT fmt = DXGI_FORMAT_NV12;
            if (src_tex) {
                D3D11_TEXTURE2D_DESC desc{};
                src_tex->GetDesc(&desc);
                fmt = desc.Format;
            }
            return verify_pattern_pixel_data(
                dec_pixels,
                width,
                height,
                static_cast<uint32_t>(fmt),
                pattern_type,
                tolerance
            );
        }

        void* tex_handle = dec->GetTextureHandle();
        void* dev_handle = dec->GetDeviceHandle();
        if (!tex_handle) {
            return MOONSHINE_ERR_INVALID_ARGUMENT;
        }

        uint32_t needed_bytes = 0;
        int size_res = moonshine_d3d11_readback_pixels(dev_handle, tex_handle, nullptr, 0, &needed_bytes);
        if (size_res != MOONSHINE_SUCCESS || needed_bytes == 0) {
            return MOONSHINE_ERR_FATAL;
        }

        std::vector<uint8_t> pixels(needed_bytes);
        uint32_t read_bytes = 0;
        int read_res = moonshine_d3d11_readback_pixels(
            dev_handle,
            tex_handle,
            pixels.data(),
            static_cast<uint32_t>(pixels.size()),
            &read_bytes
        );
        if (read_res != MOONSHINE_SUCCESS || read_bytes == 0) {
            return MOONSHINE_ERR_FATAL;
        }

        auto* src_tex = static_cast<ID3D11Texture2D*>(tex_handle);
        D3D11_TEXTURE2D_DESC desc{};
        src_tex->GetDesc(&desc);

        return verify_pattern_pixel_data(
            pixels.data(),
            width,
            height,
            static_cast<uint32_t>(desc.Format),
            pattern_type,
            tolerance
        );
    #else
        (void)decoder; (void)pattern_type; (void)tolerance;
        return MOONSHINE_ERR_UNSUPPORTED_HARDWARE;
    #endif
    } catch (const std::exception&) {
        return MOONSHINE_ERR_FATAL;
    } catch (...) {
        return MOONSHINE_ERR_FATAL;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_video_compute_quality_metrics(
    const uint8_t* reference_pixels,
    uint32_t reference_format,
    const uint8_t* decoded_pixels,
    uint32_t decoded_format,
    uint32_t width,
    uint32_t height,
    float tolerance,
    MoonshineQualityMetrics* out_metrics
) {
    if (!reference_pixels || !decoded_pixels || !out_metrics || width == 0 || height == 0) {
        return MOONSHINE_ERR_INVALID_ARGUMENT;
    }

    try {
        double sum_sq_err_y = 0.0;
        double sum_sq_err_rgb = 0.0;
        double sum_abs_err = 0.0;
        float max_err = 0.0f;
        uint32_t pixels_within_tol = 0;
        uint32_t sample_count = 0;
        
        uint32_t step_x = (width > 1280) ? 8 : (width > 640) ? 4 : 2;
        uint32_t step_y = (height > 720) ? 8 : (height > 360) ? 4 : 2;

        for (uint32_t y = 0; y < height; y += step_y) {
            for (uint32_t x = 0; x < width; x += step_x) {
                PixelSample ref = get_pixel_sample(reference_pixels, width, height, reference_format, x, y);
                PixelSample dec = get_pixel_sample(decoded_pixels, width, height, decoded_format, x, y);

                float err_r = std::abs(ref.r - dec.r);
                float err_g = std::abs(ref.g - dec.g);
                float err_b = std::abs(ref.b - dec.b);
                float err_y = std::abs(ref.y - dec.y);

                float local_max = (std::max)({err_r, err_g, err_b});
                if (local_max > max_err) max_err = local_max;
                if (local_max <= tolerance) pixels_within_tol++;

                sum_abs_err += (err_r + err_g + err_b) / 3.0;
                sum_sq_err_rgb += (err_r * err_r + err_g * err_g + err_b * err_b) / 3.0;
                sum_sq_err_y += (err_y * err_y);
                sample_count++;
            }
        }

        if (sample_count == 0) return MOONSHINE_ERR_FATAL;

        double mse_y = sum_sq_err_y / sample_count;
        double mse_rgb = sum_sq_err_rgb / sample_count;

        out_metrics->psnr_y = (mse_y > 0.0) ? static_cast<float>(10.0 * std::log10(255.0 * 255.0 / mse_y)) : 100.0f;
        out_metrics->psnr_rgb = (mse_rgb > 0.0) ? static_cast<float>(10.0 * std::log10(255.0 * 255.0 / mse_rgb)) : 100.0f;
        out_metrics->mae = static_cast<float>(sum_abs_err / sample_count);
        out_metrics->max_error = max_err;
        out_metrics->pixels_within_tolerance_pct = (static_cast<float>(pixels_within_tol) / static_cast<float>(sample_count)) * 100.0f;
        out_metrics->width = width;
        out_metrics->height = height;
        out_metrics->reference_format = reference_format;
        out_metrics->decoded_format = decoded_format;

        return MOONSHINE_SUCCESS;
    } catch (...) {
        return MOONSHINE_ERR_FATAL;
    }
}

// ============================================================================
// NVIDIA NVENC Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    try {
        if (!out_supported) return 0;
        bool supported = encoder::NvencVideoEncoder::query_codec_support(
            static_cast<encoder::VideoCodec>(codec)
        );
        *out_supported = supported ? 1 : 0;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t tuning
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::NvencVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_preset_and_tuning(
                static_cast<encoder::NvencPreset>(preset),
                static_cast<encoder::NvencTuning>(tuning)
            ) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_nvenc_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t period,
    uint32_t count
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::NvencVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_intra_refresh(enable != 0, period, count) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// AMD AMF Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    try {
        if (!out_supported) return 0;
        bool supported = encoder::AmfVideoEncoder::query_codec_support(
            static_cast<encoder::VideoCodec>(codec)
        );
        *out_supported = supported ? 1 : 0;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t preset,
    uint32_t usage
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::AmfVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_preset_and_usage(
                static_cast<encoder::AmfQualityPreset>(preset),
                static_cast<encoder::AmfUsage>(usage)
            ) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t mbs_per_slot
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::AmfVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_intra_refresh(enable != 0, mbs_per_slot) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_drain(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return unified->drain() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_amf_flush(
    MoonshineEncoderHandle handle
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        return unified->flush() ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// Intel QuickSync / oneVPL Dedicated Custom APIs
// ============================================================================

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_query_codec_support(
    uint32_t codec,
    uint32_t* out_supported
) {
    try {
        if (!out_supported) return 0;
        bool supported = encoder::QsvVideoEncoder::query_codec_support(
            static_cast<encoder::VideoCodec>(codec)
        );
        *out_supported = supported ? 1 : 0;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_tuning(
    MoonshineEncoderHandle handle,
    uint32_t target_usage,
    int low_power_vdenc
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::QsvVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_target_usage(
                static_cast<encoder::QsvTargetUsage>(target_usage),
                low_power_vdenc != 0
            ) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_set_intra_refresh(
    MoonshineEncoderHandle handle,
    int enable,
    uint32_t cycle_size,
    int32_t qp_delta
) {
    try {
        if (!handle) return 0;
        auto* unified = static_cast<encoder::UnifiedVideoEncoder*>(handle);
        auto* active = dynamic_cast<encoder::QsvVideoEncoder*>(unified->active_encoder());
        if (active) {
            return active->set_intra_refresh(enable != 0, cycle_size, qp_delta) ? 1 : 0;
        }
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int MOONSHINE_CONV moonshine_qsv_run_diagnostics(
    MoonshineQsvDiagnosticReport* out_report
) {
    try {
        if (!out_report) return -1;
        try {
            return encoder::qsv::QsvDiagnostic::run(out_report);
        } catch (...) {
            return -1;
        }
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

// ============================================================================
// Windows Mouse & Keyboard Input Injector APIs
// ============================================================================

MOONSHINE_API void* MOONSHINE_CONV moonshine_input_injector_create(void) {
    try {
        return new (std::nothrow) input::WindowsInputInjector();
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_input_injector_destroy(void* injector) {
    try {
        if (injector) {
            auto* inj = static_cast<input::WindowsInputInjector*>(injector);
            delete inj;
        }
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_move(
    void* injector,
    int16_t delta_x,
    int16_t delta_y
) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_mouse_move(delta_x, delta_y) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

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
) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_mouse_abs(x, y, client_width, client_height,
                                     monitor_offset_x, monitor_offset_y,
                                     monitor_width, monitor_height) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_button(
    void* injector,
    uint8_t button_index,
    int32_t is_down
) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_mouse_button(button_index, is_down != 0) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_mouse_scroll(
    void* injector,
    int16_t scroll_delta,
    int32_t is_horizontal
) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_mouse_scroll(scroll_delta, is_horizontal != 0) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_inject_keyboard(
    void* injector,
    int16_t virtual_key_code,
    int16_t scan_code,
    int32_t is_down,
    uint8_t modifiers
) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_keyboard_key(virtual_key_code, scan_code, is_down != 0, modifiers) ? 1 : 0;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_input_inject_batch(
    void* injector,
    const void* inputs,
    uint32_t count
) {
    try {
        if (!injector || !inputs || count == 0) return 0;
    #if defined(_WIN32)
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->inject_batch(static_cast<const INPUT*>(inputs), count);
    #else
        (void)injector; (void)inputs; (void)count;
        return 0;
    #endif
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API uint32_t MOONSHINE_CONV moonshine_input_release_all_held(void* injector) {
    try {
        if (!injector) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        return inj->release_all_held_inputs();
    } catch (const std::exception&) {
        return 0;
    } catch (...) {
        return 0;
    }
}

MOONSHINE_API int32_t MOONSHINE_CONV moonshine_input_get_virtual_desktop_bounds(
    void* injector,
    MoonshineVirtualDesktopBoundsC* bounds
) {
    try {
        if (!injector || !bounds) return 0;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        auto b = inj->get_virtual_desktop_bounds();
        bounds->x_virtual_screen = b.x_virtual_screen;
        bounds->y_virtual_screen = b.y_virtual_screen;
        bounds->cx_virtual_screen = b.cx_virtual_screen;
        bounds->cy_virtual_screen = b.cy_virtual_screen;
        return 1;
    } catch (const std::exception&) {
        return -999;
    } catch (...) {
        return -999;
    }
}

MOONSHINE_API void MOONSHINE_CONV moonshine_input_refresh_virtual_desktop_bounds(void* injector) {
    try {
        if (!injector) return;
        auto* inj = static_cast<input::WindowsInputInjector*>(injector);
        inj->refresh_virtual_desktop_bounds();
    } catch (const std::exception&) {
        return;
    } catch (...) {
        return;
    }
}

} // extern "C"
