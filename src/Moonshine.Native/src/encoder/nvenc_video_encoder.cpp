#include "moonshine/encoder/nvenc_video_encoder.hpp"
#include <cstring>
#include <chrono>
#include <iostream>

#if defined(_WIN32)
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

// GUID definitions for NVENC Codecs and Presets
static const GUID NV_ENC_CODEC_H264_GUID_LOCAL =
    { 0x6bc82762, 0x4e63, 0x4ca4, { 0xaa, 0x85, 0x1e, 0x50, 0xf3, 0x21, 0xf6, 0xbf } };
static const GUID NV_ENC_CODEC_HEVC_GUID_LOCAL =
    { 0x790cdc88, 0x4522, 0x4d7b, { 0x94, 0x25, 0xbd, 0xa9, 0x97, 0x5f, 0x76, 0x03 } };
static const GUID NV_ENC_CODEC_AV1_GUID_LOCAL =
    { 0x0a352289, 0x0aa7, 0x4759, { 0x86, 0x2d, 0x5d, 0x15, 0xcd, 0x16, 0xd2, 0x54 } };
static const GUID NV_ENC_CODEC_PROFILE_AUTOSELECT_GUID_LOCAL =
    { 0xbfd6f8e7, 0x233c, 0x4341, { 0x8b, 0x3e, 0x48, 0x18, 0x52, 0x38, 0x03, 0xf4 } };
static const GUID NV_ENC_H264_PROFILE_HIGH_GUID_LOCAL =
    { 0xe7cbc309, 0x4f7a, 0x4b89, { 0xaf, 0x2a, 0xd5, 0x37, 0xc9, 0x2b, 0xe3, 0x10 } };
static const GUID NV_ENC_HEVC_PROFILE_MAIN_GUID_LOCAL =
    { 0xb514c39a, 0xb55b, 0x40fa, { 0x87, 0x8f, 0xf1, 0x25, 0x3b, 0x4d, 0xfd, 0xec } };
static const GUID NV_ENC_HEVC_PROFILE_MAIN10_GUID_LOCAL =
    { 0xfa4d2b6c, 0x3a5b, 0x411a, { 0x80, 0x18, 0x0a, 0x3f, 0x5e, 0x3c, 0x9b, 0xe5 } };
static const GUID NV_ENC_AV1_PROFILE_MAIN_GUID_LOCAL =
    { 0x5f2a39f5, 0xf14e, 0x4f95, { 0x9a, 0x9e, 0xb7, 0x6d, 0x56, 0x8f, 0xcf, 0x97 } };

static const GUID NV_ENC_PRESET_P1_GUID_LOCAL =
    { 0xfc0a8d3e, 0x45f8, 0x4cf8, { 0x80, 0xc7, 0x29, 0x88, 0x71, 0x59, 0x0e, 0xbf } };
static const GUID NV_ENC_PRESET_P2_GUID_LOCAL =
    { 0xf581cfb8, 0x88d6, 0x4381, { 0x93, 0xf0, 0xdf, 0x13, 0xf9, 0xc2, 0x7d, 0xab } };
static const GUID NV_ENC_PRESET_P3_GUID_LOCAL =
    { 0x36850110, 0x3a07, 0x441f, { 0x94, 0xd5, 0x36, 0x70, 0x63, 0x1f, 0x91, 0xf6 } };
static const GUID NV_ENC_PRESET_P4_GUID_LOCAL =
    { 0x90a7b826, 0xdf06, 0x4862, { 0xb9, 0xd2, 0xcd, 0x6d, 0x73, 0xa0, 0x86, 0x81 } };
static const GUID NV_ENC_PRESET_P5_GUID_LOCAL =
    { 0x21c6e6b4, 0x297a, 0x4cba, { 0x99, 0x8f, 0xb6, 0xcb, 0xde, 0x72, 0xad, 0xe3 } };
static const GUID NV_ENC_PRESET_P6_GUID_LOCAL =
    { 0x8e75c279, 0x6299, 0x4ab6, { 0x83, 0x02, 0x0b, 0x21, 0x5a, 0x33, 0x5c, 0xf5 } };
static const GUID NV_ENC_PRESET_P7_GUID_LOCAL =
    { 0x84848c12, 0x6f71, 0x4c13, { 0x93, 0x1b, 0x53, 0xe2, 0x83, 0xf5, 0x79, 0x74 } };

#define NVENCAPI __stdcall
typedef int32_t NVENCSTATUS;
#define NV_ENC_SUCCESS 0

#define NVENCAPI_MAJOR_VERSION 13
#define NVENCAPI_MINOR_VERSION 1
#define NVENCAPI_VERSION (NVENCAPI_MAJOR_VERSION | (NVENCAPI_MINOR_VERSION << 24))
#define NVENCAPI_STRUCT_VERSION(ver) ((uint32_t)NVENCAPI_VERSION | ((ver) << 16) | (0x7 << 28))

#define NVENC_INFINITE_GOPLENGTH 0xffffffff

#define NV_ENC_DEVICE_TYPE_DIRECTX 0x0
#define NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX 0x0
#define NV_ENC_INPUT_IMAGE 0x0

#define NV_ENC_PARAMS_FRAME_FIELD_MODE_FRAME 0x01
#define NV_ENC_PARAMS_RC_CONSTQP 0x0
#define NV_ENC_PARAMS_RC_VBR 0x1
#define NV_ENC_PARAMS_RC_CBR 0x2

#define NV_ENC_PIC_FLAG_FORCEIDR 0x2
#define NV_ENC_PIC_FLAG_OUTPUT_SPSPPS 0x4
#define NV_ENC_PIC_STRUCT_FRAME 0x01

#define NV_ENC_PIC_TYPE_P 0x0
#define NV_ENC_PIC_TYPE_B 0x01
#define NV_ENC_PIC_TYPE_I 0x02
#define NV_ENC_PIC_TYPE_IDR 0x03

#define NV_ENC_BUFFER_FORMAT_NV12 0x00000001
#define NV_ENC_BUFFER_FORMAT_P010 0x00000040
#define NV_ENC_BUFFER_FORMAT_ARGB 0x01000000
#define NV_ENC_BUFFER_FORMAT_ABGR 0x10000000
#define NV_ENC_BUFFER_FORMAT_ABGR10 0x20000000

#define NV_ENC_TUNING_INFO_HIGH_QUALITY 1
#define NV_ENC_TUNING_INFO_LOW_LATENCY 2
#define NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY 3
#define NV_ENC_TUNING_INFO_LOSSLESS 4

typedef struct _NV_ENC_QP {
    uint32_t qpInterP;
    uint32_t qpInterB;
    uint32_t qpIntra;
} NV_ENC_QP;

typedef struct _NV_ENC_RC_PARAMS {
    uint32_t version;
    uint32_t rateControlMode;
    NV_ENC_QP constQP;
    uint32_t averageBitRate;
    uint32_t maxBitRate;
    uint32_t vbvBufferSize;
    uint32_t vbvInitialDelay;
    uint32_t enableMinQP : 1;
    uint32_t enableMaxQP : 1;
    uint32_t enableInitialRCQP : 1;
    uint32_t enableAQ : 1;
    uint32_t reservedBitField1 : 1;
    uint32_t enableLookahead : 1;
    uint32_t disableIadapt : 1;
    uint32_t disableBadapt : 1;
    uint32_t enableTemporalAQ : 1;
    uint32_t zeroReorderDelay : 1;
    uint32_t enableNonRefP : 1;
    uint32_t strictGOPTarget : 1;
    uint32_t aqStrength : 4;
    uint32_t enableExtLookahead : 1;
    uint32_t reservedBitFields : 15;
    NV_ENC_QP minQP;
    NV_ENC_QP maxQP;
    NV_ENC_QP initialRCQP;
    uint32_t temporallayerIdxMask;
    uint8_t temporalLayerQP[8];
    uint8_t targetQuality;
    uint8_t targetQualityLSB;
    uint16_t lookaheadDepth;
    uint8_t lowDelayKeyFrameScale;
    int8_t yDcQPIndexOffset;
    int8_t uDcQPIndexOffset;
    int8_t vDcQPIndexOffset;
    uint32_t qpMapMode;
    uint32_t multiPass;
    uint32_t alphaLayerBitrateRatio;
    int8_t cbQPIndexOffset;
    int8_t crQPIndexOffset;
    uint16_t reserved2;
    uint32_t lookaheadLevel;
    uint8_t viewBitrateRatios[7];
    uint8_t reserved3;
    uint32_t reserved1;
} NV_ENC_RC_PARAMS;

#define NV_ENC_RC_PARAMS_VER NVENCAPI_STRUCT_VERSION(1)

typedef struct _NV_ENC_CONFIG_H264 {
    uint32_t enableStereoMVC : 1;
    uint32_t enableHierarchicalP : 1;
    uint32_t enableHierarchicalB : 1;
    uint32_t enableIntraRefresh : 1;
    uint32_t enableConstrainedEncoding : 1;
    uint32_t enableTemporalSVC : 1;
    uint32_t enableTimeCode : 1;
    uint32_t enableMinQP : 1;
    uint32_t enableMaxQP : 1;
    uint32_t enableInitialRCQP : 1;
    uint32_t reservedBitFields : 22;
    uint32_t idrPeriod;
    uint32_t intraRefreshPeriod;
    uint32_t intraRefreshCnt;
    uint32_t maxNumRefFrames;
    uint32_t sliceMode;
    uint32_t sliceModeData;
    uint32_t reserved1[250];
    void* reserved2[64];
} NV_ENC_CONFIG_H264;

typedef struct _NV_ENC_CONFIG_HEVC {
    uint32_t level;
    uint32_t tier;
    uint32_t minCUSize;
    uint32_t maxCUSize;
    uint32_t useConstrainedIntraPred : 1;
    uint32_t disableDeblockAcrossSliceBoundary : 1;
    uint32_t outputPartitionRowSEI : 1;
    uint32_t outputSubframeSEI : 1;
    uint32_t enableIntraRefresh : 1;
    uint32_t enableConstrainedEncoding : 1;
    uint32_t enableTemporalSVC : 1;
    uint32_t enableTimeCodeSEI : 1;
    uint32_t enableMinQP : 1;
    uint32_t enableMaxQP : 1;
    uint32_t enableInitialRCQP : 1;
    uint32_t reservedBitFields : 21;
    uint32_t idrPeriod;
    uint32_t intraRefreshPeriod;
    uint32_t intraRefreshCnt;
    uint32_t maxNumRefFramesInDPB;
    uint32_t ltrNumFrames;
    uint32_t pixelBitDepthMinus8;
    uint32_t reserved1[246];
    void* reserved2[64];
} NV_ENC_CONFIG_HEVC;

typedef struct _NV_ENC_CONFIG_AV1 {
    uint32_t level;
    uint32_t tier;
    uint32_t minPartSize;
    uint32_t maxPartSize;
    uint32_t outputAnnexB : 1;
    uint32_t enableIntraRefresh : 1;
    uint32_t enableCustomTileConfig : 1;
    uint32_t enableFilmGrainParams : 1;
    uint32_t enableMinQP : 1;
    uint32_t enableMaxQP : 1;
    uint32_t enableInitialRCQP : 1;
    uint32_t enableOrderHint : 1;
    uint32_t reservedBitFields : 24;
    uint32_t idrPeriod;
    uint32_t intraRefreshPeriod;
    uint32_t intraRefreshCnt;
    uint32_t maxNumRefFramesInDPB;
    uint32_t inputBitDepth;
    uint32_t outputBitDepth;
    uint32_t reserved1[246];
    void* reserved2[64];
} NV_ENC_CONFIG_AV1;

typedef union _NV_ENC_CODEC_CONFIG {
    NV_ENC_CONFIG_H264 h264Config;
    NV_ENC_CONFIG_HEVC hevcConfig;
    NV_ENC_CONFIG_AV1 av1Config;
    uint32_t reserved[320];
} NV_ENC_CODEC_CONFIG;

typedef struct _NV_ENC_CONFIG {
    uint32_t version;
    GUID profileGUID;
    uint32_t gopLength;
    int32_t frameIntervalP;
    uint32_t monoChromeEncoding;
    uint32_t frameFieldMode;
    uint32_t mvPrecision;
    NV_ENC_RC_PARAMS rcParams;
    NV_ENC_CODEC_CONFIG encodeCodecConfig;
    uint32_t reserved[278];
    void* reserved2[64];
} NV_ENC_CONFIG;

#define NV_ENC_CONFIG_VER (NVENCAPI_STRUCT_VERSION(9) | (1u << 31))

typedef struct _NV_ENC_PRESET_CONFIG {
    uint32_t version;
    uint32_t reserved;
    NV_ENC_CONFIG presetCfg;
    uint32_t reserved1[256];
    void* reserved2[64];
} NV_ENC_PRESET_CONFIG;

#define NV_ENC_PRESET_CONFIG_VER (NVENCAPI_STRUCT_VERSION(5) | (1u << 31))

typedef struct _NV_ENC_INITIALIZE_PARAMS {
    uint32_t version;
    GUID encodeGUID;
    GUID presetGUID;
    uint32_t encodeWidth;
    uint32_t encodeHeight;
    uint32_t darWidth;
    uint32_t darHeight;
    uint32_t frameRateNum;
    uint32_t frameRateDen;
    uint32_t enableEncodeAsync;
    uint32_t enablePTD;
    uint32_t reportSliceOffsets : 1;
    uint32_t enableSubFrameWrite : 1;
    uint32_t enableExternalMEHints : 1;
    uint32_t enableMEOnlyMode : 1;
    uint32_t enableWeightedPrediction : 1;
    uint32_t splitEncodeMode : 4;
    uint32_t enableOutputInVidmem : 1;
    uint32_t enableReconFrameOutput : 1;
    uint32_t enableOutputStats : 1;
    uint32_t enableUniDirectionalB : 1;
    uint32_t reservedBitFields : 19;
    uint32_t privDataSize;
    uint32_t reserved;
    void* privData;
    NV_ENC_CONFIG* encodeConfig;
    uint32_t maxEncodeWidth;
    uint32_t maxEncodeHeight;
    void* maxMEHintCountsPerBlock[2];
    uint32_t tuningInfo;
    uint32_t bufferFormat;
    uint32_t numStateBuffers;
    uint32_t outputStatsLevel;
    uint32_t reserved1[284];
    void* reserved2[64];
} NV_ENC_INITIALIZE_PARAMS;

#define NV_ENC_INITIALIZE_PARAMS_VER (NVENCAPI_STRUCT_VERSION(7) | (1u << 31))

typedef struct _NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS {
    uint32_t version;
    uint32_t deviceType;
    void* device;
    void* reserved;
    uint32_t apiVersion;
    uint32_t reserved1[253];
    void* reserved2[64];
} NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS;

#define NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER NVENCAPI_STRUCT_VERSION(1)

typedef struct _NV_ENC_REGISTER_RESOURCE {
    uint32_t version;
    uint32_t resourceType;
    uint32_t width;
    uint32_t height;
    uint32_t pitch;
    uint32_t subResourceIndex;
    void* resourceToRegister;
    void* registeredResource;
    uint32_t bufferFormat;
    uint32_t bufferUsage;
    void* pInputFencePoint;
    uint32_t chromaOffset[2];
    uint32_t chromaOffsetIn[2];
    uint32_t reserved1[244];
    void* reserved2[61];
} NV_ENC_REGISTER_RESOURCE;

#define NV_ENC_REGISTER_RESOURCE_VER NVENCAPI_STRUCT_VERSION(5)

typedef struct _NV_ENC_MAP_INPUT_RESOURCE {
    uint32_t version;
    uint32_t subResourceIndex;
    void* inputResource;
    void* registeredResource;
    void* mappedResource;
    uint32_t mappedBufferFmt;
    uint32_t reserved1[251];
    void* reserved2[63];
} NV_ENC_MAP_INPUT_RESOURCE;

#define NV_ENC_MAP_INPUT_RESOURCE_VER NVENCAPI_STRUCT_VERSION(4)

typedef struct _NV_ENC_CREATE_BITSTREAM_BUFFER {
    uint32_t version;
    uint32_t size;
    uint32_t memoryHeap;
    uint32_t reserved;
    void* bitstreamBuffer;
    void* bitstreamBufferPtr;
    uint32_t reserved1[58];
    void* reserved2[64];
} NV_ENC_CREATE_BITSTREAM_BUFFER;

#define NV_ENC_CREATE_BITSTREAM_BUFFER_VER NVENCAPI_STRUCT_VERSION(1)

typedef struct _NV_ENC_CODEC_PIC_PARAMS {
    uint32_t reserved[256];
} NV_ENC_CODEC_PIC_PARAMS;

typedef struct _NV_ENC_PIC_PARAMS {
    uint32_t version;
    uint32_t inputWidth;
    uint32_t inputHeight;
    uint32_t inputPitch;
    uint32_t encodePicFlags;
    uint32_t frameIdx;
    uint64_t inputTimeStamp;
    uint64_t inputDuration;
    void* inputBuffer;
    void* outputBitstream;
    void* completionEvent;
    uint32_t bufferFmt;
    uint32_t pictureStruct;
    uint32_t pictureType;
    NV_ENC_CODEC_PIC_PARAMS codecPicParams;
    void* meHintCountsPerBlock[2];
    void* meExternalHints;
    uint32_t reserved2[7];
    void* reserved5[2];
    int8_t* qpDeltaMap;
    uint32_t qpDeltaMapSize;
    uint32_t reservedBitFields;
    uint16_t meHintRefPicDist[2];
    int32_t diffPicNumHint;
    void* alphaBuffer;
    void* meExternalSbHints;
    uint32_t meSbHintsCount;
    uint32_t stateBufferIdx;
    void* outputReconBuffer;
    uint32_t reserved3[284];
    void* reserved6[57];
} NV_ENC_PIC_PARAMS;

#define NV_ENC_PIC_PARAMS_VER (NVENCAPI_STRUCT_VERSION(7) | (1u << 31))

typedef struct _NV_ENC_LOCK_BITSTREAM {
    uint32_t version;
    uint32_t doNotWait : 1;
    uint32_t ltrFrame : 1;
    uint32_t getRCStats : 1;
    uint32_t reservedBitFields : 29;
    void* outputBitstream;
    uint32_t* sliceOffsets;
    uint32_t frameIdx;
    uint32_t hwEncodeStatus;
    uint32_t numSlices;
    uint32_t bitstreamSizeInBytes;
    uint64_t outputTimeStamp;
    uint64_t outputDuration;
    void* bitstreamBufferPtr;
    uint32_t pictureType;
    uint32_t pictureStruct;
    uint32_t frameAvgQP;
    uint32_t frameSatd;
    uint32_t ltrFrameIdx;
    uint32_t ltrFrameBitmap;
    uint32_t temporalId;
    uint32_t intraMBCount;
    uint32_t interMBCount;
    int32_t averageMVX;
    int32_t averageMVY;
    uint32_t alphaLayerSizeInBytes;
    uint32_t outputStatsPtrSize;
    uint32_t reserved;
    void* outputStatsPtr;
    uint32_t frameIdxDisplay;
    uint32_t reserved1[219];
    void* reserved2[63];
    uint32_t reservedInternal[8];
} NV_ENC_LOCK_BITSTREAM;

#define NV_ENC_LOCK_BITSTREAM_VER (NVENCAPI_STRUCT_VERSION(2) | (1u << 31))

typedef struct _NV_ENC_RECONFIGURE_PARAMS {
    uint32_t version;
    NV_ENC_INITIALIZE_PARAMS reInitEncodeParams;
    uint32_t resetEncoder : 1;
    uint32_t forceIDR : 1;
    uint32_t reservedBitFields : 30;
} NV_ENC_RECONFIGURE_PARAMS;

#define NV_ENC_RECONFIGURE_PARAMS_VER (NVENCAPI_STRUCT_VERSION(2) | (1u << 31))

#define NV_ENCODE_API_FUNCTION_LIST_VER NVENCAPI_STRUCT_VERSION(2)

typedef NVENCSTATUS(NVENCAPI* NvEncodeAPICreateInstance_Fn)(moonshine::encoder::NVENC_FN_LIST* functionList);
typedef NVENCSTATUS(NVENCAPI* NvEncodeAPIGetMaxSupportedVersion_Fn)(uint32_t* version);

typedef NVENCSTATUS(NVENCAPI* PNVENCOPENENCODESESSIONEX)(NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS*, void**);
typedef NVENCSTATUS(NVENCAPI* PNVENCINITIALIZEENCODER)(void*, NV_ENC_INITIALIZE_PARAMS*);
typedef NVENCSTATUS(NVENCAPI* PNVENCCREATEBITSTREAMBUFFER)(void*, NV_ENC_CREATE_BITSTREAM_BUFFER*);
typedef NVENCSTATUS(NVENCAPI* PNVENCDESTROYBITSTREAMBUFFER)(void*, void*);
typedef NVENCSTATUS(NVENCAPI* PNVENCREGISTERRESOURCE)(void*, NV_ENC_REGISTER_RESOURCE*);
typedef NVENCSTATUS(NVENCAPI* PNVENCUNREGISTERRESOURCE)(void*, void*);
typedef NVENCSTATUS(NVENCAPI* PNVENCMAPINPUTRESOURCE)(void*, NV_ENC_MAP_INPUT_RESOURCE*);
typedef NVENCSTATUS(NVENCAPI* PNVENCUNMAPINPUTRESOURCE)(void*, void*);
typedef NVENCSTATUS(NVENCAPI* PNVENCENCODEPICTURE)(void*, NV_ENC_PIC_PARAMS*);
typedef NVENCSTATUS(NVENCAPI* PNVENCLOCKBITSTREAM)(void*, NV_ENC_LOCK_BITSTREAM*);
typedef NVENCSTATUS(NVENCAPI* PNVENCUNLOCKBITSTREAM)(void*, void*);
typedef NVENCSTATUS(NVENCAPI* PNVENCDESTROYENCODER)(void*);
typedef NVENCSTATUS(NVENCAPI* PNVENCRECONFIGUREENCODER)(void*, NV_ENC_RECONFIGURE_PARAMS*);
typedef NVENCSTATUS(NVENCAPI* PNVENCGETENCODEPRESETCONFIGEX)(void*, GUID, GUID, uint32_t, NV_ENC_PRESET_CONFIG*);
#endif

namespace moonshine::encoder {

NvencVideoEncoder::NvencVideoEncoder() = default;

NvencVideoEncoder::~NvencVideoEncoder() {
    cleanup();
}

bool NvencVideoEncoder::initialize(void* d3d_device, const EncoderConfig& config) {
    cleanup();

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    // Verify adapter is genuine NVIDIA hardware
    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) {
        return false;
    }

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) {
        return false;
    }

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) { // NVIDIA Vendor ID
        return false;
    }

    // Attempt to dynamically load nvEncodeAPI64.dll from system drivers
    _nvenc_module = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!_nvenc_module) {
        _nvenc_module = LoadLibraryW(L"nvEncodeAPI64.dll");
    }
    if (!_nvenc_module) {
        return false;
    }

    auto createInstance = reinterpret_cast<NvEncodeAPICreateInstance_Fn>(
        GetProcAddress(_nvenc_module, "NvEncodeAPICreateInstance")
    );
    if (!createInstance) {
        cleanup();
        return false;
    }

    _nvenc_funcs.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    if (createInstance(&_nvenc_funcs) != NV_ENC_SUCCESS) {
        cleanup();
        return false;
    }

    // Open encode session on D3D11 device
    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS sessionParams{};
    sessionParams.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    sessionParams.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
    sessionParams.device = d3d_device;
    sessionParams.apiVersion = NVENCAPI_VERSION;

    auto pfnOpenSessionEx = reinterpret_cast<PNVENCOPENENCODESESSIONEX>(_nvenc_funcs.nvEncOpenEncodeSessionEx);
    if (!pfnOpenSessionEx || pfnOpenSessionEx(&sessionParams, &_encoder_session) != NV_ENC_SUCCESS || !_encoder_session) {
        cleanup();
        return false;
    }

    // Codec GUID selection
    auto selected_codec = static_cast<VideoCodec>(config.codec);
    GUID codecGuid = NV_ENC_CODEC_H264_GUID_LOCAL;
    if (selected_codec == VideoCodec::Hevc || selected_codec == VideoCodec::HevcMain10) {
        codecGuid = NV_ENC_CODEC_HEVC_GUID_LOCAL;
    } else if (selected_codec == VideoCodec::Av1) {
        codecGuid = NV_ENC_CODEC_AV1_GUID_LOCAL;
    }

    // Preset GUID selection
    GUID presetGuid = NV_ENC_PRESET_P1_GUID_LOCAL;
    switch (_preset) {
        case NvencPreset::P1_UltraFast: presetGuid = NV_ENC_PRESET_P1_GUID_LOCAL; break;
        case NvencPreset::P2_Fast: presetGuid = NV_ENC_PRESET_P2_GUID_LOCAL; break;
        case NvencPreset::P3_Medium: presetGuid = NV_ENC_PRESET_P3_GUID_LOCAL; break;
        case NvencPreset::P4_Default: presetGuid = NV_ENC_PRESET_P4_GUID_LOCAL; break;
        case NvencPreset::P5_Slow: presetGuid = NV_ENC_PRESET_P5_GUID_LOCAL; break;
        case NvencPreset::P6_Slower: presetGuid = NV_ENC_PRESET_P6_GUID_LOCAL; break;
        case NvencPreset::P7_Slowest: presetGuid = NV_ENC_PRESET_P7_GUID_LOCAL; break;
    }

    // Tuning selection
    uint32_t tuningInfo = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
    switch (_tuning) {
        case NvencTuning::HighQuality: tuningInfo = NV_ENC_TUNING_INFO_HIGH_QUALITY; break;
        case NvencTuning::LowLatency: tuningInfo = NV_ENC_TUNING_INFO_LOW_LATENCY; break;
        case NvencTuning::UltraLowLatency: tuningInfo = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY; break;
        case NvencTuning::Lossless: tuningInfo = NV_ENC_TUNING_INFO_LOSSLESS; break;
    }

    // Query preset configuration
    NV_ENC_PRESET_CONFIG presetConfig{};
    presetConfig.version = NV_ENC_PRESET_CONFIG_VER;
    presetConfig.presetCfg.version = NV_ENC_CONFIG_VER;

    auto pfnGetPresetConfigEx = reinterpret_cast<PNVENCGETENCODEPRESETCONFIGEX>(_nvenc_funcs.nvEncGetEncodePresetConfigEx);
    NV_ENC_CONFIG encConfig{};
    if (pfnGetPresetConfigEx && pfnGetPresetConfigEx(_encoder_session, codecGuid, presetGuid, tuningInfo, &presetConfig) == NV_ENC_SUCCESS) {
        encConfig = presetConfig.presetCfg;
    } else {
        encConfig.version = NV_ENC_CONFIG_VER;
        encConfig.profileGUID = NV_ENC_CODEC_PROFILE_AUTOSELECT_GUID_LOCAL;
    }

    // Profile specification
    if (selected_codec == VideoCodec::HevcMain10) {
        encConfig.profileGUID = NV_ENC_HEVC_PROFILE_MAIN10_GUID_LOCAL;
        encConfig.encodeCodecConfig.hevcConfig.pixelBitDepthMinus8 = 2;
    } else if (selected_codec == VideoCodec::Hevc) {
        encConfig.profileGUID = NV_ENC_HEVC_PROFILE_MAIN_GUID_LOCAL;
        encConfig.encodeCodecConfig.hevcConfig.pixelBitDepthMinus8 = 0;
    } else if (selected_codec == VideoCodec::H264) {
        encConfig.profileGUID = NV_ENC_H264_PROFILE_HIGH_GUID_LOCAL;
    } else if (selected_codec == VideoCodec::Av1) {
        encConfig.profileGUID = NV_ENC_AV1_PROFILE_MAIN_GUID_LOCAL;
    }

    encConfig.gopLength = config.gop_length == 0 ? NVENC_INFINITE_GOPLENGTH : config.gop_length;
    encConfig.frameIntervalP = 1;
    encConfig.frameFieldMode = NV_ENC_PARAMS_FRAME_FIELD_MODE_FRAME;
    encConfig.mvPrecision = 0;

    // Rate control configuration
    encConfig.rcParams.rateControlMode = (config.rc_mode == 0) ? NV_ENC_PARAMS_RC_CBR : NV_ENC_PARAMS_RC_VBR;
    encConfig.rcParams.averageBitRate = config.bitrate_kbps * 1000;
    encConfig.rcParams.maxBitRate = (config.peak_bitrate_kbps > 0 ? config.peak_bitrate_kbps : config.bitrate_kbps * 3 / 2) * 1000;
    encConfig.rcParams.vbvBufferSize = (config.fps > 0) ? (encConfig.rcParams.averageBitRate / config.fps) : encConfig.rcParams.averageBitRate;
    encConfig.rcParams.vbvInitialDelay = encConfig.rcParams.vbvBufferSize;
    encConfig.rcParams.zeroReorderDelay = 1;

    // Intra-refresh configuration if requested
    if (_intra_refresh_enabled || config.enable_intra_refresh) {
        uint32_t period = _intra_refresh_period > 0 ? _intra_refresh_period : 60;
        uint32_t count = _intra_refresh_count > 0 ? _intra_refresh_count : 4;
        if (selected_codec == VideoCodec::H264) {
            encConfig.encodeCodecConfig.h264Config.enableIntraRefresh = 1;
            encConfig.encodeCodecConfig.h264Config.intraRefreshPeriod = period;
            encConfig.encodeCodecConfig.h264Config.intraRefreshCnt = count;
        } else if (selected_codec == VideoCodec::Hevc || selected_codec == VideoCodec::HevcMain10) {
            encConfig.encodeCodecConfig.hevcConfig.enableIntraRefresh = 1;
            encConfig.encodeCodecConfig.hevcConfig.intraRefreshPeriod = period;
            encConfig.encodeCodecConfig.hevcConfig.intraRefreshCnt = count;
        } else if (selected_codec == VideoCodec::Av1) {
            encConfig.encodeCodecConfig.av1Config.enableIntraRefresh = 1;
            encConfig.encodeCodecConfig.av1Config.intraRefreshPeriod = period;
            encConfig.encodeCodecConfig.av1Config.intraRefreshCnt = count;
        }
    }

    // Encoder initialisation parameters
    NV_ENC_INITIALIZE_PARAMS initParams{};
    initParams.version = NV_ENC_INITIALIZE_PARAMS_VER;
    initParams.encodeGUID = codecGuid;
    initParams.presetGUID = presetGuid;
    initParams.encodeWidth = config.width;
    initParams.encodeHeight = config.height;
    initParams.darWidth = config.width;
    initParams.darHeight = config.height;
    initParams.frameRateNum = config.fps;
    initParams.frameRateDen = 1;
    initParams.enablePTD = 1;
    initParams.enableEncodeAsync = 0;
    initParams.encodeConfig = &encConfig;
    initParams.maxEncodeWidth = config.width;
    initParams.maxEncodeHeight = config.height;
    initParams.tuningInfo = tuningInfo;

    auto pfnInitEncoder = reinterpret_cast<PNVENCINITIALIZEENCODER>(_nvenc_funcs.nvEncInitializeEncoder);
    if (!pfnInitEncoder || pfnInitEncoder(_encoder_session, &initParams) != NV_ENC_SUCCESS) {
        cleanup();
        return false;
    }

    // Allocate output bitstream buffer
    NV_ENC_CREATE_BITSTREAM_BUFFER createBitstream{};
    createBitstream.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    auto pfnCreateBitstream = reinterpret_cast<PNVENCCREATEBITSTREAMBUFFER>(_nvenc_funcs.nvEncCreateBitstreamBuffer);
    if (!pfnCreateBitstream || pfnCreateBitstream(_encoder_session, &createBitstream) != NV_ENC_SUCCESS || !createBitstream.bitstreamBuffer) {
        cleanup();
        return false;
    }
    _bitstream_buffer = createBitstream.bitstreamBuffer;

    _d3d_device = d3d_device;
    _config = config;
    _frame_counter = 0;
    _force_keyframe = true;
    _initialized = true;
    return true;
#else
    (void)d3d_device;
    (void)config;
    return false;
#endif
}

bool NvencVideoEncoder::encode_frame(
    void* d3d_texture,
    bool force_idr,
    EncodedPacketDesc& out_desc,
    uint8_t* out_bitstream,
    uint32_t max_buffer_size,
    uint32_t& out_written_size
) {
#if defined(_WIN32)
    if (!_initialized || !_encoder_session || !d3d_texture || !out_bitstream || max_buffer_size == 0) {
        out_written_size = 0;
        return false;
    }

    // Register texture resource if new or changed
    if (_registered_texture != d3d_texture || !_registered_resource) {
        if (_registered_resource) {
            auto pfnUnreg = reinterpret_cast<PNVENCUNREGISTERRESOURCE>(_nvenc_funcs.nvEncUnregisterResource);
            if (pfnUnreg) {
                pfnUnreg(_encoder_session, _registered_resource);
            }
            _registered_resource = nullptr;
            _registered_texture = nullptr;
        }

        NV_ENC_REGISTER_RESOURCE regParams{};
        regParams.version = NV_ENC_REGISTER_RESOURCE_VER;
        regParams.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
        regParams.width = _config.width;
        regParams.height = _config.height;
        regParams.pitch = 0;
        regParams.resourceToRegister = d3d_texture;
        regParams.bufferUsage = NV_ENC_INPUT_IMAGE;

        auto* pTex = static_cast<ID3D11Texture2D*>(d3d_texture);
        D3D11_TEXTURE2D_DESC texDesc{};
        pTex->GetDesc(&texDesc);

        if (texDesc.Format == DXGI_FORMAT_R10G10B10A2_UNORM) {
            regParams.bufferFormat = NV_ENC_BUFFER_FORMAT_ABGR10;
        } else if (texDesc.Format == DXGI_FORMAT_NV12) {
            regParams.bufferFormat = NV_ENC_BUFFER_FORMAT_NV12;
        } else if (texDesc.Format == DXGI_FORMAT_P010) {
            regParams.bufferFormat = NV_ENC_BUFFER_FORMAT_P010;
        } else if (texDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM) {
            regParams.bufferFormat = NV_ENC_BUFFER_FORMAT_ARGB;
        } else {
            regParams.bufferFormat = (static_cast<VideoCodec>(_config.codec) == VideoCodec::HevcMain10)
                ? NV_ENC_BUFFER_FORMAT_ABGR10
                : NV_ENC_BUFFER_FORMAT_ABGR;
        }

        auto pfnRegister = reinterpret_cast<PNVENCREGISTERRESOURCE>(_nvenc_funcs.nvEncRegisterResource);
        if (!pfnRegister || pfnRegister(_encoder_session, &regParams) != NV_ENC_SUCCESS || !regParams.registeredResource) {
            out_written_size = 0;
            return false;
        }
        _registered_resource = regParams.registeredResource;
        _registered_texture = d3d_texture;
    }

    // Map input resource for encoder hardware access
    NV_ENC_MAP_INPUT_RESOURCE mapParams{};
    mapParams.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
    mapParams.registeredResource = _registered_resource;
    auto pfnMap = reinterpret_cast<PNVENCMAPINPUTRESOURCE>(_nvenc_funcs.nvEncMapInputResource);
    if (!pfnMap || pfnMap(_encoder_session, &mapParams) != NV_ENC_SUCCESS || !mapParams.mappedResource) {
        out_written_size = 0;
        return false;
    }

    bool is_key = force_idr || _force_keyframe.exchange(false) || (_frame_counter == 0);

    NV_ENC_PIC_PARAMS picParams{};
    picParams.version = NV_ENC_PIC_PARAMS_VER;
    picParams.inputWidth = _config.width;
    picParams.inputHeight = _config.height;
    picParams.inputPitch = _config.width;
    picParams.inputBuffer = mapParams.mappedResource;
    picParams.outputBitstream = _bitstream_buffer;
    picParams.bufferFmt = mapParams.mappedBufferFmt;
    picParams.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
    picParams.frameIdx = static_cast<uint32_t>(_frame_counter);
    picParams.encodePicFlags = 0;
    if (is_key) {
        picParams.encodePicFlags |= NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS;
    }

    auto pfnEncode = reinterpret_cast<PNVENCENCODEPICTURE>(_nvenc_funcs.nvEncEncodePicture);
    NVENCSTATUS encStatus = pfnEncode ? pfnEncode(_encoder_session, &picParams) : -1;
    if (encStatus != NV_ENC_SUCCESS) {
        auto pfnUnmap = reinterpret_cast<PNVENCUNMAPINPUTRESOURCE>(_nvenc_funcs.nvEncUnmapInputResource);
        if (pfnUnmap) {
            pfnUnmap(_encoder_session, mapParams.mappedResource);
        }
        out_written_size = 0;
        return false;
    }

    // Lock bitstream buffer to retrieve encoded payload
    NV_ENC_LOCK_BITSTREAM lockParams{};
    lockParams.version = NV_ENC_LOCK_BITSTREAM_VER;
    lockParams.doNotWait = 0;
    lockParams.outputBitstream = _bitstream_buffer;

    auto pfnLock = reinterpret_cast<PNVENCLOCKBITSTREAM>(_nvenc_funcs.nvEncLockBitstream);
    if (!pfnLock || pfnLock(_encoder_session, &lockParams) != NV_ENC_SUCCESS || !lockParams.bitstreamBufferPtr) {
        auto pfnUnmap = reinterpret_cast<PNVENCUNMAPINPUTRESOURCE>(_nvenc_funcs.nvEncUnmapInputResource);
        if (pfnUnmap) {
            pfnUnmap(_encoder_session, mapParams.mappedResource);
        }
        out_written_size = 0;
        return false;
    }

    uint32_t copySize = (lockParams.bitstreamSizeInBytes <= max_buffer_size) ? lockParams.bitstreamSizeInBytes : max_buffer_size;
    std::memcpy(out_bitstream, lockParams.bitstreamBufferPtr, copySize);

    out_written_size = copySize;
    out_desc.frame_index = _frame_counter++;
    auto now = std::chrono::high_resolution_clock::now().time_since_epoch();
    out_desc.timestamp_qpc = std::chrono::duration_cast<std::chrono::microseconds>(now).count();
    out_desc.payload_size = copySize;
    out_desc.is_keyframe = (lockParams.pictureType == NV_ENC_PIC_TYPE_IDR || lockParams.pictureType == NV_ENC_PIC_TYPE_I || is_key) ? 1 : 0;
    out_desc.is_header_packet = out_desc.is_keyframe;
    out_desc.temporal_id = 0;
    out_desc.reserved = 0;

    auto pfnUnlock = reinterpret_cast<PNVENCUNLOCKBITSTREAM>(_nvenc_funcs.nvEncUnlockBitstream);
    if (pfnUnlock) {
        pfnUnlock(_encoder_session, _bitstream_buffer);
    }

    auto pfnUnmap = reinterpret_cast<PNVENCUNMAPINPUTRESOURCE>(_nvenc_funcs.nvEncUnmapInputResource);
    if (pfnUnmap) {
        pfnUnmap(_encoder_session, mapParams.mappedResource);
    }

    return true;
#else
    (void)d3d_texture;
    (void)force_idr;
    (void)out_desc;
    (void)out_bitstream;
    (void)max_buffer_size;
    out_written_size = 0;
    return false;
#endif
}

bool NvencVideoEncoder::reconfigure(const EncoderConfig& new_config) {
    if (!_initialized) return false;
    _config = new_config;
    _force_keyframe = true;

#if defined(_WIN32)
    if (_encoder_session && _nvenc_funcs.nvEncReconfigureEncoder) {
        NV_ENC_RECONFIGURE_PARAMS reconfigParams{};
        reconfigParams.version = NV_ENC_RECONFIGURE_PARAMS_VER;
        reconfigParams.resetEncoder = 0;
        reconfigParams.forceIDR = 1;

        reconfigParams.reInitEncodeParams.version = NV_ENC_INITIALIZE_PARAMS_VER;
        reconfigParams.reInitEncodeParams.encodeWidth = new_config.width;
        reconfigParams.reInitEncodeParams.encodeHeight = new_config.height;
        reconfigParams.reInitEncodeParams.frameRateNum = new_config.fps;
        reconfigParams.reInitEncodeParams.frameRateDen = 1;

        NV_ENC_CONFIG encConfig{};
        encConfig.version = NV_ENC_CONFIG_VER;
        encConfig.rcParams.rateControlMode = (new_config.rc_mode == 0) ? NV_ENC_PARAMS_RC_CBR : NV_ENC_PARAMS_RC_VBR;
        encConfig.rcParams.averageBitRate = new_config.bitrate_kbps * 1000;
        encConfig.rcParams.maxBitRate = (new_config.peak_bitrate_kbps > 0 ? new_config.peak_bitrate_kbps : new_config.bitrate_kbps * 3 / 2) * 1000;
        encConfig.rcParams.vbvBufferSize = (new_config.fps > 0) ? (encConfig.rcParams.averageBitRate / new_config.fps) : encConfig.rcParams.averageBitRate;
        encConfig.rcParams.vbvInitialDelay = encConfig.rcParams.vbvBufferSize;
        reconfigParams.reInitEncodeParams.encodeConfig = &encConfig;

        auto pfnReconfig = reinterpret_cast<PNVENCRECONFIGUREENCODER>(_nvenc_funcs.nvEncReconfigureEncoder);
        if (pfnReconfig) {
            pfnReconfig(_encoder_session, &reconfigParams);
        }
    }
#endif
    return true;
}

void NvencVideoEncoder::request_keyframe() {
    _force_keyframe = true;
}

void NvencVideoEncoder::cleanup() {
    _initialized = false;

#if defined(_WIN32)
    if (_encoder_session) {
        if (_bitstream_buffer) {
            auto pfnDestroyBitstream = reinterpret_cast<PNVENCDESTROYBITSTREAMBUFFER>(_nvenc_funcs.nvEncDestroyBitstreamBuffer);
            if (pfnDestroyBitstream) {
                pfnDestroyBitstream(_encoder_session, _bitstream_buffer);
            }
            _bitstream_buffer = nullptr;
        }

        if (_registered_resource) {
            auto pfnUnreg = reinterpret_cast<PNVENCUNREGISTERRESOURCE>(_nvenc_funcs.nvEncUnregisterResource);
            if (pfnUnreg) {
                pfnUnreg(_encoder_session, _registered_resource);
            }
            _registered_resource = nullptr;
            _registered_texture = nullptr;
        }

        auto pfnDestroyEncoder = reinterpret_cast<PNVENCDESTROYENCODER>(_nvenc_funcs.nvEncDestroyEncoder);
        if (pfnDestroyEncoder) {
            pfnDestroyEncoder(_encoder_session);
        }
        _encoder_session = nullptr;
    }

    if (_nvenc_module) {
        FreeLibrary(_nvenc_module);
        _nvenc_module = nullptr;
    }
    std::memset(&_nvenc_funcs, 0, sizeof(_nvenc_funcs));
#endif

    _d3d_device = nullptr;
    _frame_counter = 0;
    _force_keyframe = false;
}

bool NvencVideoEncoder::set_preset_and_tuning(NvencPreset preset, NvencTuning tuning) {
    _preset = preset;
    _tuning = tuning;
    return true;
}

bool NvencVideoEncoder::set_intra_refresh(bool enabled, uint32_t period, uint32_t count) {
    _intra_refresh_enabled = enabled;
    _intra_refresh_period = period;
    _intra_refresh_count = count;
    return true;
}

bool NvencVideoEncoder::query_capabilities(void* d3d_device, EncoderCaps& out_caps) {
    std::memset(&out_caps, 0, sizeof(EncoderCaps));
    out_caps.vendor_id = static_cast<uint8_t>(EncoderVendor::NvidiaNvenc);

#if defined(_WIN32)
    if (!d3d_device) {
        return false;
    }

    auto* dev = static_cast<ID3D11Device*>(d3d_device);
    ComPtr<IDXGIDevice> dxgi_dev;
    if (FAILED(dev->QueryInterface(__uuidof(IDXGIDevice), &dxgi_dev))) return false;

    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(dxgi_dev->GetAdapter(&adapter))) return false;

    DXGI_ADAPTER_DESC desc{};
    if (FAILED(adapter->GetDesc(&desc)) || desc.VendorId != 0x10DE) return false;

    HMODULE hNvenc = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!hNvenc) {
        hNvenc = LoadLibraryW(L"nvEncodeAPI64.dll");
    }
    if (!hNvenc) return false;

    auto createInstance = reinterpret_cast<NvEncodeAPICreateInstance_Fn>(
        GetProcAddress(hNvenc, "NvEncodeAPICreateInstance")
    );
    if (!createInstance) {
        FreeLibrary(hNvenc);
        return false;
    }

    NVENC_FN_LIST fn_list{};
    fn_list.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    if (createInstance(&fn_list) != NV_ENC_SUCCESS) {
        FreeLibrary(hNvenc);
        return false;
    }

    out_caps.supported_codecs_mask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3); // H264, HEVC, HEVC Main10, AV1
    out_caps.max_width = 8192;
    out_caps.max_height = 8192;
    out_caps.max_fps = 240;
    out_caps.supports_10bit = 1;
    out_caps.supports_lossless = 1;
    out_caps.supports_smart_idr = 1;
    out_caps.min_bitrate_kbps = 500;
    out_caps.max_bitrate_kbps = 200000;

    FreeLibrary(hNvenc);
    return true;
#else
    (void)d3d_device;
    return false;
#endif
}

bool NvencVideoEncoder::query_codec_support(VideoCodec codec) {
#if defined(_WIN32)
    static const bool s_supported = []() {
        HMODULE hNvenc = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!hNvenc) {
            hNvenc = LoadLibraryW(L"nvEncodeAPI64.dll");
        }
        if (!hNvenc) return false;
        FreeLibrary(hNvenc);
        return true;
    }();
    if (!s_supported) return false;
    return codec == VideoCodec::H264 || codec == VideoCodec::Hevc ||
           codec == VideoCodec::HevcMain10 || codec == VideoCodec::Av1;
#else
    (void)codec;
    return false;
#endif
}

} // namespace moonshine::encoder
