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
#include <guiddef.h>
#define NVENCAPI __stdcall
#else
#define NVENCAPI
struct GUID {
    uint32_t Data1;
    uint16_t Data2;
    uint16_t Data3;
    uint8_t  Data4[8];
};
#endif

namespace moonshine::encoder {

#if !defined(_NVENC_FN_LIST_DEFINED)
#define _NVENC_FN_LIST_DEFINED
typedef struct _NVENC_FN_LIST {
    uint32_t version;
    uint32_t reserved;
    void* nvEncOpenEncodeSession;
    void* nvEncGetEncodeGUIDCount;
    void* nvEncGetEncodeProfileGUIDCount;
    void* nvEncGetEncodeProfileGUIDs;
    void* nvEncGetEncodeGUIDs;
    void* nvEncGetInputFormatCount;
    void* nvEncGetInputFormats;
    void* nvEncGetEncodeCaps;
    void* nvEncGetEncodePresetCount;
    void* nvEncGetEncodePresetGUIDs;
    void* nvEncGetEncodePresetConfig;
    void* nvEncInitializeEncoder;
    void* nvEncCreateInputBuffer;
    void* nvEncDestroyInputBuffer;
    void* nvEncCreateBitstreamBuffer;
    void* nvEncDestroyBitstreamBuffer;
    void* nvEncEncodePicture;
    void* nvEncLockBitstream;
    void* nvEncUnlockBitstream;
    void* nvEncLockInputBuffer;
    void* nvEncUnlockInputBuffer;
    void* nvEncGetEncodeStats;
    void* nvEncGetSequenceParams;
    void* nvEncRegisterAsyncEvent;
    void* nvEncUnregisterAsyncEvent;
    void* nvEncMapInputResource;
    void* nvEncUnmapInputResource;
    void* nvEncDestroyEncoder;
    void* nvEncInvalidateRefFrames;
    void* nvEncOpenEncodeSessionEx;
    void* nvEncRegisterResource;
    void* nvEncUnregisterResource;
    void* nvEncReconfigureEncoder;
    void* reserved1;
    void* nvEncCreateMVBuffer;
    void* nvEncDestroyMVBuffer;
    void* nvEncRunMotionEstimationOnly;
    void* nvEncGetLastErrorString;
    void* nvEncSetIOCudaStreams;
    void* nvEncGetEncodePresetConfigEx;
    void* nvEncGetSequenceParamEx;
    void* nvEncRestoreEncoderState;
    void* nvEncLookaheadPicture;
    void* reserved2[275];
} NVENC_FN_LIST;
#endif

namespace nvenc {

// GUID definitions for NVENC Codecs and Presets
inline constexpr GUID NV_ENC_CODEC_H264_GUID_LOCAL =
    { 0x6bc82762, 0x4e63, 0x4ca4, { 0xaa, 0x85, 0x1e, 0x50, 0xf3, 0x21, 0xf6, 0xbf } };
inline constexpr GUID NV_ENC_CODEC_HEVC_GUID_LOCAL =
    { 0x790cdc88, 0x4522, 0x4d7b, { 0x94, 0x25, 0xbd, 0xa9, 0x97, 0x5f, 0x76, 0x03 } };
inline constexpr GUID NV_ENC_CODEC_AV1_GUID_LOCAL =
    { 0x0a352289, 0x0aa7, 0x4759, { 0x86, 0x2d, 0x5d, 0x15, 0xcd, 0x16, 0xd2, 0x54 } };
inline constexpr GUID NV_ENC_CODEC_PROFILE_AUTOSELECT_GUID_LOCAL =
    { 0xbfd6f8e7, 0x233c, 0x4341, { 0x8b, 0x3e, 0x48, 0x18, 0x52, 0x38, 0x03, 0xf4 } };
inline constexpr GUID NV_ENC_H264_PROFILE_HIGH_GUID_LOCAL =
    { 0xe7cbc309, 0x4f7a, 0x4b89, { 0xaf, 0x2a, 0xd5, 0x37, 0xc9, 0x2b, 0xe3, 0x10 } };
inline constexpr GUID NV_ENC_HEVC_PROFILE_MAIN_GUID_LOCAL =
    { 0xb514c39a, 0xb55b, 0x40fa, { 0x87, 0x8f, 0xf1, 0x25, 0x3b, 0x4d, 0xfd, 0xec } };
inline constexpr GUID NV_ENC_HEVC_PROFILE_MAIN10_GUID_LOCAL =
    { 0xfa4d2b6c, 0x3a5b, 0x411a, { 0x80, 0x18, 0x0a, 0x3f, 0x5e, 0x3c, 0x9b, 0xe5 } };
inline constexpr GUID NV_ENC_AV1_PROFILE_MAIN_GUID_LOCAL =
    { 0x5f2a39f5, 0xf14e, 0x4f95, { 0x9a, 0x9e, 0xb7, 0x6d, 0x56, 0x8f, 0xcf, 0x97 } };

inline constexpr GUID NV_ENC_PRESET_P1_GUID_LOCAL =
    { 0xfc0a8d3e, 0x45f8, 0x4cf8, { 0x80, 0xc7, 0x29, 0x88, 0x71, 0x59, 0x0e, 0xbf } };
inline constexpr GUID NV_ENC_PRESET_P2_GUID_LOCAL =
    { 0xf581cfb8, 0x88d6, 0x4381, { 0x93, 0xf0, 0xdf, 0x13, 0xf9, 0xc2, 0x7d, 0xab } };
inline constexpr GUID NV_ENC_PRESET_P3_GUID_LOCAL =
    { 0x36850110, 0x3a07, 0x441f, { 0x94, 0xd5, 0x36, 0x70, 0x63, 0x1f, 0x91, 0xf6 } };
inline constexpr GUID NV_ENC_PRESET_P4_GUID_LOCAL =
    { 0x90a7b826, 0xdf06, 0x4862, { 0xb9, 0xd2, 0xcd, 0x6d, 0x73, 0xa0, 0x86, 0x81 } };
inline constexpr GUID NV_ENC_PRESET_P5_GUID_LOCAL =
    { 0x21c6e6b4, 0x297a, 0x4cba, { 0x99, 0x8f, 0xb6, 0xcb, 0xde, 0x72, 0xad, 0xe3 } };
inline constexpr GUID NV_ENC_PRESET_P6_GUID_LOCAL =
    { 0x8e75c279, 0x6299, 0x4ab6, { 0x83, 0x02, 0x0b, 0x21, 0x5a, 0x33, 0x5c, 0xf5 } };
inline constexpr GUID NV_ENC_PRESET_P7_GUID_LOCAL =
    { 0x84848c12, 0x6f71, 0x4c13, { 0x93, 0x1b, 0x53, 0xe2, 0x83, 0xf5, 0x79, 0x74 } };

using NVENCSTATUS = int32_t;
inline constexpr int32_t NV_ENC_SUCCESS = 0;

inline constexpr uint32_t NVENCAPI_MAJOR_VERSION = 13;
inline constexpr uint32_t NVENCAPI_MINOR_VERSION = 1;
inline constexpr uint32_t NVENCAPI_VERSION = (NVENCAPI_MAJOR_VERSION | (NVENCAPI_MINOR_VERSION << 24));
#define NVENCAPI_STRUCT_VERSION(ver) ((uint32_t)moonshine::encoder::nvenc::NVENCAPI_VERSION | ((ver) << 16) | (0x7 << 28))

inline constexpr uint32_t NVENC_INFINITE_GOPLENGTH = 0xffffffff;

inline constexpr uint32_t NV_ENC_DEVICE_TYPE_DIRECTX = 0x0;
inline constexpr uint32_t NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX = 0x0;
inline constexpr uint32_t NV_ENC_INPUT_IMAGE = 0x0;

inline constexpr uint32_t NV_ENC_PARAMS_FRAME_FIELD_MODE_FRAME = 0x01;
inline constexpr uint32_t NV_ENC_PARAMS_RC_CONSTQP = 0x0;
inline constexpr uint32_t NV_ENC_PARAMS_RC_VBR = 0x1;
inline constexpr uint32_t NV_ENC_PARAMS_RC_CBR = 0x2;

inline constexpr uint32_t NV_ENC_PIC_FLAG_EOS = 0x1;
inline constexpr uint32_t NV_ENC_PIC_FLAG_FORCEIDR = 0x2;
inline constexpr uint32_t NV_ENC_PIC_FLAG_OUTPUT_SPSPPS = 0x4;
inline constexpr uint32_t NV_ENC_PIC_STRUCT_FRAME = 0x01;

inline constexpr uint32_t NV_ENC_PIC_TYPE_P = 0x0;
inline constexpr uint32_t NV_ENC_PIC_TYPE_B = 0x01;
inline constexpr uint32_t NV_ENC_PIC_TYPE_I = 0x02;
inline constexpr uint32_t NV_ENC_PIC_TYPE_IDR = 0x03;

inline constexpr uint32_t NV_ENC_BUFFER_FORMAT_NV12 = 0x00000001;
inline constexpr uint32_t NV_ENC_BUFFER_FORMAT_P010 = 0x00000040;
inline constexpr uint32_t NV_ENC_BUFFER_FORMAT_ARGB = 0x01000000;
inline constexpr uint32_t NV_ENC_BUFFER_FORMAT_ABGR = 0x10000000;
inline constexpr uint32_t NV_ENC_BUFFER_FORMAT_ABGR10 = 0x20000000;

inline constexpr uint32_t NV_ENC_TUNING_INFO_HIGH_QUALITY = 1;
inline constexpr uint32_t NV_ENC_TUNING_INFO_LOW_LATENCY = 2;
inline constexpr uint32_t NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY = 3;
inline constexpr uint32_t NV_ENC_TUNING_INFO_LOSSLESS = 4;

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

typedef struct _NVENC_EXTERNAL_ME_HINT_COUNTS_PER_BLOCKTYPE {
    uint32_t numCandsPerBlk16x16 : 4;
    uint32_t numCandsPerBlk16x8  : 4;
    uint32_t numCandsPerBlk8x16  : 4;
    uint32_t numCandsPerBlk8x8   : 4;
    uint32_t numCandsPerSb       : 8;
    uint32_t reserved            : 8;
    uint32_t reserved1[3];
} NVENC_EXTERNAL_ME_HINT_COUNTS_PER_BLOCKTYPE;

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
    NVENC_EXTERNAL_ME_HINT_COUNTS_PER_BLOCKTYPE maxMEHintCountsPerBlock[2];
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
typedef NVENCSTATUS(NVENCAPI* PNVENCGETENCODEGUIDCOUNT)(void*, uint32_t*);
typedef NVENCSTATUS(NVENCAPI* PNVENCGETENCODEGUIDS)(void*, GUID*, uint32_t, uint32_t*);
typedef NVENCSTATUS(NVENCAPI* PNVENCINVALIDATEREFFRAMES)(void*, uint64_t);
inline constexpr uint64_t NV_ENC_INVALIDATE_ALL_REF_FRAMES = 0xFFFFFFFFFFFFFFFFULL;

} // namespace nvenc
} // namespace moonshine::encoder
