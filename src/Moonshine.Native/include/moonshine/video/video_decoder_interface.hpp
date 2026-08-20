#pragma once

#include "moonshine/export/moonshine_native_api.h"
#include <cstdint>
#include <memory>

namespace moonshine::video {

enum class VideoCodec : uint32_t {
    H264 = 0,
    HEVC = 1,
    AV1  = 2
};

class IVideoDecoder {
public:
    virtual ~IVideoDecoder() = default;
    virtual int Initialize(void* hwnd, uint32_t width, uint32_t height, VideoCodec codec) = 0;
    virtual int SubmitFrame(const MoonshineFrameDesc& frame) = 0;
    virtual void Shutdown() = 0;
};

class D3D11VideoDecoder final : public IVideoDecoder {
public:
    D3D11VideoDecoder();
    ~D3D11VideoDecoder() override;

    int Initialize(void* hwnd, uint32_t width, uint32_t height, VideoCodec codec) override;
    int SubmitFrame(const MoonshineFrameDesc& frame) override;
    void Shutdown() override;

    static void QueryCaps(MoonshineDecoderCaps& out_caps) noexcept;

private:
    void* hwnd_{nullptr};
    uint32_t width_{0};
    uint32_t height_{0};
    VideoCodec codec_{VideoCodec::HEVC};
    bool initialized_{false};
};

} // namespace moonshine::video
