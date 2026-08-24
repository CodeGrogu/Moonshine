#pragma once

#include "encoder/qsv/qsv_types.hpp"

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
    [[nodiscard]] mfxVersion version() const noexcept;

    // Function pointers
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
};

} // namespace moonshine::encoder::qsv
