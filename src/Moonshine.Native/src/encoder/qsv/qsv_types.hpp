#pragma once

#include <cstdint>
#include <cstddef>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace moonshine::encoder::qsv {

#define MFX_MAKEFOURCC(a, b, c, d) \
    ((uint32_t)(uint8_t)(a) | ((uint32_t)(uint8_t)(b) << 8) | ((uint32_t)(uint8_t)(c) << 16) | ((uint32_t)(uint8_t)(d) << 24))

typedef int32_t mfxStatus;
typedef uint64_t mfxU64;
typedef uint32_t mfxU32;
typedef uint16_t mfxU16;
typedef uint8_t  mfxU8;
typedef int64_t  mfxI64;
typedef int32_t  mfxI32;
typedef int16_t  mfxI16;
typedef int8_t   mfxI8;

enum : int32_t {
    MFX_ERR_NONE                        = 0,
    MFX_ERR_UNKNOWN                     = -1,
    MFX_ERR_NULL_PTR                    = -2,
    MFX_ERR_UNSUPPORTED                 = -3,
    MFX_ERR_MEMORY_ALLOC                = -4,
    MFX_ERR_NOT_ENOUGH_BUFFER           = -5,
    MFX_ERR_INVALID_HANDLE              = -6,
    MFX_ERR_LOCK_MEMORY                 = -7,
    MFX_ERR_NOT_INITIALIZED             = -8,
    MFX_ERR_NOT_FOUND                   = -9,
    MFX_ERR_ALREADY_INITIALIZED         = -10,
    MFX_ERR_NOT_IMPLEMENTED             = -11,
    MFX_ERR_DEVICE_FAILED               = -16,
    MFX_ERR_DEVICE_LOST                 = -17,
    MFX_WRN_IN_EXECUTION                = 1,
    MFX_WRN_DEVICE_BUSY                 = 2,
    MFX_WRN_VIDEO_PARAM_CHANGED         = 3,
    MFX_WRN_PARTIAL_ACCELERATION        = 4,
    MFX_WRN_INCOMPATIBLE_VIDEO_PARAM    = 5,
    MFX_WRN_VALUE_NOT_CHANGED           = 6,
    MFX_ERR_MORE_DATA                   = 1,
    MFX_ERR_MORE_SURFACE                = 2
};

typedef uint32_t mfxIMPL;

enum : uint32_t {
    MFX_IMPL_TYPE_SOFTWARE              = 0x0001,
    MFX_IMPL_TYPE_HARDWARE              = 0x0002,
    MFX_IMPL_AUTO                       = 0x0000,
    MFX_IMPL_SOFTWARE                   = 0x0001,
    MFX_IMPL_HARDWARE                   = 0x0002,
    MFX_IMPL_AUTO_ANY                   = 0x0003,
    MFX_IMPL_HARDWARE_ANY               = 0x0004,
    MFX_IMPL_HARDWARE2                  = 0x0005,
    MFX_IMPL_HARDWARE3                  = 0x0006,
    MFX_IMPL_HARDWARE4                  = 0x0007,
    MFX_IMPL_VIA_D3D9                   = 0x0100,
    MFX_IMPL_VIA_D3D11                  = 0x0200,
    MFX_IMPL_VIA_VAAPI                  = 0x0300
};

enum : uint32_t {
    MFX_ACCEL_MODE_NA                   = 0,
    MFX_ACCEL_MODE_VIA_D3D9             = 1,
    MFX_ACCEL_MODE_VIA_D3D11            = 2,
    MFX_ACCEL_MODE_VIA_VAAPI            = 3
};

typedef int32_t mfxHandleType;

enum : int32_t {
    MFX_HANDLE_DIRECT3D_DEVICE_MANAGER9 = 1,
    MFX_HANDLE_D3D9_DEVICE              = 2,
    MFX_HANDLE_D3D11_DEVICE             = 3,
    MFX_HANDLE_VA_DISPLAY               = 4
};

enum : uint32_t {
    MFX_CODEC_AVC                       = MFX_MAKEFOURCC('A', 'V', 'C', ' '),
    MFX_CODEC_HEVC                      = MFX_MAKEFOURCC('H', 'E', 'V', 'C'),
    MFX_CODEC_JPEG                      = MFX_MAKEFOURCC('J', 'P', 'E', 'G'),
    MFX_CODEC_VP9                       = MFX_MAKEFOURCC('V', 'P', '9', ' '),
    MFX_CODEC_AV1                       = MFX_MAKEFOURCC('A', 'V', '0', '1')
};

enum : uint32_t {
    MFX_FOURCC_NV12                     = MFX_MAKEFOURCC('N', 'V', '1', '2'),
    MFX_FOURCC_YV12                     = MFX_MAKEFOURCC('Y', 'V', '1', '2'),
    MFX_FOURCC_RGB4                     = MFX_MAKEFOURCC('R', 'G', 'B', '4'),
    MFX_FOURCC_P010                     = MFX_MAKEFOURCC('P', '0', '1', '0'),
    MFX_FOURCC_AYUV                     = MFX_MAKEFOURCC('A', 'Y', 'U', 'V')
};

enum : uint16_t {
    MFX_RATECONTROL_CBR                 = 1,
    MFX_RATECONTROL_VBR                 = 2,
    MFX_RATECONTROL_CQP                 = 3,
    MFX_RATECONTROL_AVBR                = 4,
    MFX_RATECONTROL_LA                  = 8,
    MFX_RATECONTROL_ICQ                 = 9,
    MFX_RATECONTROL_VCM                 = 10,
    MFX_RATECONTROL_LA_ICQ              = 11,
    MFX_RATECONTROL_LA_HRD              = 12,
    MFX_RATECONTROL_QVBR                = 13
};

enum : uint16_t {
    MFX_TARGETUSAGE_BEST_QUALITY        = 1,
    MFX_TARGETUSAGE_BALANCED            = 4,
    MFX_TARGETUSAGE_BEST_SPEED          = 7
};

enum : uint16_t {
    MFX_IOPATTERN_IN_VIDEO_MEMORY       = 0x01,
    MFX_IOPATTERN_IN_SYSTEM_MEMORY      = 0x02,
    MFX_IOPATTERN_OUT_VIDEO_MEMORY      = 0x10,
    MFX_IOPATTERN_OUT_SYSTEM_MEMORY     = 0x20
};

enum : uint16_t {
    MFX_MEMTYPE_SYSTEM_MEMORY           = 0x0001,
    MFX_MEMTYPE_VIDEO_MEMORY_DECODER_TARGET = 0x0002,
    MFX_MEMTYPE_VIDEO_MEMORY_PROCESSOR_TARGET = 0x0004,
    MFX_MEMTYPE_FROM_ENCODE             = 0x0008,
    MFX_MEMTYPE_FROM_DECODE             = 0x0010,
    MFX_MEMTYPE_FROM_VPPIN              = 0x0020,
    MFX_MEMTYPE_FROM_VPPOUT             = 0x0040,
    MFX_MEMTYPE_INTERNAL_FRAME          = 0x0080,
    MFX_MEMTYPE_EXTERNAL_FRAME          = 0x0100,
    MFX_MEMTYPE_OPAQUE_FRAME            = 0x0400,
    MFX_MEMTYPE_SHARED_RESOURCE         = 0x0800,
    MFX_MEMTYPE_DXVA2_DECODER_TARGET    = 0x1000,
    MFX_MEMTYPE_DXVA2_PROCESSOR_TARGET  = 0x2000,
    MFX_MEMTYPE_D3D11_MEMORY_BIND_RENDER_TARGET = 0x4000
};

enum : uint16_t {
    MFX_FRAMETYPE_UNKNOWN               = 0x0000,
    MFX_FRAMETYPE_I                     = 0x0001,
    MFX_FRAMETYPE_P                     = 0x0002,
    MFX_FRAMETYPE_B                     = 0x0004,
    MFX_FRAMETYPE_REF                   = 0x0040,
    MFX_FRAMETYPE_IDR                   = 0x0080
};

#pragma pack(push, 8)
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4201)
#endif

union mfxVersion {
    struct {
        uint16_t Minor;
        uint16_t Major;
    };
    uint32_t Version;
};

struct mfxExtBuffer {
    uint32_t BufferId;
    uint32_t BufferSz;
};

struct mfxFrameInfo {
    uint32_t reserved[4];
    uint16_t nChannel;
    uint16_t BitDepthLuma;
    uint16_t BitDepthChroma;
    uint16_t Shift;
    mfxVersion Version;
    uint32_t FourCC;
    uint16_t Width;
    uint16_t Height;
    uint16_t CropX;
    uint16_t CropY;
    uint16_t CropW;
    uint16_t CropH;
    uint32_t FrameRateExtN;
    uint32_t FrameRateExtD;
    uint32_t AspectRatioW;
    uint32_t AspectRatioH;
    uint16_t PicStruct;
    uint16_t ChromaFormat;
    uint16_t reserved2;
};

struct mfxFrameData {
    uint32_t reserved[4];
    mfxVersion Version;
    uint16_t PitchHigh;
    uint64_t TimeStamp;
    uint16_t Locked;
    uint16_t Pitch;
    uint16_t PitchLow;
    uint8_t* Y;
    uint8_t* UV;
    uint8_t* V;
    uint8_t* A;
    void* MemId;
    uint16_t Corrupted;
    uint16_t MemType;
};

struct mfxFrameSurface1 {
    uint32_t reserved[4];
    mfxVersion Version;
    mfxFrameInfo Info;
    mfxFrameData Data;
};

struct mfxInfoMFX {
    uint32_t reserved[7];
    uint16_t LowPower;
    uint16_t BRCParamMultiplier;
    mfxFrameInfo FrameInfo;
    uint32_t CodecId;
    uint16_t CodecProfile;
    uint16_t CodecLevel;
    uint16_t NumThread;
    uint16_t TargetUsage;
    uint16_t GopPicSize;
    uint16_t GopRefDist;
    uint16_t GopOptFlag;
    uint16_t IdrInterval;
    uint16_t RateControlMethod;
    union {
        uint16_t InitialDelayInKB;
        uint16_t QPI;
        uint16_t Accuracy;
    };
    uint16_t BufferSizeInKB;
    union {
        uint16_t TargetKbps;
        uint16_t QPP;
        uint16_t ICQQuality;
    };
    union {
        uint16_t MaxKbps;
        uint16_t QPB;
        uint16_t Convergence;
    };
    uint16_t MinKbps;
    uint16_t NumSlice;
    uint16_t EncodedOrder;
};

struct mfxVideoParam {
    uint32_t AllocId;
    uint32_t reserved[2];
    uint16_t AsyncDepth;
    mfxInfoMFX mfx;
    uint16_t Protected;
    uint16_t IOPattern;
    mfxExtBuffer** ExtParam;
    uint16_t NumExtParam;
    uint16_t reserved2;
};

struct mfxBitstream {
    uint32_t EncryptedData;
    mfxExtBuffer** ExtParam;
    uint16_t NumExtParam;
    mfxVersion Version;
    uint32_t reserved[2];
    int64_t DecodeTimeStamp;
    uint64_t TimeStamp;
    uint8_t* Data;
    uint32_t DataOffset;
    uint32_t DataLength;
    uint32_t MaxLength;
    uint16_t PicStruct;
    uint16_t FrameType;
    uint16_t DataFlag;
    uint16_t reserved2;
    uint32_t CodecId;
    uint32_t reserved3;
};

struct mfxInitParam {
    mfxIMPL Implementation;
    mfxVersion Version;
    uint16_t ExternalAllocators;
    mfxExtBuffer** ExtParam;
    uint16_t NumExtParam;
    uint16_t GPUCopy;
    uint32_t reserved[3];
};

struct mfxEncodeCtrl {
    mfxExtBuffer** ExtParam;
    uint16_t NumExtParam;
    uint16_t QP;
    uint16_t FrameType;
    uint16_t MfxReserved1;
    uint32_t MfxReserved[4];
};

struct mfxExtCodingOption {
    mfxExtBuffer Header;
    uint32_t reserved[4];
    uint16_t RateDistortionOpt;
    uint16_t MECost;
    uint16_t MESearchType;
    uint16_t MFR;
    uint16_t MVRange;
    uint16_t MaxDecFrameBuffering;
    uint16_t AUDelimiter;
    uint16_t EndOfSequence;
    uint16_t EndOfStream;
    uint16_t ResetRefList;
    uint16_t SingleSeiSps;
    uint16_t VuiVclHrdFlag;
    uint16_t PicTimingSEI;
    uint16_t VuiNalHrdFlag;
    uint16_t IntraPred;
    uint16_t InterPred;
    uint16_t CAVLC;
    uint16_t FieldOutput;
};

struct mfxExtCodingOption2 {
    mfxExtBuffer Header;
    uint16_t IntRefType;
    uint16_t IntRefCycleSize;
    int16_t  IntRefQPDelta;
    uint16_t MaxSliceSize;
    uint16_t MaxFrameSize;
    uint16_t MaxFrameSizeI;
    uint16_t MaxFrameSizeP;
    uint16_t BitrateLimit;
    uint16_t MBBRC;
    uint16_t ExtBRC;
    uint16_t LookAheadDepth;
    uint16_t Trellis;
    uint16_t RepeatPPS;
    uint16_t BRefType;
    uint16_t AdaptiveI;
    uint16_t AdaptiveB;
    uint16_t LookAheadDS;
    uint16_t NumMbPerSlice;
    uint16_t SkipFrame;
    uint16_t MinQPI;
    uint16_t MaxQPI;
    uint16_t MinQPP;
    uint16_t MaxQPP;
    uint16_t MinQPB;
    uint16_t MaxQPB;
};

#define MFX_EXTBUFF_CODING_OPTION   MFX_MAKEFOURCC('C', 'O', 'P', 'T')
#define MFX_EXTBUFF_CODING_OPTION2  MFX_MAKEFOURCC('C', 'O', 'P', '2')

#pragma pack(pop)
#if defined(_MSC_VER)
#pragma warning(pop)
#endif

typedef void* mfxHDL;
typedef void* mfxMemId;

#ifndef MFX_INFINITE
#define MFX_INFINITE 0xFFFFFFFF
#endif

struct mfxHDLPair {
    mfxHDL first;  // ID3D11Texture2D*
    mfxHDL second; // Subresource index (e.g. 0 or (mfxHDL)(uintptr_t)MFX_INFINITE)
};

typedef struct _mfxSession* mfxSession;
typedef void* mfxSyncPoint;
typedef void* mfxLoader;
typedef void* mfxConfig;

enum mfxVariantType : uint32_t {
    MFX_VARIANT_TYPE_UNSET = 0,
    MFX_VARIANT_TYPE_U8    = 1,
    MFX_VARIANT_TYPE_I8    = 2,
    MFX_VARIANT_TYPE_U16   = 3,
    MFX_VARIANT_TYPE_I16   = 4,
    MFX_VARIANT_TYPE_U32   = 5,
    MFX_VARIANT_TYPE_I32   = 6,
    MFX_VARIANT_TYPE_U64   = 7,
    MFX_VARIANT_TYPE_I64   = 8,
    MFX_VARIANT_TYPE_F32   = 9,
    MFX_VARIANT_TYPE_F64   = 10,
    MFX_VARIANT_TYPE_PTR   = 11
};

#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4201)
#endif
#pragma pack(push, 8)
struct mfxVariant {
    mfxVersion Version;
    mfxVariantType Type;
    union {
        uint8_t   U8;
        int8_t    I8;
        uint16_t  U16;
        int16_t   I16;
        uint32_t  U32;
        int32_t   I32;
        uint64_t  U64;
        int64_t   I64;
        float     F32;
        double    F64;
        void*     Ptr;
    } Data;
};
#pragma pack(pop)
#if defined(_MSC_VER)
#pragma warning(pop)
#endif

// oneVPL 2.x Modern Dispatcher Prototypes
typedef mfxLoader (WINAPI *MFXLoad_Fn)();
typedef void (WINAPI *MFXUnload_Fn)(mfxLoader loader);
typedef mfxConfig (WINAPI *MFXCreateConfig_Fn)(mfxLoader loader);
typedef mfxStatus (WINAPI *MFXSetConfigFilterProperty_Fn)(mfxConfig config, const uint8_t* name, mfxVariant value);
typedef mfxStatus (WINAPI *MFXCreateSession_Fn)(mfxLoader loader, uint32_t index, mfxSession* session);
typedef mfxStatus (WINAPI *MFXEnumImplementations_Fn)(mfxLoader loader, uint32_t index, uint32_t format, void** implDesc);
typedef mfxStatus (WINAPI *MFXDispReleaseImplDescription_Fn)(mfxLoader loader, void* implDesc);

// Legacy MFX Function Prototypes
typedef mfxStatus (WINAPI *MFXInitEx_Fn)(mfxInitParam par, mfxSession* session);
typedef mfxStatus (WINAPI *MFXClose_Fn)(mfxSession session);
typedef mfxStatus (WINAPI *MFXQueryVersion_Fn)(mfxSession session, mfxVersion* version);
typedef mfxStatus (WINAPI *MFXVideoCORE_SetHandle_Fn)(mfxSession session, mfxHandleType type, void* hdl);
typedef mfxStatus (WINAPI *MFXVideoCORE_SyncOperation_Fn)(mfxSession session, mfxSyncPoint sync, uint32_t wait);

typedef mfxStatus (WINAPI *MFXVideoENCODE_Query_Fn)(mfxSession session, mfxVideoParam* in, mfxVideoParam* out);
typedef mfxStatus (WINAPI *MFXVideoENCODE_QueryIOSurf_Fn)(mfxSession session, mfxVideoParam* par, struct mfxFrameAllocRequest* request);
typedef mfxStatus (WINAPI *MFXVideoENCODE_Init_Fn)(mfxSession session, mfxVideoParam* par);
typedef mfxStatus (WINAPI *MFXVideoENCODE_Reset_Fn)(mfxSession session, mfxVideoParam* par);
typedef mfxStatus (WINAPI *MFXVideoENCODE_Close_Fn)(mfxSession session);
typedef mfxStatus (WINAPI *MFXVideoENCODE_GetVideoParam_Fn)(mfxSession session, mfxVideoParam* par);
typedef mfxStatus (WINAPI *MFXVideoENCODE_EncodeFrameAsync_Fn)(mfxSession session, struct mfxEncodeCtrl* ctrl, mfxFrameSurface1* surface, mfxBitstream* bs, mfxSyncPoint* syncp);

} // namespace moonshine::encoder::qsv
