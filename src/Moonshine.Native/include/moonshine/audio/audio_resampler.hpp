#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <cmath>
#include <algorithm>
#include <cstring>
#include <numbers>

namespace moonshine::audio {

/**
 * @brief High-precision band-limited audio resampler with persistent fractional phase
 * and a persistent input FIFO queue.
 *
 * Prevents input sample dropping under buffer backpressure by only consuming source
 * frames from the FIFO when output frames are successfully generated.
 */
class AudioResampler {
public:
    static constexpr size_t kSincHalfTaps = 8;
    static constexpr size_t kSincTaps = kSincHalfTaps * 2; // 16-tap windowed sinc kernel

    AudioResampler(uint32_t src_rate = 48000, uint32_t dst_rate = 48000, uint32_t channels = 2)
        : src_rate_(src_rate == 0 ? 48000 : src_rate),
          dst_rate_(dst_rate == 0 ? 48000 : dst_rate),
          channels_(channels == 0 ? 2 : channels),
          resample_ratio_(static_cast<double>(src_rate_) / static_cast<double>(dst_rate_)),
          history_buffer_(kSincTaps * channels_, 0.0f) {
    }

    void Configure(uint32_t src_rate, uint32_t dst_rate, uint32_t channels) {
        src_rate_ = src_rate == 0 ? 48000 : src_rate;
        dst_rate_ = dst_rate == 0 ? 48000 : dst_rate;
        channels_ = channels == 0 ? 2 : channels;
        resample_ratio_ = static_cast<double>(src_rate_) / static_cast<double>(dst_rate_);
        Reset();
    }

    void Reset() noexcept {
        fifo_buffer_.clear();
        history_buffer_.assign(kSincTaps * channels_, 0.0f);
        phase_ = 0.0;
        history_frames_available_ = 0;
    }

    /**
     * @brief Pushes new interleaved PCM frames into the persistent input FIFO.
     */
    void PushInput(const float* pcm_data, size_t frame_count) {
        if (!pcm_data || frame_count == 0) return;
        size_t sample_count = frame_count * channels_;
        size_t old_size = fifo_buffer_.size();
        fifo_buffer_.resize(old_size + sample_count);
        std::memcpy(fifo_buffer_.data() + old_size, pcm_data, sample_count * sizeof(float));
    }

    /**
     * @brief Number of input frames currently queued in the FIFO.
     */
    [[nodiscard]] size_t QueuedInputFrames() const noexcept {
        return fifo_buffer_.size() / channels_;
    }

    /**
     * @brief Returns estimated number of output frames available given queued input frames.
     */
    [[nodiscard]] size_t AvailableOutputFrames() const noexcept {
        if (src_rate_ == dst_rate_) {
            return QueuedInputFrames();
        }
        double total_src = static_cast<double>(QueuedInputFrames()) - phase_;
        if (total_src <= 0.0) return 0;
        return static_cast<size_t>(std::floor(total_src / resample_ratio_));
    }

    /**
     * @brief Resamples audio from the FIFO into dst_buffer up to max_dst_frames.
     * Consumes ONLY the source frames necessary to produce the generated output frames.
     * @return Number of output frames generated.
     */
    size_t Resample(float* dst_buffer, size_t max_dst_frames) {
        if (!dst_buffer || max_dst_frames == 0) return 0;

        // 1:1 Pass-through fast path
        if (src_rate_ == dst_rate_) {
            size_t available_frames = QueuedInputFrames();
            size_t frames_to_copy = (std::min)(max_dst_frames, available_frames);
            if (frames_to_copy == 0) return 0;

            size_t samples_to_copy = frames_to_copy * channels_;
            std::memcpy(dst_buffer, fifo_buffer_.data(), samples_to_copy * sizeof(float));

            // Drain consumed frames
            fifo_buffer_.erase(fifo_buffer_.begin(), fifo_buffer_.begin() + static_cast<ptrdiff_t>(samples_to_copy));
            return frames_to_copy;
        }

        size_t total_input_frames = QueuedInputFrames();
        if (total_input_frames == 0) return 0;

        size_t frames_generated = 0;
        double cutoff = (std::min)(1.0, static_cast<double>(dst_rate_) / static_cast<double>(src_rate_));

        while (frames_generated < max_dst_frames) {
            double src_pos = phase_;
            auto src_int = static_cast<ptrdiff_t>(std::floor(src_pos));
            double frac = src_pos - static_cast<double>(src_int);

            // Check if we have sufficient future source frames in the FIFO
            if (static_cast<size_t>(src_int + static_cast<ptrdiff_t>(kSincHalfTaps)) >= total_input_frames) {
                break; // Await more input samples in FIFO
            }

            // Generate one output frame across all channels
            for (uint32_t ch = 0; ch < channels_; ++ch) {
                float sum = 0.0f;
                float weight_sum = 0.0f;

                for (ptrdiff_t tap = -static_cast<ptrdiff_t>(kSincHalfTaps) + 1; tap <= static_cast<ptrdiff_t>(kSincHalfTaps); ++tap) {
                    ptrdiff_t sample_idx = src_int + tap;
                    float s = 0.0f;

                    if (sample_idx >= 0 && static_cast<size_t>(sample_idx) < total_input_frames) {
                        s = fifo_buffer_[static_cast<size_t>(sample_idx) * channels_ + ch];
                    } else if (sample_idx < 0) {
                        // Sample from history buffer
                        ptrdiff_t hist_idx = static_cast<ptrdiff_t>(kSincTaps) + sample_idx;
                        if (hist_idx >= 0 && static_cast<size_t>(hist_idx) < kSincTaps) {
                            s = history_buffer_[static_cast<size_t>(hist_idx) * channels_ + ch];
                        }
                    }

                    double t = (static_cast<double>(tap) - frac);
                    double sinc_val = Sinc(t * cutoff);
                    double win = BlackmanHarrisWindow(static_cast<double>(tap + static_cast<ptrdiff_t>(kSincHalfTaps)) / static_cast<double>(kSincTaps));
                    double weight = sinc_val * win;

                    sum += static_cast<float>(s * weight);
                    weight_sum += static_cast<float>(weight);
                }

                float out_val = (std::abs(weight_sum) > 1e-6f) ? (sum / weight_sum) : sum;
                if (std::isnan(out_val) || std::isinf(out_val)) out_val = 0.0f;
                dst_buffer[frames_generated * channels_ + ch] = std::clamp(out_val, -1.0f, 1.0f);
            }

            frames_generated++;
            phase_ += resample_ratio_;
        }

        // Drain consumed full source frames and update history buffer
        auto consumed_frames = static_cast<size_t>(std::floor(phase_));
        if (consumed_frames > 0) {
            // Update history buffer before erasing
            size_t frames_to_hist = (std::min)(consumed_frames, kSincTaps);
            if (frames_to_hist > 0) {
                // Shift old history
                if (frames_to_hist < kSincTaps) {
                    std::memmove(
                        history_buffer_.data(),
                        history_buffer_.data() + (frames_to_hist * channels_),
                        (kSincTaps - frames_to_hist) * channels_ * sizeof(float)
                    );
                }
                // Copy newest consumed frames into end of history
                size_t src_start_frame = consumed_frames - frames_to_hist;
                std::memcpy(
                    history_buffer_.data() + ((kSincTaps - frames_to_hist) * channels_),
                    fifo_buffer_.data() + (src_start_frame * channels_),
                    frames_to_hist * channels_ * sizeof(float)
                );
            }

            size_t samples_to_erase = (std::min)(consumed_frames * channels_, fifo_buffer_.size());
            fifo_buffer_.erase(fifo_buffer_.begin(), fifo_buffer_.begin() + static_cast<ptrdiff_t>(samples_to_erase));
            phase_ -= static_cast<double>(consumed_frames);
            if (phase_ < 0.0 || std::isnan(phase_) || std::isinf(phase_)) {
                phase_ = 0.0;
            }
        }

        return frames_generated;
    }

private:
    static double Sinc(double x) noexcept {
        if (std::abs(x) < 1e-9) return 1.0;
        double pix = std::numbers::pi * x;
        return std::sin(pix) / pix;
    }

    static double BlackmanHarrisWindow(double normalized_pos) noexcept {
        if (normalized_pos < 0.0 || normalized_pos > 1.0) return 0.0;
        constexpr double a0 = 0.35875;
        constexpr double a1 = 0.48829;
        constexpr double a2 = 0.14128;
        constexpr double a3 = 0.01168;
        double theta = 2.0 * std::numbers::pi * normalized_pos;
        return a0 - a1 * std::cos(theta) + a2 * std::cos(2.0 * theta) - a3 * std::cos(3.0 * theta);
    }

    uint32_t src_rate_{48000};
    uint32_t dst_rate_{48000};
    uint32_t channels_{2};
    double resample_ratio_{1.0};
    double phase_{0.0};
    std::vector<float> fifo_buffer_;
    std::vector<float> history_buffer_;
    size_t history_frames_available_{0};
};

} // namespace moonshine::audio
