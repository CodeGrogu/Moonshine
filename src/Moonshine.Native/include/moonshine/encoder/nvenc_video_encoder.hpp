#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>
#include <cstdint>
#include <memory>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
using HMODULE = void*;
#endif

namespace moonshine::encoder {

enum class NvencLifecycleState : uint32_t {
    Uninitialised = 0,
    DeviceAttached = 1,
    SessionCreated = 2,
    EncoderInitialised = 3,
    ResourcesRegistered = 4,
    Ready = 5,
    Encoding = 6,
    Flushing = 7,
    Faulted = 8,
    Disposed = 9
};

enum class NvencPreset : uint32_t {
    P1_UltraFast = 1,
    P2_Fast = 2,
    P3_Medium = 3,
    P4_Default = 4,
    P5_Slow = 5,
    P6_Slower = 6,
    P7_Slowest = 7
};

enum class NvencTuning : uint32_t {
    HighQuality = 0,
    LowLatency = 1,
    UltraLowLatency = 2,
    Lossless = 3
};

#if !defined(_NVENC_FN_LIST_DEFINED)
#define _NVENC_FN_LIST_DEFINED
typedef struct _NVENC_FN_LIST {
    uint32_t version;
    uint32_t reserved;
    void* nvEncOpenEncodeSession;
    void* nvEncGetEncodeGUIDCount;
    void* nvEncGetEncodeProfileGUIDCount;
    void* nvEncGetEncodeProfileGUIDs;
    void* nvEncGetEncodeGUIDs;
    void* nvEncGetInputFormatCount;
    void* nvEncGetInputFormats;
    void* nvEncGetEncodeCaps;
    void* nvEncGetEncodePresetCount;
    void* nvEncGetEncodePresetGUIDs;
    void* nvEncGetEncodePresetConfig;
    void* nvEncInitializeEncoder;
    void* nvEncCreateInputBuffer;
    void* nvEncDestroyInputBuffer;
    void* nvEncCreateBitstreamBuffer;
    void* nvEncDestroyBitstreamBuffer;
    void* nvEncEncodePicture;
    void* nvEncLockBitstream;
    void* nvEncUnlockBitstream;
    void* nvEncLockInputBuffer;
    void* nvEncUnlockInputBuffer;
    void* nvEncGetEncodeStats;
    void* nvEncGetSequenceParams;
    void* nvEncRegisterAsyncEvent;
    void* nvEncUnregisterAsyncEvent;
    void* nvEncMapInputResource;
    void* nvEncUnmapInputResource;
    void* nvEncDestroyEncoder;
    void* nvEncInvalidateRefFrames;
    void* nvEncOpenEncodeSessionEx;
    void* nvEncRegisterResource;
    void* nvEncUnregisterResource;
    void* nvEncReconfigureEncoder;
    void* reserved1;
    void* nvEncCreateMVBuffer;
    void* nvEncDestroyMVBuffer;
    void* nvEncRunMotionEstimationOnly;
    void* nvEncGetLastErrorString;
    void* nvEncSetIOCudaStreams;
    void* nvEncGetEncodePresetConfigEx;
    void* nvEncGetSequenceParamEx;
    void* nvEncRestoreEncoderState;
    void* nvEncLookaheadPicture;
    void* reserved2[275];
} NVENC_FN_LIST;
#endif

class NvencVideoEncoder final : public IVideoEncoder {
public:
    NvencVideoEncoder();
    ~NvencVideoEncoder() override;

    bool initialize(void* d3d_device, const EncoderConfig& config) override;
    bool encode_frame(
        void* d3d_texture,
        bool force_idr,
        EncodedPacketDesc& out_desc,
        uint8_t* out_bitstream,
        uint32_t max_buffer_size,
        uint32_t& out_written_size
    ) override;
    bool reconfigure(const EncoderConfig& new_config) override;
    void request_keyframe() override;
    void cleanup() override;

    [[nodiscard]] EncoderVendor vendor() const noexcept override { return EncoderVendor::NvidiaNvenc; }
    [[nodiscard]] bool is_initialized() const noexcept override { return _initialized; }
    [[nodiscard]] NvencLifecycleState state() const noexcept { return _state.load(); }
    [[nodiscard]] uint32_t get_state() const noexcept override { return static_cast<uint32_t>(_state.load()); }
    [[nodiscard]] bool is_healthy() const noexcept override;

    bool set_preset_and_tuning(NvencPreset preset, NvencTuning tuning);
    bool set_intra_refresh(bool enabled, uint32_t period, uint32_t count);
    bool drain() override;
    bool flush() override;

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);
    static bool query_codec_support(VideoCodec codec);

private:
    struct Impl;
    std::unique_ptr<Impl> _impl;
    bool _initialized{false};
    std::atomic<NvencLifecycleState> _state{NvencLifecycleState::Uninitialised};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    NvencPreset _preset{NvencPreset::P1_UltraFast};
    NvencTuning _tuning{NvencTuning::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_period{0};
    uint32_t _intra_refresh_count{0};
    std::atomic<bool> _force_keyframe{true};
    std::atomic<uint64_t> _frame_counter{0};
};

} // namespace moonshine::encoder
