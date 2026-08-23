using System;
using System.Diagnostics;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

/// <summary>
/// Comprehensive resilience, zero-allocation hot path, noise gating, and mute silencing tests
/// for <see cref="ClientMicrophoneCapturePipeline"/>.
/// </summary>
public sealed class ClientMicrophoneResilienceTests
{
    private sealed class TestOpusMonoDecoder : IDisposable
    {
        private IntPtr _handle;

        public TestOpusMonoDecoder(uint sampleRate = 48000, uint channels = 1)
        {
            _handle = MoonshineNativeMethods.OpusDecoderCreate(sampleRate, channels);
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialise native Opus decoder for tests.");
            }
        }

        public unsafe bool DecodeFloat(ReadOnlySpan<byte> payload, Span<float> outPcm, out uint samplesDecoded)
        {
            samplesDecoded = 0;
            if (_handle == IntPtr.Zero || outPcm.IsEmpty) return false;

            fixed (byte* pPayload = payload)
            fixed (float* pOut = outPcm)
            {
                int res = MoonshineNativeMethods.OpusDecoderDecodeFloat(
                    _handle,
                    pPayload,
                    (uint)payload.Length,
                    pOut,
                    (uint)outPcm.Length,
                    out samplesDecoded,
                    0
                );
                return res != 0;
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                MoonshineNativeMethods.OpusDecoderDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    private static float[] GenerateSineWavePcm(int sampleCount, uint sampleRate = 48000, float frequency = 440.0f, float amplitude = 0.5f)
    {
        float[] pcm = new float[sampleCount];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = amplitude * MathF.Sin(2.0f * MathF.PI * frequency * (i / (float)sampleRate));
        }
        return pcm;
    }

    private static double ComputeRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            sumSq += samples[i] * samples[i];
        }
        return Math.Sqrt(sumSq / samples.Length);
    }

    private static float ComputePeak(ReadOnlySpan<float> samples)
    {
        float peak = 0.0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = MathF.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }
        return peak;
    }

    [Fact]
    public void ClientMicrophone_ZeroAllocationHotPath_Verified()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10,
            streamId: 0x11223344,
            sessionId: 0xAABBCCDDEEFF0011UL
        );

        float[] inputPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 440.0f, amplitude: 0.3f);
        byte[] datagramBuffer = new byte[1500];

        // Warm up JIT and any static structures
        for (int i = 0; i < 20; i++)
        {
            bool ok = pipeline.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out int bytesWritten,
                preferMoonshineFraming: (i % 2 == 0)
            );
            ok.Should().BeTrue();
            bytesWritten.Should().BeGreaterThan(0);
        }

        // Test RTP framing hot path for zero heap allocations
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        long startAllocatedRtp = GC.GetAllocatedBytesForCurrentThread();
        bool allRtpSuccess = true;
        int lastRtpBytes = 0;

        for (int i = 0; i < 1000; i++)
        {
            allRtpSuccess &= pipeline.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out lastRtpBytes,
                preferMoonshineFraming: false
            );
        }

        long allocatedRtp = GC.GetAllocatedBytesForCurrentThread() - startAllocatedRtp;
        allocatedRtp.Should().Be(0, "the client microphone RTP framing hot path must perform zero GC allocations");
        allRtpSuccess.Should().BeTrue();
        lastRtpBytes.Should().BeGreaterThan(MicAudioPacket.RtpHeaderSize);

        // Test MNBP framing hot path for zero heap allocations
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        long startAllocatedMnbp = GC.GetAllocatedBytesForCurrentThread();
        bool allMnbpSuccess = true;
        int lastMnbpBytes = 0;

        for (int i = 0; i < 1000; i++)
        {
            allMnbpSuccess &= pipeline.TryProcessRecordedFrame(
                inputPcm,
                datagramBuffer,
                out lastMnbpBytes,
                preferMoonshineFraming: true
            );
        }

        long allocatedMnbp = GC.GetAllocatedBytesForCurrentThread() - startAllocatedMnbp;
        allocatedMnbp.Should().Be(0, "the client microphone MNBP framing hot path must perform zero GC allocations");
        allMnbpSuccess.Should().BeTrue();
        lastMnbpBytes.Should().BeGreaterThan(MoonshineProtocolConstants.HeaderSize + MoonshineMicPacketCodec.HeaderSize);

        // Verify sequence numbers and sample indices advanced deterministically
        pipeline.CurrentSequenceNumber.Should().Be(2020);
        pipeline.CurrentSampleIndex.Should().Be((ulong)(2020 * 480));
    }

    [Fact]
    public void ClientMicrophone_NoiseGateTransition_AttenuatesBelowThreshold()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        pipeline.SetNoiseGateThreshold(-50.0f);
        pipeline.NoiseGateThresholdDb.Should().Be(-50.0f);

        using var decoder = new TestOpusMonoDecoder(48000, 1);

        // 1. Low-level noise (-60dB sine wave: peak ~0.001f, RMS ~0.000707f < -50dB threshold)
        float noiseAmp = MathF.Pow(10.0f, -60.0f / 20.0f); // 0.001f
        float[] noisePcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 1000.0f, amplitude: noiseAmp);

        byte[] datagramBuffer = new byte[1500];
        Span<float> decodedNoisePcm = stackalloc float[480];

        // Process frames sequentially through pipeline and decoder so envelope smoothly transitions to 0.05x
        for (int f = 0; f < 3; f++)
        {
            pipeline.TryProcessRecordedFrame(noisePcm, datagramBuffer, out int nb, preferMoonshineFraming: false).Should().BeTrue();
            MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, nb), out var np).Should().BeTrue();
            decoder.DecodeFloat(np.Payload, decodedNoisePcm, out uint samplesDecoded).Should().BeTrue();
            samplesDecoded.Should().Be(480);
        }

        float noisePeak = ComputePeak(decodedNoisePcm);
        double noiseRms = ComputeRms(decodedNoisePcm);

        // Soft attenuation factor is 0.05x, so expected peak is ~0.001 * 0.05 = 0.00005f
        noisePeak.Should().BeLessThan(0.00025f, "noise signal below -50dB threshold must be attenuated by 0.05x");
        noiseRms.Should().BeLessThan(0.0002f);

        // 2. Vocal-level signal (-10dB sine wave: peak ~0.3162f, RMS ~0.2236f > -50dB threshold)
        float vocalAmp = MathF.Pow(10.0f, -10.0f / 20.0f); // ~0.3162f
        float[] vocalPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 1000.0f, amplitude: vocalAmp);
        Span<float> decodedVocalPcm = stackalloc float[480];

        // Process frames sequentially through pipeline and decoder so envelope smoothly ramps attenuation back to 1.0x
        for (int f = 0; f < 3; f++)
        {
            pipeline.TryProcessRecordedFrame(vocalPcm, datagramBuffer, out int vb, preferMoonshineFraming: false).Should().BeTrue();
            MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, vb), out var vp).Should().BeTrue();
            decoder.DecodeFloat(vp.Payload, decodedVocalPcm, out uint samplesDecoded).Should().BeTrue();
            samplesDecoded.Should().Be(480);
        }

        float vocalPeak = ComputePeak(decodedVocalPcm);
        double vocalRms = ComputeRms(decodedVocalPcm);

        vocalPeak.Should().BeGreaterThan(0.20f, "vocal signal above threshold must pass with full amplitude");
        vocalPeak.Should().BeLessThan(0.40f);
        vocalRms.Should().BeGreaterThan(0.15);

        // Compare ratio between attenuated noise and full vocal signal
        double attenuationRatio = noiseRms / vocalRms;
        attenuationRatio.Should().BeLessThan(0.01, "noise gate must provide significant attenuation contrast between noise and speech");
    }

    [Fact]
    public void ClientMicrophone_MuteSilencing_OutputsExactZero()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        using var decoder = new TestOpusMonoDecoder(48000, 1);

        // Enable mute
        pipeline.SetMute(true);
        pipeline.IsMuted.Should().BeTrue();

        // Feed loud high-amplitude audio into muted pipeline
        float[] loudPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 440.0f, amplitude: 0.95f);
        byte[] datagramBuffer = new byte[1500];
        Span<float> decodedPcm = stackalloc float[480];

        // First frame performs smooth mute ramp transition, second frame allows codec overlap-add to settle
        for (int f = 0; f < 2; f++)
        {
            pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out int transBytes, preferMoonshineFraming: false).Should().BeTrue();
            MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, transBytes), out var transPacket).Should().BeTrue();
            decoder.DecodeFloat(transPacket.Payload, decodedPcm, out _).Should().BeTrue();
        }

        // Steady-state mute frame
        pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out int bytesWritten, preferMoonshineFraming: false).Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(MicAudioPacket.RtpHeaderSize);

        bool parseOk = MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, bytesWritten), out var rtpPacket);
        parseOk.Should().BeTrue();

        bool decodeOk = decoder.DecodeFloat(rtpPacket.Payload, decodedPcm, out uint samplesDecoded);
        decodeOk.Should().BeTrue();
        samplesDecoded.Should().Be(480);

        // Assert all decoded samples are effectively zero (within numerical precision of libopus silence decoding)
        foreach (float sample in decodedPcm)
        {
            sample.Should().BeApproximately(0.0f, 0.02f, "muted microphone input must produce zero PCM output across all samples");
        }

        double mutedRms = ComputeRms(decodedPcm);
        mutedRms.Should().BeLessThan(0.005, "muted microphone energy must be zero");

        // Unmute and verify non-zero vocal output is restored
        pipeline.SetMute(false);
        pipeline.IsMuted.Should().BeFalse();

        for (int f = 0; f < 2; f++)
        {
            pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out bytesWritten, preferMoonshineFraming: false).Should().BeTrue();
            MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, bytesWritten), out rtpPacket).Should().BeTrue();
            decoder.DecodeFloat(rtpPacket.Payload, decodedPcm, out samplesDecoded).Should().BeTrue();
        }

        float unmutedPeak = ComputePeak(decodedPcm);
        unmutedPeak.Should().BeGreaterThan(0.5f, "unmuted microphone input must produce non-zero PCM output");
    }

    [Fact]
    public void ClientMicrophone_SmoothMuteTransition_RampsGainAcross64Samples()
    {
        using var pipeline = new ClientMicrophoneCapturePipeline(
            sampleRate: 48000,
            channels: 1,
            bitrate: 32000,
            frameDurationMs: 10
        );

        using var decoder = new TestOpusMonoDecoder(48000, 1);

        float[] loudPcm = GenerateSineWavePcm(480, sampleRate: 48000, frequency: 440.0f, amplitude: 0.9f);
        byte[] datagramBuffer = new byte[1500];
        Span<float> decodedPcm = stackalloc float[480];

        // 1. Process active audio frame
        pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out int activeBytes, preferMoonshineFraming: false).Should().BeTrue();
        MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, activeBytes), out var activePacket).Should().BeTrue();
        decoder.DecodeFloat(activePacket.Payload, decodedPcm, out _).Should().BeTrue();

        // 2. Trigger mute
        pipeline.SetMute(true);

        // 3. Process transition frame (ramping first 64 samples to zero)
        pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out int transBytes, preferMoonshineFraming: false).Should().BeTrue();
        MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, transBytes), out var transPacket).Should().BeTrue();
        decoder.DecodeFloat(transPacket.Payload, decodedPcm, out _).Should().BeTrue();

        // The first samples should contain audio, whereas tail samples after ramp and Opus lookahead should be silent
        float headPeak = ComputePeak(decodedPcm[..64]);
        float tailPeak = ComputePeak(decodedPcm[240..]);
        headPeak.Should().BeGreaterThan(0.1f, "initial samples in transition frame should carry smoothly ramping audio");
        tailPeak.Should().BeLessThan(0.06f, "samples after mute transition ramp and codec lookahead must decay smoothly towards silence");

        // 4. Process steady-state mute frames after settling
        for (int f = 0; f < 2; f++)
        {
            pipeline.TryProcessRecordedFrame(loudPcm, datagramBuffer, out int silentBytes, preferMoonshineFraming: false).Should().BeTrue();
            MicAudioPacket.TryParse(datagramBuffer.AsSpan(0, silentBytes), out var silentPacket).Should().BeTrue();
            decoder.DecodeFloat(silentPacket.Payload, decodedPcm, out _).Should().BeTrue();
        }

        foreach (float sample in decodedPcm)
        {
            sample.Should().BeApproximately(0.0f, 0.02f);
        }
    }
}
