#pragma once

#include "encoder/amf/amf_types.hpp"

namespace moonshine::encoder::amf {

class AmfApi {
public:
    AmfApi();
    ~AmfApi();

    AmfApi(const AmfApi&) = delete;
    AmfApi& operator=(const AmfApi&) = delete;

    AmfApi(AmfApi&& other) noexcept;
    AmfApi& operator=(AmfApi&& other) noexcept;

    bool load();
    void unload();
    [[nodiscard]] bool is_loaded() const noexcept;
    [[nodiscard]] AMFFactory* factory() const noexcept;
    [[nodiscard]] amf_uint64 version() const noexcept;

private:
#if defined(_WIN32)
    HMODULE _module{nullptr};
#else
    void* _module{nullptr};
#endif
    AMFFactory* _factory{nullptr};
    amf_uint64 _version{0};
    bool _loaded{false};
};

} // namespace moonshine::encoder::amf
