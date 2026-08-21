using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class MicSinkNativeTests
{
    [Fact]
    public unsafe void MicSink_PushAndPullPcm_ExecutesSuccessfully()
    {
        IntPtr handle = MoonshineNativeMethods.MicSinkCreate(
            sampleRate: 48000,
            channels: 1,
            targetLatencyMs: 10,
            gainMultiplier: 1.0f,
            noiseGateThresholdDb: -50.0f,
            isMuted: 0
        );
        handle.Should().NotBe(IntPtr.Zero);

        byte[] dummyOpus = new byte[64];
        Array.Fill(dummyOpus, (byte)150);

        fixed (byte* pPayload = dummyOpus)
        {
            int pushRes = MoonshineNativeMethods.MicSinkPushOpusPacket(
                handle,
                pPayload,
                (uint)dummyOpus.Length,
                timestamp: 480,
                sequenceNumber: 1
            );
            pushRes.Should().Be(1);
        }

        float[] pcmOut = new float[480];
        fixed (float* pPcm = pcmOut)
        {
            int pullRes = MoonshineNativeMethods.MicSinkPullPcm(
                handle,
                pPcm,
                (uint)pcmOut.Length,
                out uint samplesRead
            );
            pullRes.Should().Be(1);
            samplesRead.Should().Be(480);
        }

        MoonshineNativeMethods.MicSinkGetMetrics(
            handle,
            out ulong packetsReceived,
            out ulong samplesRendered,
            out uint lossCount,
            out uint driftCorrections,
            out double jitterMs
        );

        packetsReceived.Should().Be(1);
        samplesRendered.Should().Be(480);
        lossCount.Should().Be(0);

        MoonshineNativeMethods.MicSinkDestroy(handle);
    }

    [Fact]
    public unsafe void MicSink_GainAndMute_ModifiesOutput()
    {
        IntPtr handle = MoonshineNativeMethods.MicSinkCreate(48000, 1, 10, 1.5f, -80.0f, 0);
        handle.Should().NotBe(IntPtr.Zero);

        MoonshineNativeMethods.MicSinkSetGain(handle, 2.0f);
        MoonshineNativeMethods.MicSinkSetMute(handle, 1);

        byte[] dummyOpus = new byte[64];
        Array.Fill(dummyOpus, (byte)200);

        fixed (byte* pPayload = dummyOpus)
        {
            int pushRes = MoonshineNativeMethods.MicSinkPushOpusPacket(handle, pPayload, (uint)dummyOpus.Length, 480, 2);
            pushRes.Should().Be(1);
        }

        float[] pcmOut = new float[480];
        fixed (float* pPcm = pcmOut)
        {
            int pullRes = MoonshineNativeMethods.MicSinkPullPcm(handle, pPcm, (uint)pcmOut.Length, out uint read);
            pullRes.Should().Be(1);
            read.Should().Be(480);
        }

        // Must be silence when muted
        pcmOut.Should().OnlyContain(s => s == 0.0f);

        MoonshineNativeMethods.MicSinkDestroy(handle);
    }
}
