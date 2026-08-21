#ifndef MOONSHINE_SHARED_AUDIO_BUFFER_H
#define MOONSHINE_SHARED_AUDIO_BUFFER_H

#ifdef __cplusplus
#include <cstdint>
#include <cstddef>
extern "C" {
#else
#include <stdint.h>
#include <stddef.h>
#endif

#pragma pack(push, 8)

/* Magic identifier for Moonshine shared audio buffers ("MSHNAUD1") */
#define MOONSHINE_AUDIO_MAGIC 0x314455414E48534DLL

/* Protocol version */
#define MOONSHINE_AUDIO_VERSION 1

/* Standard buffer sizing */
#define MOONSHINE_AUDIO_MAX_CHANNELS 8
#define MOONSHINE_AUDIO_MAX_SAMPLE_RATE 192000
#define MOONSHINE_AUDIO_MIN_SAMPLE_RATE 44100
#define MOONSHINE_AUDIO_DEFAULT_SAMPLE_RATE 48000
#define MOONSHINE_AUDIO_DEFAULT_FRAME_MS 10
#define MOONSHINE_AUDIO_RING_BUFFER_FRAMES 16

/* Sample Formats */
typedef enum MoonshineAudioSampleFormat {
    MOONSHINE_FORMAT_PCM_16 = 1,
    MOONSHINE_FORMAT_PCM_24 = 2,
    MOONSHINE_FORMAT_PCM_32 = 3,
    MOONSHINE_FORMAT_FLOAT_32 = 4
} MoonshineAudioSampleFormat;

/* Channel Layouts */
typedef enum MoonshineAudioChannelLayout {
    MOONSHINE_LAYOUT_MONO = 1,
    MOONSHINE_LAYOUT_STEREO = 2,
    MOONSHINE_LAYOUT_SURROUND_51 = 6,
    MOONSHINE_LAYOUT_SURROUND_71 = 8
} MoonshineAudioChannelLayout;

/* Device Endpoint Direction */
typedef enum MoonshineAudioEndpointType {
    MOONSHINE_ENDPOINT_RENDER = 0,   /* Speaker / Playback */
    MOONSHINE_ENDPOINT_CAPTURE = 1    /* Microphone / Recording */
} MoonshineAudioEndpointType;

/* Shared Ring Buffer Header - Cacheline aligned (64 bytes) for lock-free cross-process IPC */
typedef struct MoonshineSharedAudioRing {
    /* 64-byte Cacheline 1: Producer Write State */
    uint64_t magic;
    uint32_t version;
    uint32_t endpoint_type;
    volatile uint32_t write_position_bytes;
    volatile uint32_t write_packet_count;
    uint8_t pad1[40];

    /* 64-byte Cacheline 2: Consumer Read State */
    volatile uint32_t read_position_bytes;
    volatile uint32_t read_packet_count;
    volatile uint32_t underrun_count;
    volatile uint32_t overrun_count;
    uint8_t pad2[48];

    /* 64-byte Cacheline 3: Audio Format Parameters */
    uint32_t sample_rate;
    uint32_t channels;
    uint32_t sample_format;
    uint32_t bytes_per_sample;
    uint32_t frame_size_bytes;
    uint32_t buffer_capacity_bytes;
    uint32_t latency_ms;
    volatile uint32_t is_active;
    volatile uint32_t is_muted;
    float volume_scalar;
    uint8_t pad3[24];
} MoonshineSharedAudioRing;

/* IOCTL Definitions for Driver Management */
#define MOONSHINE_AUDIO_IOCTL_BASE 0x8000
#define MOONSHINE_AUDIO_IOCTL_GET_STATUS     0x8001
#define MOONSHINE_AUDIO_IOCTL_SET_FORMAT     0x8002
#define MOONSHINE_AUDIO_IOCTL_GET_BUFFER_PTR 0x8003
#define MOONSHINE_AUDIO_IOCTL_RESET_BUFFER   0x8004

#pragma pack(pop)

#ifdef __cplusplus
}
#endif

#endif /* MOONSHINE_SHARED_AUDIO_BUFFER_H */
