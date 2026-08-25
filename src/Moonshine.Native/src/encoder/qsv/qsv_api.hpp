#pragma once

#include "encoder/qsv/qsv_types.hpp"
#include <string>

namespace moonshine::encoder::qsv {

class QsvApi {
public:
    QsvApi();
    ~QsvApi();

    QsvApi(const QsvApi&) = delete;
    QsvApi& operator=(const QsvApi&) = delete;

    QsvApi(QsvApi&& other) noexcept;
    QsvApi& operator=(QsvApi&& other) noexcept;

    bool load();
    void unload();
    [[nodiscard]] bool is_loaded() const noexcept;
    [[nodiscard]] bool is_vpl() const noexcept;
    [[nodiscard]] mfxVersion version() const noexcept;
    [[nodiscard]] const std::string& resolved_dll_name() const noexcept;

    // oneVPL 2.x Function pointers
    MFXLoad_Fn MFXLoad{nullptr};
    MFXUnload_Fn MFXUnload{nullptr};
    MFXCreateConfig_Fn MFXCreateConfig{nullptr};
    MFXSetConfigFilterProperty_Fn MFXSetConfigFilterProperty{nullptr};
    MFXCreateSession_Fn MFXCreateSession{nullptr};
    MFXEnumImplementations_Fn MFXEnumImplementations{nullptr};
    MFXDispReleaseImplDescription_Fn MFXDispReleaseImplDescription{nullptr};

    // Core & Encoder Function pointers
    MFXInitEx_Fn MFXInitEx{nullptr};
    MFXClose_Fn MFXClose{nullptr};
    MFXQueryVersion_Fn MFXQueryVersion{nullptr};
    MFXVideoCORE_SetHandle_Fn MFXVideoCORE_SetHandle{nullptr};
    MFXVideoCORE_SyncOperation_Fn MFXVideoCORE_SyncOperation{nullptr};

    MFXVideoENCODE_Query_Fn MFXVideoENCODE_Query{nullptr};
    MFXVideoENCODE_QueryIOSurf_Fn MFXVideoENCODE_QueryIOSurf{nullptr};
    MFXVideoENCODE_Init_Fn MFXVideoENCODE_Init{nullptr};
    MFXVideoENCODE_Reset_Fn MFXVideoENCODE_Reset{nullptr};
    MFXVideoENCODE_Close_Fn MFXVideoENCODE_Close{nullptr};
    MFXVideoENCODE_GetVideoParam_Fn MFXVideoENCODE_GetVideoParam{nullptr};
    MFXVideoENCODE_EncodeFrameAsync_Fn MFXVideoENCODE_EncodeFrameAsync{nullptr};

private:
#if defined(_WIN32)
    HMODULE _module{nullptr};
#else
    void* _module{nullptr};
#endif
    mfxVersion _version{};
    bool _loaded{false};
    bool _is_vpl{false};
    std::string _resolved_dll_name;
};

} // namespace moonshine::encoder::qsv
