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
#endif

namespace moonshine::encoder::amf {

using amf_int32 = int32_t;
using amf_uint32 = uint32_t;
using amf_int64 = int64_t;
using amf_uint64 = uint64_t;
using amf_size = size_t;
using amf_pts = int64_t;
using amf_bool = bool;

enum AMF_RESULT : int32_t {
    AMF_OK = 0,
    AMF_FAIL = 1,
    AMF_UNEXPECTED = 2,
    AMF_ACCESS_DENIED = 3,
    AMF_INVALID_ARG = 4,
    AMF_OUT_OF_RANGE = 5,
    AMF_OUT_OF_MEMORY = 6,
    AMF_INVALID_POINTER = 7,
    AMF_NO_INTERFACE = 8,
    AMF_NOT_IMPLEMENTED = 9,
    AMF_NOT_SUPPORTED = 10,
    AMF_NOT_FOUND = 11,
    AMF_ALREADY_INITIALIZED = 12,
    AMF_NOT_INITIALIZED = 13,
    AMF_INVALID_FORMAT = 14,
    AMF_WRONG_STATE = 15,
    AMF_FILE_NOT_OPEN = 16,
    AMF_NO_DEVICE = 17,
    AMF_DIRECTX = 18,
    AMF_OPENCL = 19,
    AMF_GLX = 20,
    AMF_XV = 21,
    AMF_ALSA = 22,
    AMF_EOF = 23,
    AMF_REPEAT = 24,
    AMF_INPUT_FULL = 25,
    AMF_RESOLUTION_CHANGED = 26,
    AMF_RESOLUTION_UPDATED = 27,
    AMF_INVALID_DATA_TYPE = 28,
    AMF_INVALID_RESOLUTION = 29,
    AMF_CODEC_NOT_SUPPORTED = 30,
    AMF_SURFACE_FORMAT_NOT_SUPPORTED = 31,
    AMF_NEED_MORE_INPUT = 32
};

enum AMF_MEMORY_TYPE : int32_t {
    AMF_MEMORY_UNKNOWN = 0,
    AMF_MEMORY_HOST = 1,
    AMF_MEMORY_DX9 = 2,
    AMF_MEMORY_DX11 = 3,
    AMF_MEMORY_OPENCL = 4,
    AMF_MEMORY_OPENGL = 5,
    AMF_MEMORY_XV = 6,
    AMF_MEMORY_GRALLOC = 7,
    AMF_MEMORY_COMPUTE = 8,
    AMF_MEMORY_VULKAN = 9,
    AMF_MEMORY_DX12 = 10
};

enum AMF_SURFACE_FORMAT : int32_t {
    AMF_SURFACE_UNKNOWN = 0,
    AMF_SURFACE_NV12 = 1,
    AMF_SURFACE_YV12 = 2,
    AMF_SURFACE_BGRA = 3,
    AMF_SURFACE_ARGB = 4,
    AMF_SURFACE_RGBA = 5,
    AMF_SURFACE_GRAY8 = 6,
    AMF_SURFACE_YUV420P = 7,
    AMF_SURFACE_U8V8 = 8,
    AMF_SURFACE_YUY2 = 9,
    AMF_SURFACE_P010 = 10,
    AMF_SURFACE_RGBA_F16 = 11,
    AMF_SURFACE_UYVY = 12,
    AMF_SURFACE_R10G10B10A2 = 13
};

enum AMF_DATA_TYPE : int32_t {
    AMF_DATA_BUFFER = 0,
    AMF_DATA_SURFACE = 1,
    AMF_DATA_AUDIO_BUFFER = 2,
    AMF_DATA_USER = 1000
};

enum AMF_VARIANT_TYPE : int32_t {
    AMF_VARIANT_EMPTY = 0,
    AMF_VARIANT_BOOL = 1,
    AMF_VARIANT_INT64 = 2,
    AMF_VARIANT_DOUBLE = 3,
    AMF_VARIANT_STRING = 4,
    AMF_VARIANT_WSTRING = 5,
    AMF_VARIANT_INTERFACE = 6,
    AMF_VARIANT_FLOAT = 7
};

#pragma pack(push, 8)
struct AMFVariantStruct {
    AMF_VARIANT_TYPE type;
    uint32_t reserved;
    union {
        amf_bool boolValue;
        amf_int64 int64Value;
        double doubleValue;
        char* stringValue;
        wchar_t* wstringValue;
        void* pInterface;
        float floatValue;
    };
};
#pragma pack(pop)

#define AMF_MAKE_FULL_VERSION(MAJOR, MINOR, RELEASE, BUILD) \
    ((amf_uint64(MAJOR) << 48) | (amf_uint64(MINOR) << 32) | (amf_uint64(RELEASE) << 16) | amf_uint64(BUILD))

constexpr amf_uint64 AMF_FULL_VERSION = AMF_MAKE_FULL_VERSION(1, 4, 34, 0);

// Core AMF Interfaces (COM-style vtables)
struct AMFInterface {
    virtual amf_int64 __cdecl Acquire() = 0;
    virtual amf_int64 __cdecl Release() = 0;
    virtual AMF_RESULT __cdecl QueryInterface(const void* interfaceID, void** ppInterface) = 0;
};

struct AMFPropertyStorage : public AMFInterface {
    virtual AMF_RESULT __cdecl SetProperty(const wchar_t* name, AMFVariantStruct value) = 0;
    virtual AMF_RESULT __cdecl GetProperty(const wchar_t* name, AMFVariantStruct* pValue) const = 0;
    virtual amf_bool __cdecl HasProperty(const wchar_t* name) const = 0;
    virtual amf_size __cdecl GetPropertyCount() const = 0;
    virtual AMF_RESULT __cdecl GetPropertyAt(amf_size index, wchar_t* name, amf_size nameSize, AMFVariantStruct* pValue) const = 0;
    virtual AMF_RESULT __cdecl Clear() = 0;
    virtual AMF_RESULT __cdecl AddTo(AMFPropertyStorage* pDest, amf_bool overwrite, amf_bool deepCopy) const = 0;
    virtual AMF_RESULT __cdecl CopyTo(AMFPropertyStorage* pDest, amf_bool deepCopy) const = 0;
};

struct AMFData : public AMFPropertyStorage {
    virtual AMF_MEMORY_TYPE __cdecl GetMemoryType() = 0;
    virtual AMF_RESULT __cdecl Duplicate(AMF_MEMORY_TYPE type, AMFData** ppData) = 0;
    virtual AMF_RESULT __cdecl Convert(AMF_MEMORY_TYPE type) = 0;
    virtual AMF_RESULT __cdecl Interop(AMF_MEMORY_TYPE type) = 0;
    virtual AMF_DATA_TYPE __cdecl GetDataType() = 0;
    virtual amf_bool __cdecl IsReusable() = 0;
    virtual void __cdecl SetPts(amf_pts pts) = 0;
    virtual amf_pts __cdecl GetPts() = 0;
    virtual void __cdecl SetDuration(amf_pts duration) = 0;
    virtual amf_pts __cdecl GetDuration() = 0;
};

struct AMFBuffer : public AMFData {
    virtual amf_size __cdecl GetSize() = 0;
    virtual void* __cdecl GetNative() = 0;
    virtual AMF_RESULT __cdecl SetSize(amf_size newSize) = 0;
};

struct AMFPlane : public AMFInterface {
    virtual AMF_SURFACE_FORMAT __cdecl GetFormat() = 0;
    virtual void* __cdecl GetNative() = 0;
    virtual amf_int32 __cdecl GetPixelSize() = 0;
    virtual amf_int32 __cdecl GetHPitch() = 0;
    virtual amf_int32 __cdecl GetVPitch() = 0;
    virtual amf_int32 __cdecl GetWidth() = 0;
    virtual amf_int32 __cdecl GetHeight() = 0;
    virtual amf_bool __cdecl IsNative() = 0;
    virtual AMF_RESULT __cdecl Convert(AMF_MEMORY_TYPE type) = 0;
    virtual AMF_RESULT __cdecl Interop(AMF_MEMORY_TYPE type) = 0;
};

struct AMFSurfaceObserver;

struct AMFSurface : public AMFData {
    virtual AMF_SURFACE_FORMAT __cdecl GetFormat() = 0;
    virtual amf_size __cdecl GetPlanesCount() = 0;
    virtual AMFPlane* __cdecl GetPlaneAt(amf_size index) = 0;
    virtual AMFPlane* __cdecl GetPlane(AMF_SURFACE_FORMAT format) = 0;
    virtual AMF_RESULT __cdecl SetObserver(AMFSurfaceObserver* pObserver) = 0;
    virtual AMFSurfaceObserver* __cdecl GetObserver() = 0;
};

struct AMFContext : public AMFPropertyStorage {
    virtual AMF_RESULT __cdecl Terminate() = 0;
    virtual AMF_RESULT __cdecl InitDX9(void* pDevice) = 0;
    virtual void* __cdecl GetDX9() = 0;
    virtual AMF_RESULT __cdecl LockDX9() = 0;
    virtual AMF_RESULT __cdecl UnlockDX9() = 0;
    virtual AMF_RESULT __cdecl InitDX11(void* pDevice, amf_int32 dxVersion = 0) = 0;
    virtual void* __cdecl GetDX11(amf_int32 dxVersion = 0) = 0;
    virtual AMF_RESULT __cdecl LockDX11() = 0;
    virtual AMF_RESULT __cdecl UnlockDX11() = 0;
    virtual AMF_RESULT __cdecl InitOpenCL(void* pCommandQueue = nullptr) = 0;
    virtual void* __cdecl GetOpenCL() = 0;
    virtual AMF_RESULT __cdecl LockOpenCL() = 0;
    virtual AMF_RESULT __cdecl UnlockOpenCL() = 0;
    virtual AMF_RESULT __cdecl InitOpenGL(void* hContext = nullptr, void* hDC = nullptr, void* hWnd = nullptr) = 0;
    virtual void* __cdecl GetOpenGL() = 0;
    virtual AMF_RESULT __cdecl LockOpenGL() = 0;
    virtual AMF_RESULT __cdecl UnlockOpenGL() = 0;
    virtual AMF_RESULT __cdecl AllocBuffer(AMF_MEMORY_TYPE type, amf_size size, AMFBuffer** ppBuffer) = 0;
    virtual AMF_RESULT __cdecl AllocSurface(AMF_MEMORY_TYPE type, AMF_SURFACE_FORMAT format, amf_int32 width, amf_int32 height, AMFSurface** ppSurface) = 0;
    virtual AMF_RESULT __cdecl CreateSurfaceFromDX11Native(void* pDX11Surface, AMFSurface** ppSurface, AMFSurfaceObserver* pObserver) = 0;
    virtual AMF_RESULT __cdecl CreateSurfaceFromDX9Native(void* pDX9Surface, AMFSurface** ppSurface, AMFSurfaceObserver* pObserver) = 0;
};

struct AMFComponent : public AMFPropertyStorage {
    virtual AMF_RESULT __cdecl Init(AMF_SURFACE_FORMAT format, amf_int32 width, amf_int32 height) = 0;
    virtual AMF_RESULT __cdecl ReInit(amf_int32 width, amf_int32 height) = 0;
    virtual AMF_RESULT __cdecl Terminate() = 0;
    virtual AMF_RESULT __cdecl Drain() = 0;
    virtual AMF_RESULT __cdecl Flush() = 0;
    virtual AMF_RESULT __cdecl SubmitInput(AMFData* pData) = 0;
    virtual AMF_RESULT __cdecl QueryOutput(AMFData** ppData) = 0;
    virtual AMF_RESULT __cdecl GetCaps(void** ppCaps) = 0;
    virtual AMF_RESULT __cdecl Optimize(void* pGraph) = 0;
    virtual void* __cdecl GetContext() = 0;
    virtual AMF_RESULT __cdecl SetOutputData(AMFData* pData) = 0;
    virtual AMF_RESULT __cdecl GetOutputData(AMFData** ppData) = 0;
};

struct AMFFactory {
    virtual AMF_RESULT __cdecl CreateContext(AMFContext** ppContext) = 0;
    virtual AMF_RESULT __cdecl CreateComponent(AMFContext* pContext, const wchar_t* id, AMFComponent** ppComponent) = 0;
    virtual AMF_RESULT __cdecl SetCacheFolder(const wchar_t* path) = 0;
    virtual const wchar_t* __cdecl GetCacheFolder() = 0;
    virtual AMF_RESULT __cdecl GetDebug(void** ppDebug) = 0;
    virtual AMF_RESULT __cdecl GetTrace(void** ppTrace) = 0;
    virtual AMF_RESULT __cdecl GetPrograms(void** ppPrograms) = 0;
};

typedef AMF_RESULT(__cdecl *AMFInit_Fn)(amf_uint64 version, AMFFactory** ppFactory);
typedef AMF_RESULT(__cdecl *AMFQueryVersion_Fn)(amf_uint64* pVersion);

// Component ID Constants
inline constexpr const wchar_t* AMFVideoEncoderVCE_AVC = L"AMFVideoEncoderVCE_AVC";
inline constexpr const wchar_t* AMFVideoEncoder_HEVC = L"AMFVideoEncoder_HEVC";
inline constexpr const wchar_t* AMFVideoEncoder_AV1 = L"AMFVideoEncoder_AV1";

// Encoder Property Name Constants
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_USAGE = L"Usage";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_PROFILE = L"Profile";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_QUALITY_PRESET = L"QualityPreset";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD = L"RateControlMethod";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_TARGET_BITRATE = L"TargetBitrate";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_PEAK_BITRATE = L"PeakBitrate";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_FRAMESIZE = L"FrameSize";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_FRAMERATE = L"FrameRate";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_B_PIC_PATTERN = L"BPicturesPattern";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_INTRA_REFRESH_NUM_MBS_PER_SLOT = L"IntraRefreshNumMBsPerSlot";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_FORCE_PICTURE_TYPE = L"ForcePictureType";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_FILLER_DATA_ENABLE = L"FillerDataEnable";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_EXTRADATA = L"ExtraData";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_END_OF_SEQUENCE = L"EndOfSequence";
inline constexpr const wchar_t* AMF_VIDEO_ENCODER_DRAIN = L"Drain";

// Capability Categorisation Status
enum class AmfCapabilityStatus : uint32_t {
    SupportedPass = 0,
    SupportedFail = 1,
    NotPresent = 2,
    DriverError = 3,
    UnsupportedCodec = 4
};

// Helper for AMFVariant initialization
inline AMFVariantStruct make_int64_variant(int64_t val) {
    AMFVariantStruct v{};
    v.type = AMF_VARIANT_INT64;
    v.int64Value = val;
    return v;
}

inline AMFVariantStruct make_bool_variant(bool val) {
    AMFVariantStruct v{};
    v.type = AMF_VARIANT_BOOL;
    v.boolValue = val;
    return v;
}

} // namespace moonshine::encoder::amf
