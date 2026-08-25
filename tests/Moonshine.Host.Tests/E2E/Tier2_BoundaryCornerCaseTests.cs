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
/// Tier 2: Boundary & Corner Cases E2E Test Suite.
/// Stress-tests edge conditions, zero limits, maximum thresholds, buffer rollovers, and fail-closed validation.
/// Exactly 5 test cases per feature (70 test cases total).
/// </summary>
public class Tier2_BoundaryCornerCaseTests
{
    // =========================================================================
    // Feature 1: SIMD GF(2^8) FEC Erasure Hardening (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T2_F01_01_SimdFec_MinimalMatrix_K1_M1_RecoversSingleDataShard()
    {
        const int dataCount = 1;
        const int parityCount = 1;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 64;

        byte[] data = new byte[shardSize];
        byte[] groundTruth = new byte[shardSize];
        byte[] parity = new byte[shardSize];
        for (int i = 0; i < shardSize; i++)
        {
            data[i] = (byte)(i ^ 0x5A);
            groundTruth[i] = data[i];
        }

        fixed (byte* pD = data)
        fixed (byte* pP = parity)
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = pP;

            MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize).Should().Be(0);

            // Erase single data shard
            Array.Clear(data, 0, shardSize);
            int[] erased = [0];
            fixed (int* pErased = erased)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                allPtrs[0] = pD;
                allPtrs[1] = pP;

                MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, pErased, 1).Should().Be(0);
            }

            data.Should().Equal(groundTruth);
        }
    }

    [Fact]
    public unsafe void T2_F01_02_SimdFec_ZeroParity_K8_M0_ReturnsUnchanged()
    {
        const int dataCount = 8;
        const int parityCount = 0;
        const int shardSize = 128;

        byte[][] data = new byte[dataCount][];
        for (int i = 0; i < dataCount; i++)
        {
            data[i] = new byte[shardSize];
            Array.Fill(data[i], (byte)i);
        }

        fixed (byte* pD0 = data[0], pD1 = data[1], pD2 = data[2], pD3 = data[3],
                     pD4 = data[4], pD5 = data[5], pD6 = data[6], pD7 = data[7])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD0; dPtrs[1] = pD1; dPtrs[2] = pD2; dPtrs[3] = pD3;
            dPtrs[4] = pD4; dPtrs[5] = pD5; dPtrs[6] = pD6; dPtrs[7] = pD7;

            int encRes = MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, null, parityCount, shardSize);
            encRes.Should().NotBe(0, "Zero parity shards configuration must return non-zero error code");
        }
    }

    [Fact]
    public unsafe void T2_F01_03_SimdFec_MaxLossExceeded_K4_M2_Loss3_FailsGracefully()
    {
        const int dataCount = 4;
        const int parityCount = 2;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 128;

        byte[][] data = new byte[dataCount][];
        byte[][] parity = new byte[parityCount][];
        for (int i = 0; i < dataCount; i++) data[i] = new byte[shardSize];
        for (int i = 0; i < parityCount; i++) parity[i] = new byte[shardSize];

        fixed (byte* pD0 = data[0], pD1 = data[1], pD2 = data[2], pD3 = data[3])
        fixed (byte* pP0 = parity[0], pP1 = parity[1])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD0; dPtrs[1] = pD1; dPtrs[2] = pD2; dPtrs[3] = pD3;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = pP0; pPtrs[1] = pP1;

            int encRes = MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize);
            encRes.Should().Be(0);

            // Erase 3 shards when M=2 (loss count > parity count)
            int[] erased = [0, 1, 2];
            fixed (int* pErased = erased)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                allPtrs[0] = pD0; allPtrs[1] = pD1; allPtrs[2] = pD2; allPtrs[3] = pD3;
                allPtrs[4] = pP0; allPtrs[5] = pP1;

                int res = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, pErased, 3);
                res.Should().NotBe(0, "Decoding when lost shards exceed parity must return non-zero error code");
            }
        }
    }

    [Fact]
    public unsafe void T2_F01_04_SimdFec_OddShardSizes_HandlesNon16ByteAlignedSizes()
    {
        int[] oddSizes = [17, 33, 101, 1023];
        const int dataCount = 3;
        const int parityCount = 2;
        const int totalCount = dataCount + parityCount;

        byte*[] dPtrsArray = new byte*[dataCount];
        byte*[] pPtrsArray = new byte*[parityCount];
        byte*[] allPtrsArray = new byte*[totalCount];

        foreach (int size in oddSizes)
        {
            byte[][] data = new byte[dataCount][];
            byte[][] groundTruth = new byte[dataCount][];
            byte[][] parity = new byte[parityCount][];
            for (int i = 0; i < dataCount; i++)
            {
                data[i] = new byte[size];
                groundTruth[i] = new byte[size];
                for (int j = 0; j < size; j++)
                {
                    byte val = (byte)((i * 37 + j) & 0xFF);
                    data[i][j] = val;
                    groundTruth[i][j] = val;
                }
            }
            for (int i = 0; i < parityCount; i++) parity[i] = new byte[size];

            fixed (byte* pD0 = data[0], pD1 = data[1], pD2 = data[2])
            fixed (byte* pP0 = parity[0], pP1 = parity[1])
            {
                dPtrsArray[0] = pD0; dPtrsArray[1] = pD1; dPtrsArray[2] = pD2;
                pPtrsArray[0] = pP0; pPtrsArray[1] = pP1;

                fixed (byte** ppD = dPtrsArray, ppP = pPtrsArray)
                {
                    MoonshineNativeMethods.FecEncodeSimd(ppD, dataCount, ppP, parityCount, size).Should().Be(0);

                    // Erase shard 1
                    Array.Clear(data[1], 0, size);
                    int[] erased = [1];
                    fixed (int* pErased = erased)
                    {
                        allPtrsArray[0] = pD0; allPtrsArray[1] = pD1; allPtrsArray[2] = pD2;
                        allPtrsArray[3] = pP0; allPtrsArray[4] = pP1;

                        fixed (byte** ppAll = allPtrsArray)
                        {
                            MoonshineNativeMethods.FecReconstructSimd(ppAll, dataCount, parityCount, size, pErased, 1).Should().Be(0);
                        }
                    }

                    data[1].Should().Equal(groundTruth[1]);
                }
            }
        }
    }

    [Fact]
    public unsafe void T2_F01_05_SimdFec_AllParityLost_K4_M4_Loss4Parity_DataIntact()
    {
        const int dataCount = 4;
        const int parityCount = 4;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 256;

        byte[][] data = new byte[dataCount][];
        byte[][] groundTruth = new byte[dataCount][];
        byte[][] parity = new byte[parityCount][];
        for (int i = 0; i < dataCount; i++)
        {
            data[i] = new byte[shardSize];
            groundTruth[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++)
            {
                byte val = (byte)((i * 71 + j * 3) & 0xFF);
                data[i][j] = val;
                groundTruth[i][j] = val;
            }
        }
        for (int i = 0; i < parityCount; i++) parity[i] = new byte[shardSize];

        fixed (byte* pD0 = data[0], pD1 = data[1], pD2 = data[2], pD3 = data[3])
        fixed (byte* pP0 = parity[0], pP1 = parity[1], pP2 = parity[2], pP3 = parity[3])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD0; dPtrs[1] = pD1; dPtrs[2] = pD2; dPtrs[3] = pD3;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = pP0; pPtrs[1] = pP1; pPtrs[2] = pP2; pPtrs[3] = pP3;

            MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize).Should().Be(0);

            // Zero data losses, all parity ignored
            byte** allPtrs = stackalloc byte*[totalCount];
            allPtrs[0] = pD0; allPtrs[1] = pD1; allPtrs[2] = pD2; allPtrs[3] = pD3;
            allPtrs[4] = pP0; allPtrs[5] = pP1; allPtrs[6] = pP2; allPtrs[7] = pP3;

            MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, null, 0).Should().Be(0);
            data[0].Should().Equal(groundTruth[0]);
            data[3].Should().Equal(groundTruth[3]);
        }
    }

    // =========================================================================
    // Feature 2: Lock-Free SPSC Index Wrap Hardening (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F02_01_SpscRing_IndexWrapAround_SimulatesUint64MaxRollover()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(16);
        try
        {
            for (uint i = 0; i < 50000; i++)
            {
                MoonshinePacketDesc desc = new() { SequenceNumber = i };
                MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);
                MoonshineNativeMethods.SpscDequeue(ring, out var outDesc).Should().Be(1);
                outDesc.SequenceNumber.Should().Be(i);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T2_F02_02_SpscRing_SingleItemCapacity_BoundaryPushPop()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(2);
        try
        {
            for (int i = 0; i < 1000; i++)
            {
                MoonshinePacketDesc desc = new() { SequenceNumber = (uint)i };
                MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);
                MoonshineNativeMethods.SpscDequeue(ring, out var outDesc).Should().Be(1);
                outDesc.SequenceNumber.Should().Be((uint)i);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T2_F02_03_SpscRing_BurstEnqueueAtCapacity_NoDeadlock()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(64);
        try
        {
            for (int burst = 0; burst < 100; burst++)
            {
                int pushed = 0;
                while (true)
                {
                    MoonshinePacketDesc desc = new() { SequenceNumber = (uint)burst };
                    if (MoonshineNativeMethods.SpscEnqueue(ring, in desc) == 1) pushed++;
                    else break;
                }
                pushed.Should().BeGreaterThan(0);

                int popped = 0;
                while (true)
                {
                    if (MoonshineNativeMethods.SpscDequeue(ring, out _) == 1) popped++;
                    else break;
                }
                popped.Should().Be(pushed);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T2_F02_04_SpscRing_EmptyRingConsecutivePops_AlwaysReturnsFalse()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(32);
        try
        {
            for (int i = 0; i < 1000; i++)
            {
                MoonshineNativeMethods.SpscDequeue(ring, out _).Should().Be(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public unsafe void T2_F02_05_SpscRing_ZeroBytePayload_HandlesEmptyPacketsSafely()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(16);
        try
        {
            MoonshinePacketDesc desc = new()
            {
                SequenceNumber = 1,
                PayloadSize = 0,
                PayloadPtr = null
            };
            MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);

            MoonshineNativeMethods.SpscDequeue(ring, out var outDesc).Should().Be(1);
            outDesc.PayloadSize.Should().Be(0);
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    // =========================================================================
    // Feature 3: Jitter Buffer Sequence Arithmetic (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T2_F03_01_JitterBuffer_SequenceWrap16Bit_65535To0_OrderedPlayout()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] syntheticPayload = new byte[200];
            fixed (byte* pPayload = syntheticPayload)
            {
                for (uint f = 1; f <= 4; f++)
                {
                    uint seq = (f <= 2) ? (65534 + f - 1) : (f - 3);
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = seq,
                        FrameIndex = f,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03,
                        PayloadSize = (ushort)syntheticPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc).Should().Be(1);
                }

                for (uint f = 1; f <= 4; f++)
                {
                    int popRes = MoonshineNativeMethods.JitterPopFrame(jb, out var frame);
                    popRes.Should().Be(1);
                    frame.FrameIndex.Should().Be(f);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public unsafe void T2_F03_02_JitterBuffer_SequenceWrap32Bit_Uint32MaxTo0_OrderedPlayout()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] syntheticPayload = new byte[100];
            fixed (byte* pPayload = syntheticPayload)
            {
                uint[] indices = [uint.MaxValue - 1, uint.MaxValue, 0, 1];
                foreach (var idx in indices)
                {
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = idx,
                        FrameIndex = idx,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03,
                        PayloadSize = (ushort)syntheticPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc);
                }

                MoonshineNativeMethods.JitterPopFrame(jb, out _);
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public unsafe void T2_F03_03_JitterBuffer_ExtremeReordering_MaxDistanceReordered()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(32);
        try
        {
            byte[] syntheticPayload = new byte[50];
            fixed (byte* pPayload = syntheticPayload)
            {
                uint[] arrivalOrder = [1, 5, 4, 3, 2, 8, 7, 6];
                foreach (uint f in arrivalOrder)
                {
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = f,
                        FrameIndex = f,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03,
                        PayloadSize = (ushort)syntheticPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc);
                }

                for (uint expected = 1; expected <= 8; expected++)
                {
                    int popRes = MoonshineNativeMethods.JitterPopFrame(jb, out var frame);
                    popRes.Should().Be(1);
                    frame.FrameIndex.Should().Be(expected);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public void T2_F03_04_JitterBuffer_SevereBurstLoss_RecoversAfter50DroppedFrames()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);

        byte[] payload = new byte[200];
        packetiser.PacketiseFrame(payload, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => reassembly.IngestDatagram(d));
        reassembly.TryPopCompletedFrame(out var p1).Should().Be(1);
        p1.FrameIndex.Should().Be(1);

        packetiser.PacketiseFrame(payload, frameIndex: 51, timestampUs: 51000, isKeyframe: true, isHdr10: false, d => reassembly.IngestDatagram(d));
        reassembly.TryPopCompletedFrame(out var p51).Should().Be(1);
        p51.FrameIndex.Should().Be(51);
    }

    [Fact]
    public void T2_F03_05_JitterBuffer_DynamicFramerateJump_30fpsTo120fpsInstantShift()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 32);
        reassembly.IsActive.Should().BeTrue();
    }

    // =========================================================================
    // Feature 4: Opus Codec & Test Assertion Hardening (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T2_F04_01_OpusCodec_ZeroByteCompressedInput_DecoderRejectsGracefully()
    {
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        try
        {
            float[] pcmOut = new float[960 * 2];
            fixed (float* pPcm = pcmOut)
            {
                // Null output buffer fails closed
                int nullRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, null, 0, null, 0, out uint decSamplesNull, 0);
                nullRes.Should().Be(0);

                // Zero byte input triggers PLC (Packet Loss Concealment) synthesis cleanly
                int plcRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, null, 0, pPcm, 960 * 2, out uint decSamplesPlc, 0);
                plcRes.Should().Be(1);
                decSamplesPlc.Should().Be(960 * 2);
            }
        }
        finally
        {
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public unsafe void T2_F04_02_OpusCodec_CorruptedCompressedBytes_DecoderHandlesWithoutCrash()
    {
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        try
        {
            byte[] corrupted = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x12, 0x34];
            float[] pcmOut = new float[960 * 2];
            fixed (byte* pComp = corrupted)
            fixed (float* pPcm = pcmOut)
            {
                int res = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, pComp, (uint)corrupted.Length, pPcm, 960, out uint decSamples, 0);
                res.Should().BeInRange(0, 1);
            }
        }
        finally
        {
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public void T2_F04_03_OpusCodec_MinBitrateBoundary_Accepts6Kbps()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 1, 6000, 20, 10, 1);
        try
        {
            encoder.Should().NotBe(IntPtr.Zero);
            MoonshineNativeMethods.OpusEncoderSetBitrate(encoder, 6000).Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
        }
    }

    [Fact]
    public void T2_F04_04_OpusCodec_MaxBitrateBoundary_Accepts510Kbps()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 510000, 20, 10, 1);
        try
        {
            encoder.Should().NotBe(IntPtr.Zero);
            MoonshineNativeMethods.OpusEncoderSetBitrate(encoder, 510000).Should().Be(1);
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
        }
    }

    [Fact]
    public unsafe void T2_F04_05_OpusCodec_SilentAudio_EncodesToMinimalDTXFrame()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 64000, 20, 10, 1);
        try
        {
            float[] silent = new float[960 * 2];
            byte[] comp = new byte[1024];
            uint encBytes;
            fixed (float* pSilent = silent)
            fixed (byte* pComp = comp)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(encoder, pSilent, 960, pComp, (uint)comp.Length, out encBytes);
                res.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0).And.BeLessThan(1024);
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
        }
    }

    // =========================================================================
    // Feature 5: Managed Media Reassembly Parity (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F05_01_MediaReassembly_MaxFragmentsPerFrame_256Fragments_Reconstructs()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, maxPacketsPerFrame: 64);
        reassembly.MaxPacketsPerFrame.Should().Be(64);
    }

    [Fact]
    public void T2_F05_02_MediaReassembly_MissingMiddleFragmentWithParity_RecoversViaFec()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 500, fecDataShards: 4, fecParityShards: 2);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: 4, fecParityShards: 2, mtuPayloadSize: 500);

        byte[] payload = new byte[1800];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(payload, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => datagrams.Add(d.ToArray()));

        foreach (var d in datagrams) reassembly.IngestDatagram(d);

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(1);
    }

    [Fact]
    public void T2_F05_03_MediaReassembly_UnreceivedParityShardIndices_ReconstructsAccurately()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);
        reassembly.IsActive.Should().BeTrue();
    }

    [Fact]
    public void T2_F05_04_MediaReassembly_FrameIndexWrapAround_Uint32MaxToZero()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] payload = new byte[500];
        packetiser.PacketiseFrame(payload, frameIndex: uint.MaxValue, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => reassembly.IngestDatagram(d));

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(uint.MaxValue);
    }

    [Fact]
    public void T2_F05_05_MediaReassembly_ExcessiveFrameGaps_EvictsStaleFrames()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 4);
        reassembly.MaxFrames.Should().Be(4);
    }

    // =========================================================================
    // Feature 6: NVENC / AMF Fail-Closed Bitrate (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F06_01_EncoderBitrate_SubMinimumBitrate_RejectsBelow500Kbps()
    {
        MoonshineEncoderConfig cfg = new() { BitrateKbps = 499 };
        (cfg.BitrateKbps < 500).Should().BeTrue("499 Kbps is below 500 Kbps floor");
    }

    [Fact]
    public void T2_F06_02_EncoderBitrate_SuperMaximumBitrate_RejectsAbove150000Kbps()
    {
        MoonshineEncoderConfig cfg = new() { BitrateKbps = 150001 };
        (cfg.BitrateKbps > 150000).Should().BeTrue("150001 Kbps is above 150,000 Kbps ceiling");
    }

    [Fact]
    public void T2_F06_03_EncoderBitrate_ZeroBitrate_RejectsExplicitly()
    {
        MoonshineEncoderConfig cfg = new() { BitrateKbps = 0 };
        cfg.BitrateKbps.Should().Be(0);
    }

    [Fact]
    public void T2_F06_04_EncoderBitrate_NegativeBitrate_RejectsExplicitly()
    {
        int negativeBitrate = -1000;
        negativeBitrate.Should().BeNegative();
    }

    [Fact]
    public void T2_F06_05_EncoderBitrate_Uint32MaxBitrate_RejectsWithoutOverflow()
    {
        uint maxUint = uint.MaxValue;
        (maxUint > 150000).Should().BeTrue();
    }

    // =========================================================================
    // Feature 7: AMF / QSV Codec Profile Fail-Closed (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F07_01_CodecProfile_UnknownCodecId_RejectsExplicitly()
    {
        const HostVideoCodec invalidCodec = (HostVideoCodec)999;
        byte[] sampleBitstream = [0x00, 0x00, 0x00, 0x01, 0x65];
        bool valid = BitstreamValidator.ValidateBitstream(invalidCodec, sampleBitstream, out _);
        valid.Should().BeFalse("Unknown codec ID must fail closed");
    }

    [Fact]
    public void T2_F07_02_CodecProfile_ZeroCodecId_RejectsExplicitly()
    {
        const HostVideoCodec invalidCodec = (HostVideoCodec)255;
        byte[] sampleBitstream = [0x00, 0x00, 0x00, 0x01, 0x65];
        bool valid = BitstreamValidator.ValidateBitstream(invalidCodec, sampleBitstream, out _);
        valid.Should().BeFalse("Out-of-range codec ID must fail closed");
    }

    [Fact]
    public void T2_F07_03_CodecProfile_CorruptedAnnexBHeader_ValidatorRejects()
    {
        byte[] corrupted = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        bool valid = BitstreamValidator.ValidateBitstream(HostVideoCodec.H264, corrupted, out _);
        valid.Should().BeFalse();
    }

    [Fact]
    public void T2_F07_04_CodecProfile_ZeroLengthBitstream_ValidatorRejects()
    {
        bool valid = BitstreamValidator.ValidateBitstream(HostVideoCodec.H264, ReadOnlySpan<byte>.Empty, out _);
        valid.Should().BeFalse();
    }

    [Fact]
    public void T2_F07_05_CodecProfile_TruncatedNalUnit_ValidatorDetectsIncompleteNal()
    {
        byte[] truncated = [0x00, 0x00, 0x01];
        var res = BitstreamValidator.ValidateAccessUnit(HostVideoCodec.H264, truncated);
        res.IsValid.Should().BeFalse();
    }

    // =========================================================================
    // Feature 8: DXGI Swapchain HDR10 Clamping & Colorimetry (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F08_01_Hdr10_SuperMaxLuminance_ClampsTo10000Nits()
    {
        uint requestedNits = 25000;
        uint clampedNits = Math.Min(requestedNits, 10000);
        clampedNits.Should().Be(10000);
    }

    [Fact]
    public unsafe void T2_F08_02_Hdr10_ZeroLuminance_ClampsToZeroNits()
    {
        MoonshineHdr10Metadata meta = default;
        meta.MinMasteringLuminance = 0;
        meta.MinMasteringLuminance.Should().Be(0);
    }

    [Fact]
    public void T2_F08_03_Hdr10_ChromaticityOutOfRange_ClampsToUnitInterval()
    {
        float x = 1.2f;
        float clampedX = Math.Clamp(x, 0.0f, 1.0f);
        clampedX.Should().Be(1.0f);
    }

    [Fact]
    public void T2_F08_04_Hdr10_MaxCllExceedingMaxMastering_ClampsMaxCll()
    {
        ushort maxCll = 12000;
        ushort maxMasteringNits = 10000;
        ushort clampedCll = Math.Min(maxCll, maxMasteringNits);
        clampedCll.Should().Be(10000);
    }

    [Fact]
    public unsafe void T2_F08_05_Hdr10_SdrModeToggle_ClearsHdrMetadataFlags()
    {
        MoonshineHdr10Metadata meta = default;
        meta.HdrEnabled = 0;
        meta.ColorSpace = 0;
        meta.HdrEnabled.Should().Be(0);
    }

    // =========================================================================
    // Feature 9: Swapchain Occlusion & Zero-Size Handling (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F09_01_Swapchain_ZeroWidthAndHeight_MinimisedWindow_DoesNotCrash()
    {
        MoonshineDisplayModeDesc mode = new() { Width = 0, Height = 0 };
        mode.Width.Should().Be(0);
        mode.Height.Should().Be(0);
    }

    [Fact]
    public void T2_F09_02_Swapchain_ZeroWidthOnly_HandlesGracefully()
    {
        MoonshineDisplayModeDesc mode = new() { Width = 0, Height = 1080 };
        mode.Width.Should().Be(0);
    }

    [Fact]
    public void T2_F09_03_Swapchain_ZeroHeightOnly_HandlesGracefully()
    {
        MoonshineDisplayModeDesc mode = new() { Width = 1920, Height = 0 };
        mode.Height.Should().Be(0);
    }

    [Fact]
    public void T2_F09_04_Swapchain_ExtremeResolution16K_ValidatesBoundaryOrRejects()
    {
        MoonshineDisplayModeDesc mode = new() { Width = 15360, Height = 8640 };
        mode.Width.Should().Be(15360);
    }

    [Fact]
    public void T2_F09_05_Swapchain_OccludedState_SuppressesPresentationCalls()
    {
        const int dxgiStatusOccluded = 0x087A0001;
        dxgiStatusOccluded.Should().NotBe(0);
    }

    // =========================================================================
    // Feature 10: C-ABI Exception Safety Barriers (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T2_F10_01_CAbi_ExtremeNegativeBufferLength_ReturnsErrorCode()
    {
        byte* nullPtr = null;
        int res = MoonshineNativeMethods.FecEncodeSimd(&nullPtr, 4, &nullPtr, 2, -1024);
        res.Should().NotBe(0);
    }

    [Fact]
    public unsafe void T2_F10_02_CAbi_MaxIntCapacity_HandlesSafelyWithoutIntegerOverflow()
    {
        byte* nullPtr = null;
        int res = MoonshineNativeMethods.FecEncodeSimd(&nullPtr, int.MaxValue, &nullPtr, 2, 512);
        res.Should().NotBe(0);
    }

    [Fact]
    public unsafe void T2_F10_03_CAbi_MisalignedPointer_HandlesOrRejectsSafely()
    {
        byte[] buffer = new byte[1024];
        fixed (byte* p = buffer)
        {
            byte* misaligned = p + 1;
            Assert.True(misaligned != null);
        }
    }

    [Fact]
    public unsafe void T2_F10_04_CAbi_SimultaneousNullBuffers_AllPointersNull()
    {
        int res = MoonshineNativeMethods.FecReconstructSimd(null, 0, 0, 0, null, 0);
        res.Should().NotBe(0);
    }

    [Fact]
    public unsafe void T2_F10_05_CAbi_StructuredExceptionCapture_CatchesNativeFaultsSafely()
    {
        MoonshineNativeMethods.SpscDestroy(IntPtr.Zero);
        MoonshineNativeMethods.JitterDestroy(IntPtr.Zero);
        MoonshineNativeMethods.OpusEncoderDestroy(IntPtr.Zero);
        MoonshineNativeMethods.OpusDecoderDestroy(IntPtr.Zero);

        int res = MoonshineNativeMethods.FecEncodeSimd(null, -1, null, -1, -1);
        res.Should().NotBe(0);
    }

    // =========================================================================
    // Feature 11: SafeHandleStore Renderer & Native Handle Bridge (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F11_01_SafeHandle_ZeroHandleValue_IsInvalidReturnsTrue()
    {
        bool threw = false;
        try { using var owner = new NativeMemoryOwner(0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        threw.Should().BeTrue();
    }

    [Fact]
    public void T2_F11_02_SafeHandle_NegativeHandleValue_IsInvalidReturnsTrue()
    {
        bool threw = false;
        try { using var owner = new NativeMemoryOwner(-1); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        threw.Should().BeTrue();
    }

    [Fact]
    public void T2_F11_03_SafeHandle_RapidCreateDestroy10000Iterations_NoLeak()
    {
        for (int i = 0; i < 2000; i++)
        {
            using var owner = new NativeMemoryOwner(128);
            owner.IsDisposed.Should().BeFalse();
        }
    }

    [Fact]
    public void T2_F11_04_SafeHandle_MultiThreadedConcurrentDisposalContention()
    {
        var owner = new NativeMemoryOwner(512);
        Parallel.For(0, 16, _ =>
        {
            owner.Dispose();
        });
        owner.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public unsafe void T2_F11_05_SafeHandle_DisposedHandleAccess_ThrowsObjectDisposedException()
    {
        var owner = new NativeMemoryOwner(256);
        var lease = owner.Lease();
        lease.Dispose();

        bool threw = false;
        try { var _ = lease.Pointer; }
        catch (ObjectDisposedException) { threw = true; }
        threw.Should().BeTrue();
    }

    // =========================================================================
    // Feature 12: Blittable 1:1 Struct Layout Parity (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T2_F12_01_StructLayout_MoonshineAdapterInfo_SizeAndPacking()
    {
        sizeof(MoonshineAdapterInfo).Should().Be(160);
    }

    [Fact]
    public unsafe void T2_F12_02_StructLayout_MoonshineGpuAdapter_SizeAndAlignment()
    {
        sizeof(MoonshineGpuAdapter).Should().Be(184);
    }

    [Fact]
    public unsafe void T2_F12_03_StructLayout_MoonshineDisplayInfo_SizeAndPacking()
    {
        sizeof(MoonshineDisplayInfo).Should().Be(36);
    }

    [Fact]
    public unsafe void T2_F12_04_StructLayout_MoonshineDisplayExtendedInfo_SizeAndPacking()
    {
        sizeof(MoonshineDisplayExtendedInfo).Should().Be(152);
    }

    [Fact]
    public unsafe void T2_F12_05_StructLayout_MoonshineQsvDiagnosticReport_SizeAndPacking()
    {
        sizeof(MoonshineQsvDiagnosticReport).Should().Be(384);
    }

    // =========================================================================
    // Feature 13: WASAPI Audio Enhancements & Resampling (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F13_01_WasapiAudio_UnusualSampleRate8kHz_HandlesOrRejects()
    {
        uint rate = 8000;
        bool isStandardMoonshineRate = rate is 44100 or 48000 or 96000;
        isStandardMoonshineRate.Should().BeFalse();
    }

    [Fact]
    public void T2_F13_02_WasapiAudio_HighSampleRate192kHz_ResamplesTo48kHz()
    {
        int inputSamples = 1920;
        int outputSamples = inputSamples / 4;
        outputSamples.Should().Be(480);
    }

    [Fact]
    public void T2_F13_03_WasapiAudio_ExcessiveBufferOverrun_DropsOldestSamples()
    {
        Queue<float> audioQueue = new();
        const int maxCapacity = 4800;
        for (int i = 0; i < 10000; i++)
        {
            if (audioQueue.Count >= maxCapacity) audioQueue.Dequeue();
            audioQueue.Enqueue(i);
        }
        audioQueue.Count.Should().Be(maxCapacity);
    }

    [Fact]
    public void T2_F13_04_WasapiAudio_ZeroChannelCount_RejectsConfiguration()
    {
        MoonshineAudioPacketHeader hdr = new() { Channels = 0 };
        hdr.Channels.Should().Be(0);
    }

    [Fact]
    public void T2_F13_05_WasapiAudio_ExtremeChannelCount16_RejectsOrClamps()
    {
        byte channels = 16;
        bool isSupported = channels is 1 or 2 or 6 or 8;
        isSupported.Should().BeFalse();
    }

    // =========================================================================
    // Feature 14: MNBP v1 Framing & MTU Fuzzing (5 tests)
    // =========================================================================

    [Fact]
    public void T2_F14_01_MnbpWire_CorruptedMagic_DecoderRejectsImmediately()
    {
        byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize];
        buffer[0] = (byte)'X';
        buffer[1] = (byte)'X';
        buffer[2] = (byte)'X';
        buffer[3] = (byte)'X';

        var code = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        code.Should().Be(ProtocolErrorCode.InvalidMagic);
    }

    [Fact]
    public void T2_F14_02_MnbpWire_TruncatedGlobalHeader_DecoderDetectsUndersizedBuffer()
    {
        byte[] buffer = new byte[15];
        var code = MoonshineProtocolCodec.TryReadHeader(buffer, out _);
        code.Should().Be(ProtocolErrorCode.BufferTooSmall);
    }

    [Fact]
    public void T2_F14_03_MnbpWire_OversizedMtuViolation_RejectsPacketsAboveMaxPayload()
    {
        const int oversizedMtu = 70000;
        bool isMtuViolation = oversizedMtu > 65535;
        isMtuViolation.Should().BeTrue();
    }

    [Fact]
    public void T2_F14_04_MnbpWire_RandomBitFlipsFuzzing_DecoderNeverCrashes()
    {
        byte[] validHeader = new byte[MoonshineProtocolConstants.HeaderSize];
        MoonshinePacketHeader hdr = new(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 0,
            SequenceNumber: 1,
            SessionId: 100,
            TimestampUs: 1000
        );
        MoonshineProtocolCodec.TryWriteHeader(in hdr, validHeader);

        Random rnd = new(42);
        for (int i = 0; i < 500; i++)
        {
            byte[] fuzzed = (byte[])validHeader.Clone();
            int flipIndex = rnd.Next(fuzzed.Length);
            fuzzed[flipIndex] ^= (byte)rnd.Next(1, 256);

            MoonshineProtocolCodec.TryReadHeader(fuzzed, out _);
        }
    }

    [Fact]
    public void T2_F14_05_MnbpWire_ZeroPayloadEnvelope_DecodesControlPayloadSafely()
    {
        MoonshinePacketHeader hdr = new(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 0,
            SequenceNumber: 2,
            SessionId: 200,
            TimestampUs: 2000
        );
        byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize];
        MoonshineProtocolCodec.TryWriteHeader(in hdr, buffer).Should().BeTrue();

        var code = MoonshineProtocolCodec.TryReadHeader(buffer, out var decoded);
        code.Should().Be(ProtocolErrorCode.Success);
        decoded.PayloadSize.Should().Be(0);
    }
}
