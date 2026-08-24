#include "encoder/amf/amf_api.hpp"
#include <utility>

namespace moonshine::encoder::amf {

AmfApi::AmfApi() = default;

AmfApi::~AmfApi() {
    unload();
}

AmfApi::AmfApi(AmfApi&& other) noexcept
    : _module(other._module),
      _factory(other._factory),
      _version(other._version),
      _loaded(other._loaded) {
    other._module = nullptr;
    other._factory = nullptr;
    other._version = 0;
    other._loaded = false;
}

AmfApi& AmfApi::operator=(AmfApi&& other) noexcept {
    if (this != &other) {
        unload();
        _module = other._module;
        _factory = other._factory;
        _version = other._version;
        _loaded = other._loaded;
        other._module = nullptr;
        other._factory = nullptr;
        other._version = 0;
        other._loaded = false;
    }
    return *this;
}

bool AmfApi::load() {
    if (_loaded) {
        return true;
    }

#if defined(_WIN32)
    _module = LoadLibraryExW(L"amfrt64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!_module) {
        _module = LoadLibraryW(L"amfrt64.dll");
    }
    if (!_module) {
        return false;
    }

    auto queryVersion = reinterpret_cast<AMFQueryVersion_Fn>(
        GetProcAddress(_module, "AMFQueryVersion")
    );
    auto initFn = reinterpret_cast<AMFInit_Fn>(
        GetProcAddress(_module, "AMFInit")
    );

    if (!queryVersion || !initFn) {
        unload();
        return false;
    }

    if (queryVersion(&_version) != AMF_OK) {
        unload();
        return false;
    }

    AMFFactory* pFactory = nullptr;
    if (initFn(AMF_FULL_VERSION, &pFactory) != AMF_OK || !pFactory) {
        unload();
        return false;
    }

    _factory = pFactory;
    _loaded = true;
    return true;
#else
    return false;
#endif
}

void AmfApi::unload() {
#if defined(_WIN32)
    if (_module) {
        FreeLibrary(_module);
        _module = nullptr;
    }
#else
    _module = nullptr;
#endif
    _factory = nullptr;
    _version = 0;
    _loaded = false;
}

bool AmfApi::is_loaded() const noexcept {
    return _loaded;
}

AMFFactory* AmfApi::factory() const noexcept {
    return _factory;
}

amf_uint64 AmfApi::version() const noexcept {
    return _version;
}

} // namespace moonshine::encoder::amf
