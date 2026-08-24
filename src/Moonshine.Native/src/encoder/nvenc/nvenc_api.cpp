#include "encoder/nvenc/nvenc_api.hpp"
#include <cstring>
#include <utility>

namespace moonshine::encoder::nvenc {

NvencApi::NvencApi() = default;

NvencApi::~NvencApi() {
    unload();
}

NvencApi::NvencApi(NvencApi&& other) noexcept
    : _module(other._module),
      _fn_list(other._fn_list),
      _loaded(other._loaded) {
    other._module = nullptr;
    std::memset(&other._fn_list, 0, sizeof(other._fn_list));
    other._loaded = false;
}

NvencApi& NvencApi::operator=(NvencApi&& other) noexcept {
    if (this != &other) {
        unload();
        _module = other._module;
        _fn_list = other._fn_list;
        _loaded = other._loaded;
        other._module = nullptr;
        std::memset(&other._fn_list, 0, sizeof(other._fn_list));
        other._loaded = false;
    }
    return *this;
}

bool NvencApi::load() {
    if (_loaded) {
        return true;
    }

#if defined(_WIN32)
    _module = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!_module) {
        _module = LoadLibraryW(L"nvEncodeAPI64.dll");
    }
    if (!_module) {
        return false;
    }

    auto create_instance = reinterpret_cast<NvEncodeAPICreateInstance_Fn>(
        GetProcAddress(_module, "NvEncodeAPICreateInstance")
    );
    if (!create_instance) {
        unload();
        return false;
    }

    std::memset(&_fn_list, 0, sizeof(_fn_list));
    _fn_list.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    if (create_instance(&_fn_list) != NV_ENC_SUCCESS) {
        unload();
        return false;
    }

    _loaded = true;
    return true;
#else
    return false;
#endif
}

void NvencApi::unload() {
#if defined(_WIN32)
    if (_module) {
        FreeLibrary(_module);
        _module = nullptr;
    }
#else
    _module = nullptr;
#endif
    std::memset(&_fn_list, 0, sizeof(_fn_list));
    _loaded = false;
}

bool NvencApi::is_loaded() const noexcept {
    return _loaded;
}

const NVENC_FN_LIST& NvencApi::functions() const noexcept {
    return _fn_list;
}

} // namespace moonshine::encoder::nvenc
