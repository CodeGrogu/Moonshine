#include "moonshine/audio/opus_audio_encoder.hpp"
#include "moonshine/audio/opus_audio_decoder.hpp"
#include <iostream>
#include <vector>
#include <cassert>
#include <cmath>

using namespace moonshine::audio;

int main() {
    std::cout << "[+] Starting Moonshine Opus Audio Decoder Native Tests..." << std::endl;

    // 1. Stereo Encode and Decode Roundtrip
    {
        OpusEncoderConfig enc_cfg{};
        enc_cfg.sample_rate = 48000;
        enc_cfg.channels = 2;
        enc_cfg.bitrate = 160000;
        enc_cfg.frame_duration_ms = 5;

        OpusAudioEncoder encoder(enc_cfg);
        assert(encoder.is_initialized());

        OpusAudioDecoder decoder(48000, 2);
        assert(decoder.is_initialized());

        // Generate synthetic stereo audio
        std::vector<float> pcm_in(480); // 240 samples * 2 channels
        for (size_t i = 0; i < pcm_in.size(); ++i) {
            pcm_in[i] = std::sin(2.0f * 3.14159f * 440.0f * (static_cast<float>(i) / 48000.0f));
        }

        std::vector<uint8_t> opus_packet(1024);
        uint32_t payload_bytes = 0;
        bool enc_res = encoder.encode_float(pcm_in.data(), 240, opus_packet.data(), (uint32_t)opus_packet.size(), payload_bytes);
        (void)enc_res;
        assert(enc_res);
        assert(payload_bytes > 0);

        std::vector<float> pcm_out(480);
        uint32_t samples_decoded = 0;
        bool dec_res = decoder.decode_float(opus_packet.data(), payload_bytes, pcm_out.data(), (uint32_t)pcm_out.size(), samples_decoded, 0);
        (void)dec_res;
        assert(dec_res);
        assert(samples_decoded == 480);

        OpusDecoderMetrics metrics{};
        decoder.get_metrics(metrics);
        assert(metrics.total_frames_decoded == 1);
        assert(metrics.total_samples_decoded == 480);
        assert(metrics.decode_errors == 0);

        std::cout << "    [+] Stereo 48kHz Encode/Decode Roundtrip: " << samples_decoded << " samples, " << payload_bytes << " bytes" << std::endl;
    }

    // 2. Surround 5.1 Multi-Stream Decode Test
    {
        OpusEncoderConfig enc_cfg{};
        enc_cfg.sample_rate = 48000;
        enc_cfg.channels = 6;
        enc_cfg.bitrate = 256000;
        enc_cfg.frame_duration_ms = 5;

        OpusAudioEncoder encoder(enc_cfg);
        assert(encoder.is_initialized());

        OpusAudioDecoder decoder(48000, 6);
        assert(decoder.is_initialized());

        std::vector<float> pcm_in(1440); // 240 samples * 6 channels
        for (size_t i = 0; i < pcm_in.size(); ++i) {
            pcm_in[i] = 0.5f * std::sin(2.0f * 3.14159f * 220.0f * (static_cast<float>(i) / 48000.0f));
        }

        std::vector<uint8_t> opus_packet(2048);
        uint32_t payload_bytes = 0;
        bool enc_res = encoder.encode_float(pcm_in.data(), 240, opus_packet.data(), (uint32_t)opus_packet.size(), payload_bytes);
        (void)enc_res;
        assert(enc_res);

        std::vector<float> pcm_out(1440);
        uint32_t samples_decoded = 0;
        bool dec_res = decoder.decode_float(opus_packet.data(), payload_bytes, pcm_out.data(), (uint32_t)pcm_out.size(), samples_decoded, 0);
        (void)dec_res;
        assert(dec_res);
        assert(samples_decoded == 1440);

        std::cout << "    [+] Surround 5.1 Multi-Stream Decode: " << samples_decoded << " samples" << std::endl;
    }

    // 3. Surround 7.1 Multi-Stream Decode Test
    {
        OpusEncoderConfig enc_cfg{};
        enc_cfg.sample_rate = 48000;
        enc_cfg.channels = 8;
        enc_cfg.bitrate = 450000;
        enc_cfg.frame_duration_ms = 5;

        OpusAudioEncoder encoder(enc_cfg);
        assert(encoder.is_initialized());

        OpusAudioDecoder decoder(48000, 8);
        assert(decoder.is_initialized());

        std::vector<int16_t> pcm_in(1920); // 240 samples * 8 channels
        for (size_t i = 0; i < pcm_in.size(); ++i) {
            pcm_in[i] = static_cast<int16_t>(i % 1000);
        }

        std::vector<uint8_t> opus_packet(4096);
        uint32_t payload_bytes = 0;
        bool enc_res = encoder.encode_pcm16(pcm_in.data(), 240, opus_packet.data(), (uint32_t)opus_packet.size(), payload_bytes);
        (void)enc_res;
        assert(enc_res);

        std::vector<int16_t> pcm_out(1920);
        uint32_t samples_decoded = 0;
        bool dec_res = decoder.decode_pcm16(opus_packet.data(), payload_bytes, pcm_out.data(), (uint32_t)pcm_out.size(), samples_decoded, 0);
        (void)dec_res;
        assert(dec_res);
        assert(samples_decoded == 1920);

        std::cout << "    [+] Surround 7.1 Multi-Stream Decode: " << samples_decoded << " samples" << std::endl;
    }

    // 4. Packet Loss Concealment (PLC / Null packet)
    {
        OpusAudioDecoder decoder(48000, 2);
        assert(decoder.is_initialized());

        std::vector<float> pcm_out(480);
        uint32_t samples_decoded = 0;
        bool dec_res = decoder.decode_float(nullptr, 0, pcm_out.data(), (uint32_t)pcm_out.size(), samples_decoded, 1);
        (void)dec_res;
        assert(dec_res);
        assert(samples_decoded == 480);

        OpusDecoderMetrics metrics{};
        decoder.get_metrics(metrics);
        assert(metrics.concealment_frames == 1);

        std::cout << "    [+] Packet Loss Concealment (PLC): " << samples_decoded << " concealed samples emitted" << std::endl;
    }

    std::cout << "[+] All Opus Audio Decoder Native Tests Passed Successfully!" << std::endl;
    return 0;
}
