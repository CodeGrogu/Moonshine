using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class OpusNativeTests
{
    [Theory]
    [InlineData(2u, 160000u)] // Stereo
    [InlineData(6u, 256000u)] // Surround 5.1
    [InlineData(8u, 450000u)] // Surround 7.1
    public unsafe void OpusEncoder_MultiChannel_EncodesValidPayload(uint channels, uint bitrate)
    {
        IntPtr handle = MoonshineNativeMethods.OpusEncoderCreate(
            sampleRate: 48000,
            channels: channels,
            bitrate: bitrate,
            frameDurationMs: 5,
            complexity: 8,
            useVbr: 1
        );
        handle.Should().NotBe(IntPtr.Zero);

        int sampleCount = (int)(240 * channels);
        float[] pcm = new float[sampleCount];
        Array.Fill(pcm, 0.25f);

        byte[] payload = new byte[2048];

        fixed (float* pcmPtr = pcm)
        fixed (byte* outPtr = payload)
        {
            int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(
                handle,
                pcmPtr,
                240,
                outPtr,
                (uint)payload.Length,
                out uint written
            );

            res.Should().Be(1);
            written.Should().BeGreaterThan(0);
        }

        MoonshineNativeMethods.OpusEncoderGetMetrics(
            handle,
            out ulong frames,
            out ulong bytes,
            out double avgUs,
            out uint curBitrate,
            out uint streams
        );

        frames.Should().Be(1);
        bytes.Should().BeGreaterThan(0);
        curBitrate.Should().Be(bitrate);
        streams.Should().BeGreaterThan(0);

        MoonshineNativeMethods.OpusEncoderDestroy(handle);
    }

    [Fact]
    public unsafe void OpusEncoder_Pcm16AndDynamicBitrate_ExecutesCleanly()
    {
        IntPtr handle = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 128000, 10, 8, 1);
        handle.Should().NotBe(IntPtr.Zero);

        short[] pcm16 = new short[960]; // 480 * 2
        byte[] payload = new byte[1024];

        fixed (short* pcmPtr = pcm16)
        fixed (byte* outPtr = payload)
        {
            int res = MoonshineNativeMethods.OpusEncoderEncodePcm16(
                handle,
                pcmPtr,
                480,
                outPtr,
                (uint)payload.Length,
                out uint written
            );

            res.Should().Be(1);
            written.Should().BeGreaterThan(0);
        }

        int setBitrateRes = MoonshineNativeMethods.OpusEncoderSetBitrate(handle, 192000);
        setBitrateRes.Should().Be(1);

        int setComplexityRes = MoonshineNativeMethods.OpusEncoderSetComplexity(handle, 10);
        setComplexityRes.Should().Be(1);

        MoonshineNativeMethods.OpusEncoderDestroy(handle);
    }
}
