#pragma once

#include "encoder/nvenc/nvenc_types.hpp"

namespace moonshine::encoder::nvenc {

class NvencApi {
public:
    NvencApi();
    ~NvencApi();

    NvencApi(const NvencApi&) = delete;
    NvencApi& operator=(const NvencApi&) = delete;

    NvencApi(NvencApi&& other) noexcept;
    NvencApi& operator=(NvencApi&& other) noexcept;

    bool load();
    void unload();
    [[nodiscard]] bool is_loaded() const noexcept;
    [[nodiscard]] const NVENC_FN_LIST& functions() const noexcept;

private:
#if defined(_WIN32)
    HMODULE _module{nullptr};
#else
    void* _module{nullptr};
#endif
    NVENC_FN_LIST _fn_list{};
    bool _loaded{false};
};

} // namespace moonshine::encoder::nvenc

namespace moonshine::encoder {
using nvenc::NvencApi;
} // namespace moonshine::encoder
