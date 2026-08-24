#include "encoder/qsv/qsv_api.hpp"
#include <utility>
#include <string>

namespace moonshine::encoder::qsv {

QsvApi::QsvApi() = default;

QsvApi::~QsvApi() {
    unload();
}

QsvApi::QsvApi(QsvApi&& other) noexcept
    : MFXLoad(other.MFXLoad),
      MFXUnload(other.MFXUnload),
      MFXCreateConfig(other.MFXCreateConfig),
      MFXSetConfigFilterProperty(other.MFXSetConfigFilterProperty),
      MFXCreateSession(other.MFXCreateSession),
      MFXDispReleaseImplDescription(other.MFXDispReleaseImplDescription),
      MFXInitEx(other.MFXInitEx),
      MFXClose(other.MFXClose),
      MFXQueryVersion(other.MFXQueryVersion),
      MFXVideoCORE_SetHandle(other.MFXVideoCORE_SetHandle),
      MFXVideoCORE_SyncOperation(other.MFXVideoCORE_SyncOperation),
      MFXVideoENCODE_Query(other.MFXVideoENCODE_Query),
      MFXVideoENCODE_QueryIOSurf(other.MFXVideoENCODE_QueryIOSurf),
      MFXVideoENCODE_Init(other.MFXVideoENCODE_Init),
      MFXVideoENCODE_Reset(other.MFXVideoENCODE_Reset),
      MFXVideoENCODE_Close(other.MFXVideoENCODE_Close),
      MFXVideoENCODE_GetVideoParam(other.MFXVideoENCODE_GetVideoParam),
      MFXVideoENCODE_EncodeFrameAsync(other.MFXVideoENCODE_EncodeFrameAsync),
      _module(other._module),
      _version(other._version),
      _loaded(other._loaded),
      _is_vpl(other._is_vpl) {
    other._module = nullptr;
    other._loaded = false;
    other._is_vpl = false;
    other.MFXLoad = nullptr;
    other.MFXUnload = nullptr;
    other.MFXCreateConfig = nullptr;
    other.MFXSetConfigFilterProperty = nullptr;
    other.MFXCreateSession = nullptr;
    other.MFXDispReleaseImplDescription = nullptr;
    other.MFXInitEx = nullptr;
    other.MFXClose = nullptr;
    other.MFXQueryVersion = nullptr;
    other.MFXVideoCORE_SetHandle = nullptr;
    other.MFXVideoCORE_SyncOperation = nullptr;
    other.MFXVideoENCODE_Query = nullptr;
    other.MFXVideoENCODE_QueryIOSurf = nullptr;
    other.MFXVideoENCODE_Init = nullptr;
    other.MFXVideoENCODE_Reset = nullptr;
    other.MFXVideoENCODE_Close = nullptr;
    other.MFXVideoENCODE_GetVideoParam = nullptr;
    other.MFXVideoENCODE_EncodeFrameAsync = nullptr;
}

QsvApi& QsvApi::operator=(QsvApi&& other) noexcept {
    if (this != &other) {
        unload();
        _module = other._module;
        _version = other._version;
        _loaded = other._loaded;
        _is_vpl = other._is_vpl;
        MFXLoad = other.MFXLoad;
        MFXUnload = other.MFXUnload;
        MFXCreateConfig = other.MFXCreateConfig;
        MFXSetConfigFilterProperty = other.MFXSetConfigFilterProperty;
        MFXCreateSession = other.MFXCreateSession;
        MFXDispReleaseImplDescription = other.MFXDispReleaseImplDescription;
        MFXInitEx = other.MFXInitEx;
        MFXClose = other.MFXClose;
        MFXQueryVersion = other.MFXQueryVersion;
        MFXVideoCORE_SetHandle = other.MFXVideoCORE_SetHandle;
        MFXVideoCORE_SyncOperation = other.MFXVideoCORE_SyncOperation;
        MFXVideoENCODE_Query = other.MFXVideoENCODE_Query;
        MFXVideoENCODE_QueryIOSurf = other.MFXVideoENCODE_QueryIOSurf;
        MFXVideoENCODE_Init = other.MFXVideoENCODE_Init;
        MFXVideoENCODE_Reset = other.MFXVideoENCODE_Reset;
        MFXVideoENCODE_Close = other.MFXVideoENCODE_Close;
        MFXVideoENCODE_GetVideoParam = other.MFXVideoENCODE_GetVideoParam;
        MFXVideoENCODE_EncodeFrameAsync = other.MFXVideoENCODE_EncodeFrameAsync;

        other._module = nullptr;
        other._loaded = false;
        other._is_vpl = false;
        other.MFXLoad = nullptr;
        other.MFXUnload = nullptr;
        other.MFXCreateConfig = nullptr;
        other.MFXSetConfigFilterProperty = nullptr;
        other.MFXCreateSession = nullptr;
        other.MFXDispReleaseImplDescription = nullptr;
        other.MFXInitEx = nullptr;
        other.MFXClose = nullptr;
        other.MFXQueryVersion = nullptr;
        other.MFXVideoCORE_SetHandle = nullptr;
        other.MFXVideoCORE_SyncOperation = nullptr;
        other.MFXVideoENCODE_Query = nullptr;
        other.MFXVideoENCODE_QueryIOSurf = nullptr;
        other.MFXVideoENCODE_Init = nullptr;
        other.MFXVideoENCODE_Reset = nullptr;
        other.MFXVideoENCODE_Close = nullptr;
        other.MFXVideoENCODE_GetVideoParam = nullptr;
        other.MFXVideoENCODE_EncodeFrameAsync = nullptr;
    }
    return *this;
}

bool QsvApi::load() {
    if (_loaded) {
        return true;
    }

#if defined(_WIN32)
    static std::wstring s_resolved_dll;
    static bool s_probe_done = false;

    if (!s_probe_done) {
        const wchar_t* dll_names[] = {
            L"vpl.dll",
            L"libvpl.dll",
            L"mfx64.dll",
            L"libmfx64.dll"
        };

        for (const auto* name : dll_names) {
            HMODULE mod = LoadLibraryExW(name, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (mod) {
                s_resolved_dll = name;
                FreeLibrary(mod);
                break;
            }
        }
        s_probe_done = true;
    }

    if (s_resolved_dll.empty()) {
        return false;
    }

    _module = LoadLibraryExW(s_resolved_dll.c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!_module) {
        return false;
    }

    // oneVPL 2.x modern dispatcher symbols
    MFXLoad = reinterpret_cast<MFXLoad_Fn>(GetProcAddress(_module, "MFXLoad"));
    MFXUnload = reinterpret_cast<MFXUnload_Fn>(GetProcAddress(_module, "MFXUnload"));
    MFXCreateConfig = reinterpret_cast<MFXCreateConfig_Fn>(GetProcAddress(_module, "MFXCreateConfig"));
    MFXSetConfigFilterProperty = reinterpret_cast<MFXSetConfigFilterProperty_Fn>(GetProcAddress(_module, "MFXSetConfigFilterProperty"));
    MFXCreateSession = reinterpret_cast<MFXCreateSession_Fn>(GetProcAddress(_module, "MFXCreateSession"));
    MFXDispReleaseImplDescription = reinterpret_cast<MFXDispReleaseImplDescription_Fn>(GetProcAddress(_module, "MFXDispReleaseImplDescription"));

    // Legacy MSDK symbols
    MFXInitEx = reinterpret_cast<MFXInitEx_Fn>(GetProcAddress(_module, "MFXInitEx"));
    MFXClose = reinterpret_cast<MFXClose_Fn>(GetProcAddress(_module, "MFXClose"));
    MFXQueryVersion = reinterpret_cast<MFXQueryVersion_Fn>(GetProcAddress(_module, "MFXQueryVersion"));
    MFXVideoCORE_SetHandle = reinterpret_cast<MFXVideoCORE_SetHandle_Fn>(GetProcAddress(_module, "MFXVideoCORE_SetHandle"));
    MFXVideoCORE_SyncOperation = reinterpret_cast<MFXVideoCORE_SyncOperation_Fn>(GetProcAddress(_module, "MFXVideoCORE_SyncOperation"));

    MFXVideoENCODE_Query = reinterpret_cast<MFXVideoENCODE_Query_Fn>(GetProcAddress(_module, "MFXVideoENCODE_Query"));
    MFXVideoENCODE_QueryIOSurf = reinterpret_cast<MFXVideoENCODE_QueryIOSurf_Fn>(GetProcAddress(_module, "MFXVideoENCODE_QueryIOSurf"));
    MFXVideoENCODE_Init = reinterpret_cast<MFXVideoENCODE_Init_Fn>(GetProcAddress(_module, "MFXVideoENCODE_Init"));
    MFXVideoENCODE_Reset = reinterpret_cast<MFXVideoENCODE_Reset_Fn>(GetProcAddress(_module, "MFXVideoENCODE_Reset"));
    MFXVideoENCODE_Close = reinterpret_cast<MFXVideoENCODE_Close_Fn>(GetProcAddress(_module, "MFXVideoENCODE_Close"));
    MFXVideoENCODE_GetVideoParam = reinterpret_cast<MFXVideoENCODE_GetVideoParam_Fn>(GetProcAddress(_module, "MFXVideoENCODE_GetVideoParam"));
    MFXVideoENCODE_EncodeFrameAsync = reinterpret_cast<MFXVideoENCODE_EncodeFrameAsync_Fn>(GetProcAddress(_module, "MFXVideoENCODE_EncodeFrameAsync"));

    if (MFXLoad && MFXUnload && MFXCreateConfig && MFXSetConfigFilterProperty && MFXCreateSession) {
        _is_vpl = true;
    }

    if (!_is_vpl && !MFXInitEx) {
        unload();
        return false;
    }

    if (!MFXClose || !MFXVideoENCODE_Init || !MFXVideoENCODE_EncodeFrameAsync || !MFXVideoCORE_SyncOperation) {
        unload();
        return false;
    }

    _loaded = true;
    return true;
#else
    return false;
#endif
}

void QsvApi::unload() {
#if defined(_WIN32)
    if (_module) {
        FreeLibrary(_module);
        _module = nullptr;
    }
#else
    _module = nullptr;
#endif
    MFXLoad = nullptr;
    MFXUnload = nullptr;
    MFXCreateConfig = nullptr;
    MFXSetConfigFilterProperty = nullptr;
    MFXCreateSession = nullptr;
    MFXDispReleaseImplDescription = nullptr;
    MFXInitEx = nullptr;
    MFXClose = nullptr;
    MFXQueryVersion = nullptr;
    MFXVideoCORE_SetHandle = nullptr;
    MFXVideoCORE_SyncOperation = nullptr;
    MFXVideoENCODE_Query = nullptr;
    MFXVideoENCODE_QueryIOSurf = nullptr;
    MFXVideoENCODE_Init = nullptr;
    MFXVideoENCODE_Reset = nullptr;
    MFXVideoENCODE_Close = nullptr;
    MFXVideoENCODE_GetVideoParam = nullptr;
    MFXVideoENCODE_EncodeFrameAsync = nullptr;
    _version = {};
    _loaded = false;
    _is_vpl = false;
}

bool QsvApi::is_loaded() const noexcept {
    return _loaded;
}

bool QsvApi::is_vpl() const noexcept {
    return _is_vpl;
}

mfxVersion QsvApi::version() const noexcept {
    return _version;
}

} // namespace moonshine::encoder::qsv
