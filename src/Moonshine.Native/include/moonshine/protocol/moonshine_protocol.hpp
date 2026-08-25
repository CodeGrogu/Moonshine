#ifndef MOONSHINE_PROTOCOL_HPP
#define MOONSHINE_PROTOCOL_HPP

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <span>
#include <bit>

namespace moonshine::protocol {

inline constexpr uint32_t MOONSHINE_MAGIC = 0x4D53484EU; // 'MSHN'
inline constexpr uint16_t MOONSHINE_VERSION_1_0 = 0x0001U;

enum class MoonshineMessageType : uint16_t {
    None = 0x0000,

    // Discovery & Beacon
    DiscoveryProbe = 0x0001,
    DiscoveryResponse = 0x0002,
    DiscoveryAnnouncement = 0x0003,

    // Control & Session
    Hello = 0x0101,
    HelloResponse = 0x0102,
    SessionSetup = 0x0103,
    SessionSetupResponse = 0x0104,
    KeepAlive = 0x0105,
    KeepAliveAck = 0x0106,
    Teardown = 0x0107,

    // Media
    VideoPacket = 0x0201,
    AudioPacket = 0x0301,
    MicPacket = 0x0401,

    // Feedback & QoS
    FeedbackLossStats = 0x0501,
    IdrRequest = 0x0502,

    // Input
    InputKeyboard = 0x0601,
    InputMouse = 0x0602,
    InputGamepad = 0x0603,

    // Telemetry
    TelemetryReport = 0x0701,

    // Host Management & Remote Configuration
    GetHostCapabilities = 0x0801,
    HostCapabilitiesResponse = 0x0802,
    GetHostConfiguration = 0x0803,
    HostConfigurationResponse = 0x0804,
    SetHostConfiguration = 0x0805,
    SetHostConfigurationResponse = 0x0806,
    ConfigurationChanged = 0x0807
};

enum class MoonshineErrorCode : uint32_t {
    Success = 0,
    InvalidMagic = 1,
    UnsupportedVersion = 2,
    MalformedHeader = 3,
    BufferTooSmall = 4,
    PayloadTruncated = 5,
    InvalidSession = 6,
    AuthenticationFailed = 7,
    StreamNotFound = 8,
    DuplicateSequence = 9,
    StaleTimestamp = 10,
    UnsupportedCodec = 11,
    UnauthorizedConfiguration = 12,
    InvalidConfigurationParameter = 13
};

#pragma pack(push, 1)

/**
 * @brief Canonical 128-bit UUID (RFC 4122 Big-Endian byte buffer).
 */
struct MoonshineUuid128 {
    uint8_t bytes[16];

    bool operator==(const MoonshineUuid128& other) const noexcept {
        return std::memcmp(bytes, other.bytes, 16) == 0;
    }
};

/**
 * @brief Global 32-byte packet envelope header.
 */
struct MoonshinePacketHeader {
    uint32_t magic;
    uint16_t version;
    uint16_t message_type;
    uint32_t payload_size;
    uint32_t sequence_number;
    uint64_t session_id;
    uint64_t timestamp_us;
};

/**
 * @brief Hello payload for version negotiation and capabilities exchange (32 bytes).
 */
struct MoonshineHelloPayload {
    uint16_t client_version_major;
    uint16_t client_version_minor;
    uint32_t capabilities_mask;
    uint64_t client_nonce;
    MoonshineUuid128 client_uuid;
};

/**
 * @brief HelloResponse payload returned by host (48 bytes).
 */
struct MoonshineHelloResponsePayload {
    uint16_t server_version_major;
    uint16_t server_version_minor;
    uint32_t negotiated_capabilities;
    uint64_t assigned_session_id;
    uint64_t server_nonce;
    MoonshineUuid128 challenge_salt;
    uint32_t session_lease_seconds;
    uint32_t reserved;
};

/**
 * @brief SessionSetup payload configuring video, audio, and network streams (40 bytes).
 */
struct MoonshineSessionSetupPayload {
    uint32_t video_width;
    uint32_t video_height;
    uint32_t video_fps;
    uint32_t video_bitrate_kbps;
    uint8_t video_codec;
    uint8_t video_color_format;
    uint8_t audio_channels;
    uint8_t audio_codec;
    uint32_t audio_sample_rate;
    uint32_t audio_bitrate_kbps;
    uint16_t client_udp_video_port;
    uint16_t client_udp_audio_port;
    uint16_t client_udp_feedback_port;
    uint16_t reserved;
    uint32_t mtu_payload_size;
};

/**
 * @brief SessionSetupResponse payload confirming stream allocation (32 bytes).
 */
struct MoonshineSessionSetupResponsePayload {
    uint32_t status_code;
    uint32_t video_stream_id;
    uint32_t audio_stream_id;
    uint32_t feedback_stream_id;
    uint16_t host_udp_video_port;
    uint16_t host_udp_audio_port;
    uint16_t host_udp_feedback_port;
    uint16_t host_udp_input_port;
    uint32_t negotiated_mtu;
    uint32_t reserved;
};

/**
 * @brief Video packet framing header (32 bytes).
 */
struct MoonshineVideoPacketHeader {
    uint32_t stream_id;
    uint64_t frame_index;
    uint32_t packet_index;
    uint32_t total_packets;
    uint32_t fec_block_index;
    uint16_t payload_size;
    uint8_t packet_type;
    uint8_t flags;
    uint32_t reserved;
};

/**
 * @brief Audio packet framing header (24 bytes).
 */
struct MoonshineAudioPacketHeader {
    uint32_t stream_id;
    uint64_t sample_index;
    uint32_t sample_rate;
    uint16_t frame_duration_us;
    uint16_t payload_size;
    uint8_t channels;
    uint8_t codec;
    uint16_t reserved;
};

/**
 * @brief Microphone backchannel packet framing header (20 bytes).
 */
struct MoonshineMicPacketHeader {
    uint32_t stream_id;
    uint64_t sample_index;
    uint16_t payload_size;
    uint8_t channels;
    uint8_t codec;
    uint32_t sample_rate;
};

/**
 * @brief Feedback loss and RTT statistics payload (40 bytes).
 *
 * Invariant: `last_received_frame_index` represents the client's highest observed
 * monotonic stream frame index position. Media frames strictly advance monotonically
 * per stream, enabling out-of-order/stale UDP feedback datagrams to be deterministically filtered.
 */
struct MoonshineFeedbackLossStatsPayload {
    uint32_t stream_id;
    uint64_t last_received_frame_index;
    uint32_t packets_received;
    uint32_t packets_lost;
    uint32_t packets_recovered_fec;
    uint32_t round_trip_time_us;
    uint32_t jitter_us;
    uint32_t estimated_bandwidth_kbps;
    uint32_t receive_queue_depth;
};

/**
 * @brief IDR request payload (16 bytes).
 */
struct MoonshineIdrRequestPayload {
    uint32_t stream_id;
    uint64_t last_valid_frame_index;
    uint32_t reason_code;
};

/**
 * @brief Keyboard input injection payload (12 bytes).
 */
struct MoonshineInputKeyboardPayload {
    uint16_t key_code;
    uint16_t scan_code;
    uint8_t is_down;
    uint8_t modifiers;
    uint16_t reserved;
    uint32_t timestamp_offset_us;
};

/**
 * @brief Mouse input injection payload (20 bytes).
 */
struct MoonshineInputMousePayload {
    int32_t x;
    int32_t y;
    int16_t wheel_delta_y;
    int16_t wheel_delta_x;
    uint16_t button_flags;
    uint8_t is_absolute;
    uint8_t reserved;
    uint32_t timestamp_offset_us;
};

/**
 * @brief Gamepad input injection payload (24 bytes).
 */
struct MoonshineInputGamepadPayload {
    uint8_t gamepad_index;
    uint8_t reserved;
    uint16_t button_mask;
    uint8_t left_trigger;
    uint8_t right_trigger;
    int16_t thumb_lx;
    int16_t thumb_ly;
    int16_t thumb_rx;
    int16_t thumb_ry;
    uint16_t motor_left;
    uint16_t motor_right;
    uint32_t timestamp_offset_us;
    uint16_t reserved2;
};

/**
 * @brief Telemetry metrics report payload (32 bytes).
 */
struct MoonshineTelemetryReportPayload {
    uint32_t encode_latency_us;
    uint32_t decode_latency_us;
    uint32_t render_latency_us;
    uint32_t network_latency_us;
    uint32_t frames_rendered;
    uint32_t frames_dropped;
    uint32_t fec_recovered_frames;
    uint32_t reserved;
};

/**
 * @brief Host capabilities query response payload (32 bytes).
 */
struct MoonshineHostCapabilitiesResponsePayload {
    uint32_t supported_video_codecs;
    uint32_t supported_audio_codecs;
    uint32_t max_encode_width;
    uint32_t max_encode_height;
    uint32_t max_encode_fps;
    uint8_t supports_hdr10;
    uint8_t supports_virtual_audio;
    uint8_t supports_mic_backchannel;
    uint8_t reserved;
    uint32_t max_bitrate_kbps;
    uint32_t reserved2;
};

/**
 * @brief Host configuration query response / set payload (48 bytes).
 */
struct MoonshineHostConfigurationPayload {
    uint32_t config_version;
    uint32_t display_width;
    uint32_t display_height;
    uint32_t refresh_rate_hz;
    uint32_t target_bitrate_kbps;
    uint32_t max_bitrate_kbps;
    uint8_t preferred_codec;
    uint8_t hdr10_enabled;
    uint8_t audio_channels;
    uint8_t audio_quality_mode;
    uint32_t audio_bitrate_kbps;
    uint16_t input_polling_rate_hz;
    uint8_t mic_passthrough_enabled;
    uint8_t virtual_audio_driver_enabled;
    uint32_t reserved1;
    uint32_t reserved2;
    uint32_t reserved3;
};

/**
 * @brief SetHostConfigurationResponse payload (8 bytes).
 */
struct MoonshineSetHostConfigurationResponsePayload {
    uint32_t status_code;
    uint32_t applied_config_version;
};

/**
 * @brief ConfigurationChanged payload (8 bytes).
 */
struct MoonshineConfigurationChangedPayload {
    uint32_t new_config_version;
    uint32_t change_reason_flags;
};

#pragma pack(pop)

// Compile-time static assertions ensuring exact binary layout sizes
static_assert(sizeof(MoonshineUuid128) == 16, "MoonshineUuid128 must be exactly 16 bytes");
static_assert(sizeof(MoonshinePacketHeader) == 32, "MoonshinePacketHeader size must be exactly 32 bytes");
static_assert(sizeof(MoonshineHelloPayload) == 32, "MoonshineHelloPayload size must be exactly 32 bytes");
static_assert(sizeof(MoonshineHelloResponsePayload) == 48, "MoonshineHelloResponsePayload size must be exactly 48 bytes");
static_assert(sizeof(MoonshineSessionSetupPayload) == 40, "MoonshineSessionSetupPayload size must be exactly 40 bytes");
static_assert(sizeof(MoonshineSessionSetupResponsePayload) == 32, "MoonshineSessionSetupResponsePayload size must be exactly 32 bytes");
static_assert(sizeof(MoonshineVideoPacketHeader) == 32, "MoonshineVideoPacketHeader size must be exactly 32 bytes");
static_assert(sizeof(MoonshineAudioPacketHeader) == 24, "MoonshineAudioPacketHeader size must be exactly 24 bytes");
static_assert(sizeof(MoonshineMicPacketHeader) == 20, "MoonshineMicPacketHeader size must be exactly 20 bytes");
static_assert(sizeof(MoonshineFeedbackLossStatsPayload) == 40, "MoonshineFeedbackLossStatsPayload size must be exactly 40 bytes");
static_assert(sizeof(MoonshineIdrRequestPayload) == 16, "MoonshineIdrRequestPayload size must be exactly 16 bytes");
static_assert(sizeof(MoonshineInputKeyboardPayload) == 12, "MoonshineInputKeyboardPayload size must be exactly 12 bytes");
static_assert(sizeof(MoonshineInputMousePayload) == 20, "MoonshineInputMousePayload size must be exactly 20 bytes");
static_assert(sizeof(MoonshineInputGamepadPayload) == 24, "MoonshineInputGamepadPayload size must be exactly 24 bytes");
static_assert(sizeof(MoonshineTelemetryReportPayload) == 32, "MoonshineTelemetryReportPayload size must be exactly 32 bytes");
static_assert(sizeof(MoonshineHostCapabilitiesResponsePayload) == 32, "MoonshineHostCapabilitiesResponsePayload size must be exactly 32 bytes");
static_assert(sizeof(MoonshineHostConfigurationPayload) == 48, "MoonshineHostConfigurationPayload size must be exactly 48 bytes");
static_assert(sizeof(MoonshineSetHostConfigurationResponsePayload) == 8, "MoonshineSetHostConfigurationResponsePayload size must be exactly 8 bytes");
static_assert(sizeof(MoonshineConfigurationChangedPayload) == 8, "MoonshineConfigurationChangedPayload size must be exactly 8 bytes");

// Helper for Big-Endian wire conversion
template <typename T>
[[nodiscard]] constexpr T to_big_endian(T value) noexcept {
    if constexpr (std::endian::native == std::endian::big) {
        return value;
    } else {
        return std::byteswap(value);
    }
}

template <typename T>
[[nodiscard]] constexpr T from_big_endian(T value) noexcept {
    return to_big_endian(value);
}

/**
 * @brief Serialises a packet header into big-endian wire format.
 */
inline bool write_header(const MoonshinePacketHeader& header, std::span<uint8_t> dest) noexcept {
    if (dest.size() < sizeof(MoonshinePacketHeader)) {
        return false;
    }

    MoonshinePacketHeader wire{};
    wire.magic = to_big_endian(header.magic);
    wire.version = to_big_endian(header.version);
    wire.message_type = to_big_endian(header.message_type);
    wire.payload_size = to_big_endian(header.payload_size);
    wire.sequence_number = to_big_endian(header.sequence_number);
    wire.session_id = to_big_endian(header.session_id);
    wire.timestamp_us = to_big_endian(header.timestamp_us);

    std::memcpy(dest.data(), &wire, sizeof(MoonshinePacketHeader));
    return true;
}

[[nodiscard]] inline constexpr uint32_t get_minimum_payload_size(MoonshineMessageType message_type) noexcept {
    switch (message_type) {
        case MoonshineMessageType::Hello: return 32;
        case MoonshineMessageType::HelloResponse: return 48;
        case MoonshineMessageType::SessionSetup: return 40;
        case MoonshineMessageType::SessionSetupResponse: return 32;
        case MoonshineMessageType::FeedbackLossStats: return 40;
        case MoonshineMessageType::IdrRequest: return 16;
        case MoonshineMessageType::InputKeyboard: return 12;
        case MoonshineMessageType::InputMouse: return 20;
        case MoonshineMessageType::InputGamepad: return 24;
        case MoonshineMessageType::TelemetryReport: return 32;
        case MoonshineMessageType::GetHostCapabilities: return 4;
        case MoonshineMessageType::HostCapabilitiesResponse: return 32;
        case MoonshineMessageType::GetHostConfiguration: return 4;
        case MoonshineMessageType::HostConfigurationResponse: return 48;
        case MoonshineMessageType::SetHostConfiguration: return 48;
        case MoonshineMessageType::SetHostConfigurationResponse: return 8;
        case MoonshineMessageType::ConfigurationChanged: return 8;
        case MoonshineMessageType::VideoPacket: return 32;
        case MoonshineMessageType::AudioPacket: return 24;
        case MoonshineMessageType::MicPacket: return 20;
        default: return 0;
    }
}

/**
 * @brief Deserialises and validates a packet header from big-endian wire format.
 */
inline MoonshineErrorCode read_header(std::span<const uint8_t> source, MoonshinePacketHeader& out_header) noexcept {
    if (source.size() < sizeof(MoonshinePacketHeader)) {
        return MoonshineErrorCode::BufferTooSmall;
    }

    MoonshinePacketHeader wire{};
    std::memcpy(&wire, source.data(), sizeof(MoonshinePacketHeader));

    out_header.magic = from_big_endian(wire.magic);
    out_header.version = from_big_endian(wire.version);
    out_header.message_type = from_big_endian(wire.message_type);
    out_header.payload_size = from_big_endian(wire.payload_size);
    out_header.sequence_number = from_big_endian(wire.sequence_number);
    out_header.session_id = from_big_endian(wire.session_id);
    out_header.timestamp_us = from_big_endian(wire.timestamp_us);

    if (out_header.magic != MOONSHINE_MAGIC) {
        return MoonshineErrorCode::InvalidMagic;
    }

    if (out_header.version != MOONSHINE_VERSION_1_0) {
        return MoonshineErrorCode::UnsupportedVersion;
    }

    if (out_header.payload_size > 1048576) {
        return MoonshineErrorCode::MalformedHeader;
    }

    if (source.size() < sizeof(MoonshinePacketHeader) + out_header.payload_size) {
        return MoonshineErrorCode::PayloadTruncated;
    }

    auto msg_type = static_cast<MoonshineMessageType>(out_header.message_type);
    if (out_header.payload_size < get_minimum_payload_size(msg_type)) {
        return MoonshineErrorCode::PayloadTruncated;
    }

    return MoonshineErrorCode::Success;
}

[[nodiscard]] inline constexpr bool is_newer_sequence16(uint16_t candidate, uint16_t previous) noexcept {
    return candidate != previous && static_cast<int16_t>(static_cast<uint16_t>(candidate - previous)) > 0;
}

[[nodiscard]] inline constexpr bool is_newer_sequence(uint32_t candidate, uint32_t previous) noexcept {
    return candidate != previous && static_cast<int32_t>(candidate - previous) > 0;
}

[[nodiscard]] inline constexpr bool is_newer_sequence32(uint32_t candidate, uint32_t previous) noexcept {
    return is_newer_sequence(candidate, previous);
}

[[nodiscard]] inline constexpr bool is_newer_frame_index(uint64_t candidate, uint64_t previous) noexcept {
    return candidate != previous && static_cast<int64_t>(candidate - previous) > 0;
}

[[nodiscard]] inline constexpr bool is_newer_sequence64(uint64_t candidate, uint64_t previous) noexcept {
    return is_newer_frame_index(candidate, previous);
}

[[nodiscard]] inline constexpr int16_t sequence_distance16(uint16_t candidate, uint16_t previous) noexcept {
    return static_cast<int16_t>(static_cast<uint16_t>(candidate - previous));
}

[[nodiscard]] inline constexpr int32_t sequence_distance32(uint32_t candidate, uint32_t previous) noexcept {
    return static_cast<int32_t>(candidate - previous);
}

[[nodiscard]] inline constexpr int64_t sequence_distance64(uint64_t candidate, uint64_t previous) noexcept {
    return static_cast<int64_t>(candidate - previous);
}

[[nodiscard]] inline constexpr bool requires_session_id(MoonshineMessageType message_type) noexcept {
    switch (message_type) {
        case MoonshineMessageType::Hello:
        case MoonshineMessageType::HelloResponse:
        case MoonshineMessageType::DiscoveryProbe:
        case MoonshineMessageType::DiscoveryAnnouncement:
        case MoonshineMessageType::DiscoveryResponse:
        case MoonshineMessageType::GetHostCapabilities:
        case MoonshineMessageType::HostCapabilitiesResponse:
        case MoonshineMessageType::GetHostConfiguration:
        case MoonshineMessageType::HostConfigurationResponse:
        case MoonshineMessageType::SetHostConfiguration:
        case MoonshineMessageType::SetHostConfigurationResponse:
        case MoonshineMessageType::ConfigurationChanged:
            return false;
        default:
            return true;
    }
}

[[nodiscard]] inline constexpr bool requires_authentication(MoonshineMessageType message_type) noexcept {
    switch (message_type) {
        case MoonshineMessageType::GetHostCapabilities:
        case MoonshineMessageType::HostCapabilitiesResponse:
        case MoonshineMessageType::GetHostConfiguration:
        case MoonshineMessageType::HostConfigurationResponse:
        case MoonshineMessageType::SetHostConfiguration:
        case MoonshineMessageType::SetHostConfigurationResponse:
        case MoonshineMessageType::ConfigurationChanged:
            return true;
        default:
            return false;
    }
}

} // namespace moonshine::protocol

#endif // MOONSHINE_PROTOCOL_HPP
