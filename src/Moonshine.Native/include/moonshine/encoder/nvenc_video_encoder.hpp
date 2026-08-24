#pragma once

#include "moonshine/encoder/video_encoder_interface.hpp"
#include <atomic>
#include <vector>
#include <cstdint>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
using HMODULE = void*;
#endif

namespace moonshine::encoder {

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

    bool set_preset_and_tuning(NvencPreset preset, NvencTuning tuning);
    bool set_intra_refresh(bool enabled, uint32_t period, uint32_t count);

    static bool query_capabilities(void* d3d_device, EncoderCaps& out_caps);
    static bool query_codec_support(VideoCodec codec);

private:
    bool _initialized{false};
    EncoderConfig _config{};
    void* _d3d_device{nullptr};
    void* _encoder_session{nullptr};
    void* _bitstream_buffer{nullptr};
    HMODULE _nvenc_module{nullptr};
    NVENC_FN_LIST _nvenc_funcs{};
    void* _registered_texture{nullptr};
    void* _registered_resource{nullptr};
    NvencPreset _preset{NvencPreset::P1_UltraFast};
    NvencTuning _tuning{NvencTuning::UltraLowLatency};
    bool _intra_refresh_enabled{false};
    uint32_t _intra_refresh_period{0};
    uint32_t _intra_refresh_count{0};
    std::atomic<bool> _force_keyframe{true};
    uint64_t _frame_counter{0};
    std::vector<uint8_t> _header_cache;
};

} // namespace moonshine::encoder
