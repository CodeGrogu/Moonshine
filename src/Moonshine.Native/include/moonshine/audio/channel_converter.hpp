#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <algorithm>
#include <cmath>

namespace moonshine::audio {

/**
 * @brief ITU-R BS.775 compliant multi-channel audio format converter.
 * Supports Mono (1ch), Stereo (2ch), 5.1 Surround (6ch), and 7.1 Surround (8ch).
 */
class ChannelConverter {
public:
    static constexpr float kSqrt2Inv = 0.7071067811865475f; // 1 / sqrt(2) (-3 dB)

    /**
     * @brief Converts multi-channel audio from src_channels to dst_channels according to ITU-R BS.775.
     * @param src Pointer to interleaved source PCM samples.
     * @param src_channels Channel count of source.
     * @param frames Number of audio frames to convert.
     * @param dst Pointer to interleaved destination PCM samples.
     * @param dst_channels Channel count of destination.
     */
    static void Convert(const float* src, uint32_t src_channels, uint32_t frames, float* dst, uint32_t dst_channels) noexcept {
        if (!src || !dst || frames == 0 || src_channels == 0 || dst_channels == 0) return;

        if (src_channels == dst_channels) {
            std::memcpy(dst, src, static_cast<size_t>(frames) * src_channels * sizeof(float));
            return;
        }

        for (uint32_t f = 0; f < frames; ++f) {
            const float* in = src + (f * src_channels);
            float* out = dst + (f * dst_channels);

            if (dst_channels == 1) {
                // To Mono
                if (src_channels == 2) {
                    out[0] = std::clamp(0.5f * (in[0] + in[1]), -1.0f, 1.0f);
                } else if (src_channels == 6) {
                    // 5.1 -> Mono: M = 0.7071*L + 0.7071*R + 1.0*C + 0.5*Ls + 0.5*Rs
                    float sum = 0.7071f * in[0] + 0.7071f * in[1] + in[2] + 0.5f * in[4] + 0.5f * in[5];
                    out[0] = std::clamp(sum * 0.5f, -1.0f, 1.0f);
                } else if (src_channels == 8) {
                    // 7.1 -> Mono
                    float sum = 0.7071f * in[0] + 0.7071f * in[1] + in[2] + 0.35f * (in[4] + in[5] + in[6] + in[7]);
                    out[0] = std::clamp(sum * 0.5f, -1.0f, 1.0f);
                } else {
                    out[0] = in[0];
                }
            } else if (dst_channels == 2) {
                // To Stereo
                if (src_channels == 1) {
                    // Mono -> Stereo
                    out[0] = in[0];
                    out[1] = in[0];
                } else if (src_channels == 6) {
                    // ITU-R BS.775 5.1 -> Stereo:
                    // L' = L + 0.7071*C + 0.7071*Ls
                    // R' = R + 0.7071*C + 0.7071*Rs
                    // Standard WAVE: 0:L, 1:R, 2:C, 3:LFE, 4:Ls, 5:Rs
                    float c = in[2];
                    float ls = in[4];
                    float rs = in[5];
                    out[0] = std::clamp(in[0] + kSqrt2Inv * c + kSqrt2Inv * ls, -1.0f, 1.0f);
                    out[1] = std::clamp(in[1] + kSqrt2Inv * c + kSqrt2Inv * rs, -1.0f, 1.0f);
                } else if (src_channels == 8) {
                    // ITU-R BS.775 7.1 -> Stereo:
                    // Standard WAVE: 0:L, 1:R, 2:C, 3:LFE, 4:Lss, 5:Rss, 6:Lsr, 7:Rsr
                    float c = in[2];
                    float l_surr = in[4] + in[6];
                    float r_surr = in[5] + in[7];
                    out[0] = std::clamp(in[0] + kSqrt2Inv * c + 0.5f * l_surr, -1.0f, 1.0f);
                    out[1] = std::clamp(in[1] + kSqrt2Inv * c + 0.5f * r_surr, -1.0f, 1.0f);
                } else {
                    out[0] = in[0];
                    out[1] = in[1];
                }
            } else if (dst_channels == 6) {
                // To 5.1 (0:L, 1:R, 2:C, 3:LFE, 4:Ls, 5:Rs)
                if (src_channels == 1) {
                    out[0] = in[0] * kSqrt2Inv;
                    out[1] = in[0] * kSqrt2Inv;
                    out[2] = in[0];
                    out[3] = 0.0f;
                    out[4] = in[0] * 0.5f;
                    out[5] = in[0] * 0.5f;
                } else if (src_channels == 2) {
                    out[0] = in[0];
                    out[1] = in[1];
                    out[2] = 0.5f * (in[0] + in[1]); // Phantom centre
                    out[3] = 0.0f;
                    out[4] = in[0] * 0.5f;
                    out[5] = in[1] * 0.5f;
                } else {
                    for (uint32_t ch = 0; ch < 6; ++ch) {
                        out[ch] = (ch < src_channels) ? in[ch] : 0.0f;
                    }
                }
            } else if (dst_channels == 8) {
                // To 7.1 (0:L, 1:R, 2:C, 3:LFE, 4:Lss, 5:Rss, 6:Lsr, 7:Rsr)
                if (src_channels == 1) {
                    out[0] = in[0] * kSqrt2Inv;
                    out[1] = in[0] * kSqrt2Inv;
                    out[2] = in[0];
                    out[3] = 0.0f;
                    out[4] = in[0] * 0.35f;
                    out[5] = in[0] * 0.35f;
                    out[6] = in[0] * 0.35f;
                    out[7] = in[0] * 0.35f;
                } else if (src_channels == 2) {
                    out[0] = in[0];
                    out[1] = in[1];
                    out[2] = 0.5f * (in[0] + in[1]);
                    out[3] = 0.0f;
                    out[4] = in[0] * 0.5f;
                    out[5] = in[1] * 0.5f;
                    out[6] = in[0] * 0.35f;
                    out[7] = in[1] * 0.35f;
                } else if (src_channels == 6) {
                    out[0] = in[0];
                    out[1] = in[1];
                    out[2] = in[2];
                    out[3] = in[3];
                    out[4] = in[4];
                    out[5] = in[5];
                    out[6] = in[4] * 0.7071f;
                    out[7] = in[5] * 0.7071f;
                } else {
                    for (uint32_t ch = 0; ch < 8; ++ch) {
                        out[ch] = (ch < src_channels) ? in[ch] : 0.0f;
                    }
                }
            } else {
                for (uint32_t ch = 0; ch < dst_channels; ++ch) {
                    out[ch] = (ch < src_channels) ? in[ch] : 0.0f;
                }
            }
        }
    }
};

} // namespace moonshine::audio
