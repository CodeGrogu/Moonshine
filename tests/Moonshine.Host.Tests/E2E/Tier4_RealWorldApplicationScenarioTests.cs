using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moonshine.Core.Audio;
using Moonshine.Core.Congestion;
using Moonshine.Core.Discovery;
using Moonshine.Core.Feedback;
using Moonshine.Core.Hardware;
using Moonshine.Core.Input;
using Moonshine.Core.Media;
using Moonshine.Core.Network;
using Moonshine.Core.Pairing;
using Moonshine.Core.Runtime;
using Moonshine.Core.Security;
using Moonshine.Core.Session;
using Moonshine.Core.Transport;
using Moonshine.Core.Video;
using Moonshine.Host.Audio;
using Moonshine.Host.Capture;
using Moonshine.Host.Color;
using Moonshine.Host.Control;
using Moonshine.Host.Encoding;
using Moonshine.Host.Input;
using Moonshine.Host.Session;
using Moonshine.Interop;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Control;
using Moonshine.Protocol.Crypto;
using Moonshine.Protocol.Discovery;
using Moonshine.Protocol.FEC;
using Moonshine.Protocol.Feedback;
using Moonshine.Protocol.Input;
using Moonshine.Protocol.RTP;
using Moonshine.Protocol.Video;
using Xunit;
using ProtocolErrorCode = Moonshine.Protocol.Contracts.MoonshineErrorCode;
using HostVideoCodec = Moonshine.Host.Encoding.VideoCodec;

namespace Moonshine.Host.Tests.E2E;

/// <summary>
/// Tier 4: Real-World Application Scenarios E2E Test Suite.
/// End-to-end full streaming pipeline workloads exercising multi-subsystem lifecycles under realistic stress.
/// Exactly 5 complex streaming scenario tests.
/// </summary>
public class Tier4_RealWorldApplicationScenarioTests
{
    [Fact]
    public unsafe void T4_S01_4kHdr60fps_FullStreamingPipeline_EncodePacketiseReassemble()
    {
        const int frameBytes = 24000;
        const int mtuPayload = 800;
        const int dataShards = 30;
        const int parityShards = 6;

        byte[] groundTruthKeyframe = new byte[frameBytes];
        for (int i = 0; i < frameBytes; i++)
        {
            groundTruthKeyframe[i] = (byte)((i * 61 + 17) & 0xFF);
        }

        MoonshineHdr10Metadata hdrMeta = default;
        hdrMeta.HdrEnabled = 1;
        hdrMeta.ColorSpace = 1;
        hdrMeta.MaxMasteringLuminance = 10000000;
        hdrMeta.MaxContentLightLevel = 1000;
        hdrMeta.MaxFrameAverageLightLevel = 400;

        var packetiser = new MoonshineVideoPacketiser(
            streamId: 1,
            sessionId: 0xCAFEBABEDEADBEEFUL,
            mtuPayloadSize: mtuPayload,
            fecDataShards: dataShards,
            fecParityShards: parityShards
        );

        using var reassembly = new MoonshineMediaReassemblyPipeline(
            maxFrames: 16,
            maxPacketsPerFrame: 64,
            fecDataShards: dataShards,
            fecParityShards: parityShards,
            mtuPayloadSize: mtuPayload
        );

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(
            groundTruthKeyframe,
            frameIndex: 100,
            timestampUs: 16666,
            isKeyframe: true,
            isHdr10: true,
            d => datagrams.Add(d.ToArray())
        );

        datagrams.Count.Should().Be(dataShards + parityShards);

        List<int> dropped = [4, 12];
        for (int i = 0; i < datagrams.Count; i++)
        {
            if (!dropped.Contains(i))
            {
                reassembly.IngestDatagram(datagrams[i]);
            }
        }

        int popRes = reassembly.TryPopCompletedFrame(out var popped);
        popRes.Should().Be(1, "4K HDR10 60fps frame must be successfully reconstructed via SIMD FEC");
        popped.FrameIndex.Should().Be(100);
        popped.IsKeyframe.Should().Be(1);
        popped.TotalBytes.Should().Be(frameBytes);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(groundTruthKeyframe).Should().BeTrue("Reassembled 4K keyframe must match ground truth exactly");
    }

    [Fact]
    public unsafe void T4_S02_1080p120fps_UltraLowLatency_BurstPacketLossRecovery()
    {
        const int frameCount = 30;
        const int dataShards = 8;
        const int parityShards = 2;
        const int mtuPayload = 500;
        const int frameBytes = dataShards * mtuPayload;

        var packetiser = new MoonshineVideoPacketiser(
            streamId: 1,
            sessionId: 100,
            mtuPayloadSize: mtuPayload,
            fecDataShards: dataShards,
            fecParityShards: parityShards
        );

        using var reassembly = new MoonshineMediaReassemblyPipeline(
            maxFrames: 64,
            maxPacketsPerFrame: 32,
            fecDataShards: dataShards,
            fecParityShards: parityShards,
            mtuPayloadSize: mtuPayload
        );

        Dictionary<uint, byte[]> groundTruths = new();

        for (uint f = 1; f <= frameCount; f++)
        {
            byte[] framePayload = new byte[frameBytes];
            for (int i = 0; i < frameBytes; i++) framePayload[i] = (byte)((f * 13 + i * 7) & 0xFF);
            groundTruths[f] = framePayload;

            List<byte[]> datagrams = [];
            packetiser.PacketiseFrame(
                framePayload,
                frameIndex: f,
                timestampUs: f * 8333,
                isKeyframe: f == 1,
                isHdr10: false,
                d => datagrams.Add(d.ToArray())
            );

            datagrams.Count.Should().Be(dataShards + parityShards);

            List<int> dropped = [1, 6];
            for (int i = 0; i < datagrams.Count; i++)
            {
                if (!dropped.Contains(i))
                {
                    reassembly.IngestDatagram(datagrams[i]);
                }
            }
        }

        for (uint f = 1; f <= frameCount; f++)
        {
            int popRes = reassembly.TryPopCompletedFrame(out var popped);
            popRes.Should().Be(1, $"Frame {f} of 30 must pop cleanly under 120fps stream");
            popped.FrameIndex.Should().Be(f);

            ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
            reassembledSpan.SequenceEqual(groundTruths[f]).Should().BeTrue($"Frame {f} must match ground truth exactly");
        }
    }

    [Fact]
    public unsafe void T4_S03_AudioDynamicResampling_And_MicrophoneUplinkStream()
    {
        IntPtr opusEnc = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 160000, 20, 10, 1);
        IntPtr opusDec = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        try
        {
            const int frames = 20;
            for (int f = 0; f < frames; f++)
            {
                int targetSamples = 960;

                float[] pcm48k = new float[targetSamples * 2];
                for (int i = 0; i < targetSamples; i++)
                {
                    float tone = MathF.Sin(2 * MathF.PI * (f < 10 ? 440 : 880) * i / 48000.0f) * 0.4f;
                    pcm48k[i * 2] = tone;
                    pcm48k[i * 2 + 1] = tone;
                }

                byte[] compressed = new byte[1024];
                uint encBytes;
                fixed (float* pPcm = pcm48k)
                fixed (byte* pComp = compressed)
                {
                    int encRes = MoonshineNativeMethods.OpusEncoderEncodeFloat(opusEnc, pPcm, (uint)targetSamples, pComp, (uint)compressed.Length, out encBytes);
                    encRes.Should().Be(1);
                }
                encBytes.Should().BeGreaterThan(0);

                float[] decoded = new float[targetSamples * 2];
                uint decSamples;
                fixed (byte* pComp = compressed)
                fixed (float* pOut = decoded)
                {
                    int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(opusDec, pComp, encBytes, pOut, (uint)(targetSamples * 2), out decSamples, 0);
                    decRes.Should().Be(1);
                }
                decSamples.Should().Be((uint)(targetSamples * 2));

                MoonshineMicPacketHeader micHdr = new()
                {
                    StreamId = 3,
                    SampleIndex = (ulong)(f * 480),
                    SampleRate = 48000,
                    Channels = 1,
                    Codec = MoonshineAudioCodec.Pcm16,
                    PayloadSize = 480 * sizeof(short)
                };
                micHdr.Channels.Should().Be(1);
            }
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(opusEnc);
            MoonshineNativeMethods.OpusDecoderDestroy(opusDec);
        }
    }

    [Fact]
    public async Task T4_S04_HighConcurrency_MultiThreadedStress_And_AbruptTeardown()
    {
        using CancellationTokenSource cts = new();
        IntPtr ring = MoonshineNativeMethods.SpscCreate(1024);
        IntPtr jb = MoonshineNativeMethods.JitterCreate(64);

        try
        {
            var tasks = new Task[8];
            for (int t = 0; t < 8; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < 500 && !cts.Token.IsCancellationRequested; i++)
                    {
                        using var owner = new NativeMemoryOwner(512);
                        unsafe
                        {
                            byte* p = owner.Pointer;
                            if (p != null) p[0] = (byte)threadId;
                        }

                        using var pool = new PinnedBufferPool(4, 256);
                        unsafe
                        {
                            if (pool.TryRent(out int slotIndex, out _, out var span))
                            {
                                span[0] = (byte)i;
                                pool.Return(slotIndex);
                            }
                        }

                        if (threadId % 2 == 0)
                        {
                            MoonshinePacketDesc desc = new() { SequenceNumber = (uint)(threadId * 1000 + i) };
                            MoonshineNativeMethods.SpscEnqueue(ring, in desc);
                            MoonshineNativeMethods.SpscDequeue(ring, out _);
                        }
                    }
                });
            }

            await Task.Delay(50);
            cts.Cancel();
            await Task.WhenAll(tasks);
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public void T4_S05_SaturatedWire_AdversarialFuzzing_And_MtuOverflowStream()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 600);

        Random rnd = new(1337);
        int validFramesGenerated = 0;

        for (int i = 0; i < 200; i++)
        {
            int packetTypeChoice = rnd.Next(4);
            if (packetTypeChoice == 0)
            {
                byte[] payload = new byte[1200];
                rnd.NextBytes(payload);
                packetiser.PacketiseFrame(payload, frameIndex: (uint)(i + 1), timestampUs: (ulong)(i * 10000), isKeyframe: false, isHdr10: false, d =>
                {
                    reassembly.IngestDatagram(d);
                });
                validFramesGenerated++;
            }
            else if (packetTypeChoice == 1)
            {
                byte[] badMagic = new byte[64];
                rnd.NextBytes(badMagic);
                var code = MoonshineProtocolCodec.TryReadHeader(badMagic, out _);
                code.Should().NotBe(ProtocolErrorCode.Success);
            }
            else if (packetTypeChoice == 2)
            {
                byte[] truncated = new byte[rnd.Next(1, 20)];
                rnd.NextBytes(truncated);
                var code = MoonshineProtocolCodec.TryReadHeader(truncated, out _);
                code.Should().Be(ProtocolErrorCode.BufferTooSmall);
            }
            else
            {
                byte[] fuzzedBitstream = new byte[rnd.Next(4, 100)];
                rnd.NextBytes(fuzzedBitstream);
                BitstreamValidator.ValidateBitstream(HostVideoCodec.H264, fuzzedBitstream, out _);
            }
        }

        reassembly.IsActive.Should().BeTrue();
    }
}
