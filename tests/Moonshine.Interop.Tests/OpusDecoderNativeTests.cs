using System.Runtime.InteropServices;
using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public sealed class OpusDecoderNativeTests
{
    [Fact]
    public unsafe void OpusDecoder_Stereo_DecodeFloatAndPcm16_Succeeds()
    {
        IntPtr encHandle = MoonshineNativeMethods.OpusEncoderCreate(
            sampleRate: 48000,
            channels: 2,
            bitrate: 160000,
            frameDurationMs: 5,
            complexity: 8,
            useVbr: 1
        );
        encHandle.Should().NotBe(IntPtr.Zero);

        IntPtr decHandle = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        decHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            // Encode stereo frame
            float[] pcmIn = new float[480];
            for (int i = 0; i < pcmIn.Length; i++)
            {
                pcmIn[i] = 0.5f * MathF.Sin(2.0f * MathF.PI * 440.0f * (i / 48000.0f));
            }

            byte[] opusPacket = new byte[1024];
            uint encodedBytes = 0;
            fixed (float* inPtr = pcmIn)
            fixed (byte* pktPtr = opusPacket)
            {
                int encRes = MoonshineNativeMethods.OpusEncoderEncodeFloat(
                    encHandle,
                    inPtr,
                    240,
                    pktPtr,
                    (uint)opusPacket.Length,
                    out encodedBytes
                );
                encRes.Should().Be(1);
                encodedBytes.Should().BeGreaterThan(0);
            }

            // Decode to Float32 PCM
            float[] pcmOutFloat = new float[480];
            uint samplesDecodedFloat = 0;
            fixed (byte* pktPtr = opusPacket)
            fixed (float* outPtr = pcmOutFloat)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(
                    decHandle,
                    pktPtr,
                    encodedBytes,
                    outPtr,
                    (uint)pcmOutFloat.Length,
                    out samplesDecodedFloat,
                    0
                );
                decRes.Should().Be(1);
                samplesDecodedFloat.Should().Be(480);
            }

            // Decode to Int16 PCM
            short[] pcmOut16 = new short[480];
            uint samplesDecoded16 = 0;
            fixed (byte* pktPtr = opusPacket)
            fixed (short* outPtr = pcmOut16)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodePcm16(
                    decHandle,
                    pktPtr,
                    encodedBytes,
                    outPtr,
                    (uint)pcmOut16.Length,
                    out samplesDecoded16,
                    0
                );
                decRes.Should().Be(1);
                samplesDecoded16.Should().Be(480);
            }

            // Verify metrics
            MoonshineNativeMethods.OpusDecoderGetMetrics(
                decHandle,
                out ulong framesDecoded,
                out ulong totalSamples,
                out uint decodeErrors,
                out uint concealmentFrames,
                out double avgDecodeTimeUs,
                out uint streamsCount
            );

            framesDecoded.Should().Be(2);
            totalSamples.Should().Be(960);
            decodeErrors.Should().Be(0);
            concealmentFrames.Should().Be(0);
            streamsCount.Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encHandle);
            MoonshineNativeMethods.OpusDecoderDestroy(decHandle);
        }
    }

    [Fact]
    public unsafe void OpusDecoder_Surround51_MultiStreamDecode_Succeeds()
    {
        IntPtr decHandle = MoonshineNativeMethods.OpusDecoderCreate(48000, 6);
        decHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] pcmOut = new float[1440];
            uint samplesDecoded = 0;

            // Packet Loss Concealment on empty packet
            fixed (float* outPtr = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(
                    decHandle,
                    null,
                    0,
                    outPtr,
                    (uint)pcmOut.Length,
                    out samplesDecoded,
                    1
                );
                decRes.Should().Be(1);
                samplesDecoded.Should().Be(1440);
            }

            MoonshineNativeMethods.OpusDecoderGetMetrics(
                decHandle,
                out ulong framesDecoded,
                out ulong totalSamples,
                out uint decodeErrors,
                out uint concealmentFrames,
                out double _,
                out uint streamsCount
            );

            framesDecoded.Should().Be(1);
            totalSamples.Should().Be(1440);
            concealmentFrames.Should().Be(1);
            streamsCount.Should().Be(4);
        }
        finally
        {
            MoonshineNativeMethods.OpusDecoderDestroy(decHandle);
        }
    }

    [Fact]
    public unsafe void OpusDecoder_Surround71_MultiStreamDecode_Succeeds()
    {
        IntPtr decHandle = MoonshineNativeMethods.OpusDecoderCreate(48000, 8);
        decHandle.Should().NotBe(IntPtr.Zero);

        try
        {
            float[] pcmOut = new float[1920];
            uint samplesDecoded = 0;

            fixed (float* outPtr = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(
                    decHandle,
                    null,
                    0,
                    outPtr,
                    (uint)pcmOut.Length,
                    out samplesDecoded,
                    1
                );
                decRes.Should().Be(1);
                samplesDecoded.Should().Be(1920);
            }

            MoonshineNativeMethods.OpusDecoderGetMetrics(
                decHandle,
                out _,
                out _,
                out _,
                out _,
                out _,
                out uint streamsCount
            );

            streamsCount.Should().Be(6);
        }
        finally
        {
            MoonshineNativeMethods.OpusDecoderDestroy(decHandle);
        }
    }
}
