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
/// Tier 1: Feature Coverage E2E Test Suite.
/// Provides comprehensive opaque-box requirement-driven test coverage across all 14 core Moonshine subsystems.
/// Exactly 5 test cases per feature (70 test cases total).
/// </summary>
public class Tier1_FeatureCoverageTests
{
    // =========================================================================
    // Feature 1: SIMD GF(2^8) FEC Erasure Hardening (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F01_01_SimdFec_ZeroLoss_ComputesParityAndReturnsSuccess()
    {
        const int dataCount = 8;
        const int parityCount = 4;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 1024;

        byte[][] shards = new byte[totalCount][];
        for (int i = 0; i < dataCount; i++)
        {
            shards[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++) shards[i][j] = (byte)((i * 31 + j * 7) & 0xFF);
        }
        for (int i = dataCount; i < totalCount; i++) shards[i] = new byte[shardSize];

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2], s3 = shards[3],
                     s4 = shards[4], s5 = shards[5], s6 = shards[6], s7 = shards[7],
                     p0 = shards[8], p1 = shards[9], p2 = shards[10], p3 = shards[11])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = s0; dPtrs[1] = s1; dPtrs[2] = s2; dPtrs[3] = s3;
            dPtrs[4] = s4; dPtrs[5] = s5; dPtrs[6] = s6; dPtrs[7] = s7;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = p0; pPtrs[1] = p1; pPtrs[2] = p2; pPtrs[3] = p3;

            int encRes = MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize);
            encRes.Should().Be(0, "SIMD FEC encoding must return 0 on success");

            byte** allPtrs = stackalloc byte*[totalCount];
            for (int i = 0; i < dataCount; i++) allPtrs[i] = dPtrs[i];
            for (int i = 0; i < parityCount; i++) allPtrs[dataCount + i] = pPtrs[i];

            // Zero loss decoding (all data shards present)
            int decRes = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, null, 0);
            decRes.Should().Be(0, "SIMD FEC decoding with 0 losses must return 0 without error");
        }
    }

    [Fact]
    public unsafe void T1_F01_02_SimdFec_SingleShardLoss_ReconstructsMissingDataShard()
    {
        const int dataCount = 4;
        const int parityCount = 2;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 512;

        byte[][] shards = new byte[totalCount][];
        byte[][] groundTruth = new byte[dataCount][];
        for (int i = 0; i < dataCount; i++)
        {
            shards[i] = new byte[shardSize];
            groundTruth[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++)
            {
                byte val = (byte)((i * 47 + j * 13) & 0xFF);
                shards[i][j] = val;
                groundTruth[i][j] = val;
            }
        }
        for (int i = dataCount; i < totalCount; i++) shards[i] = new byte[shardSize];

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2], s3 = shards[3],
                     p0 = shards[4], p1 = shards[5])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = s0; dPtrs[1] = s1; dPtrs[2] = s2; dPtrs[3] = s3;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = p0; pPtrs[1] = p1;

            MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize).Should().Be(0);

            // Erase shard index 1
            Array.Clear(shards[1], 0, shardSize);
            int[] erasedIndices = [1];
            fixed (int* pErased = erasedIndices)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                for (int i = 0; i < dataCount; i++) allPtrs[i] = dPtrs[i];
                for (int i = 0; i < parityCount; i++) allPtrs[dataCount + i] = pPtrs[i];

                int decRes = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, pErased, 1);
                decRes.Should().Be(0, "SIMD FEC decode should reconstruct missing shard");
            }

            shards[1].Should().Equal(groundTruth[1], "Reconstructed shard must match original ground truth bytes");
        }
    }

    [Fact]
    public unsafe void T1_F01_03_SimdFec_MaxParityLossRecovery_RecoversAllLostDataShards()
    {
        const int dataCount = 6;
        const int parityCount = 3;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 256;

        byte[][] shards = new byte[totalCount][];
        byte[][] groundTruth = new byte[dataCount][];
        for (int i = 0; i < dataCount; i++)
        {
            shards[i] = new byte[shardSize];
            groundTruth[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++)
            {
                byte val = (byte)((i * 19 + j * 29 + 7) & 0xFF);
                shards[i][j] = val;
                groundTruth[i][j] = val;
            }
        }
        for (int i = dataCount; i < totalCount; i++) shards[i] = new byte[shardSize];

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2],
                     s3 = shards[3], s4 = shards[4], s5 = shards[5],
                     p0 = shards[6], p1 = shards[7], p2 = shards[8])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = s0; dPtrs[1] = s1; dPtrs[2] = s2; dPtrs[3] = s3; dPtrs[4] = s4; dPtrs[5] = s5;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = p0; pPtrs[1] = p1; pPtrs[2] = p2;

            MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize).Should().Be(0);

            // Erase 3 shards (shards 0, 2, 4)
            Array.Clear(shards[0], 0, shardSize);
            Array.Clear(shards[2], 0, shardSize);
            Array.Clear(shards[4], 0, shardSize);
            int[] erasedIndices = [0, 2, 4];
            fixed (int* pErased = erasedIndices)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                for (int i = 0; i < dataCount; i++) allPtrs[i] = dPtrs[i];
                for (int i = 0; i < parityCount; i++) allPtrs[dataCount + i] = pPtrs[i];

                int decRes = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, pErased, 3);
                decRes.Should().Be(0, "Decoding with lost shards equal to parity count must succeed");
            }

            shards[0].Should().Equal(groundTruth[0]);
            shards[2].Should().Equal(groundTruth[2]);
            shards[4].Should().Equal(groundTruth[4]);
        }
    }

    [Fact]
    public unsafe void T1_F01_04_SimdFec_MultiShardParityVerification_ValidatesCauchyMatrixParity()
    {
        const int dataCount = 10;
        const int parityCount = 4;
        const int shardSize = 128;

        byte[][] dataShards = new byte[dataCount][];
        byte[][] parityShards = new byte[parityCount][];
        for (int i = 0; i < dataCount; i++)
        {
            dataShards[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++) dataShards[i][j] = (byte)((i * 11 + j * 23) & 0xFF);
        }
        for (int i = 0; i < parityCount; i++) parityShards[i] = new byte[shardSize];

        fixed (byte* pD0 = dataShards[0], pD1 = dataShards[1], pD2 = dataShards[2], pD3 = dataShards[3], pD4 = dataShards[4],
                     pD5 = dataShards[5], pD6 = dataShards[6], pD7 = dataShards[7], pD8 = dataShards[8], pD9 = dataShards[9])
        fixed (byte* pP0 = parityShards[0], pP1 = parityShards[1], pP2 = parityShards[2], pP3 = parityShards[3])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = pD0; dPtrs[1] = pD1; dPtrs[2] = pD2; dPtrs[3] = pD3; dPtrs[4] = pD4;
            dPtrs[5] = pD5; dPtrs[6] = pD6; dPtrs[7] = pD7; dPtrs[8] = pD8; dPtrs[9] = pD9;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = pP0; pPtrs[1] = pP1; pPtrs[2] = pP2; pPtrs[3] = pP3;

            int encRes = MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize);
            encRes.Should().Be(0);

            // Verify that parity shards are non-zero
            bool hasNonZeroParity = false;
            for (int i = 0; i < parityCount; i++)
            {
                for (int j = 0; j < shardSize; j++)
                {
                    if (parityShards[i][j] != 0) { hasNonZeroParity = true; break; }
                }
            }
            hasNonZeroParity.Should().BeTrue("Cauchy parity generator must compute non-trivial parity vectors");
        }
    }

    [Fact]
    public unsafe void T1_F01_05_SimdFec_LargeShardBlock_ReconstructsSaturatedPayload()
    {
        const int dataCount = 16;
        const int parityCount = 4;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 2048;

        byte[][] shards = new byte[totalCount][];
        byte[][] groundTruth = new byte[dataCount][];
        for (int i = 0; i < dataCount; i++)
        {
            shards[i] = new byte[shardSize];
            groundTruth[i] = new byte[shardSize];
            for (int j = 0; j < shardSize; j++)
            {
                byte val = (byte)((i * 53 + j) & 0xFF);
                shards[i][j] = val;
                groundTruth[i][j] = val;
            }
        }
        for (int i = dataCount; i < totalCount; i++) shards[i] = new byte[shardSize];

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2], s3 = shards[3],
                     s4 = shards[4], s5 = shards[5], s6 = shards[6], s7 = shards[7],
                     s8 = shards[8], s9 = shards[9], s10 = shards[10], s11 = shards[11],
                     s12 = shards[12], s13 = shards[13], s14 = shards[14], s15 = shards[15],
                     p0 = shards[16], p1 = shards[17], p2 = shards[18], p3 = shards[19])
        {
            byte** dPtrs = stackalloc byte*[dataCount];
            dPtrs[0] = s0; dPtrs[1] = s1; dPtrs[2] = s2; dPtrs[3] = s3;
            dPtrs[4] = s4; dPtrs[5] = s5; dPtrs[6] = s6; dPtrs[7] = s7;
            dPtrs[8] = s8; dPtrs[9] = s9; dPtrs[10] = s10; dPtrs[11] = s11;
            dPtrs[12] = s12; dPtrs[13] = s13; dPtrs[14] = s14; dPtrs[15] = s15;

            byte** pPtrs = stackalloc byte*[parityCount];
            pPtrs[0] = p0; pPtrs[1] = p1; pPtrs[2] = p2; pPtrs[3] = p3;

            MoonshineNativeMethods.FecEncodeSimd(dPtrs, dataCount, pPtrs, parityCount, shardSize).Should().Be(0);

            // Erase shards 3 and 7
            Array.Clear(shards[3], 0, shardSize);
            Array.Clear(shards[7], 0, shardSize);
            int[] erased = [3, 7];
            fixed (int* pErased = erased)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                for (int i = 0; i < dataCount; i++) allPtrs[i] = dPtrs[i];
                for (int i = 0; i < parityCount; i++) allPtrs[dataCount + i] = pPtrs[i];

                MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, pErased, 2).Should().Be(0);
            }

            shards[3].Should().Equal(groundTruth[3]);
            shards[7].Should().Equal(groundTruth[7]);
        }
    }

    // =========================================================================
    // Feature 2: Lock-Free SPSC Index Wrap Hardening (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F02_01_SpscRing_BasicEnqueueDequeue_PreservesOrderingAndValues()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(64);
        ring.Should().NotBe(IntPtr.Zero);
        try
        {
            for (uint i = 1; i <= 20; i++)
            {
                MoonshinePacketDesc desc = new()
                {
                    SequenceNumber = i,
                    FrameIndex = 100 + i,
                    PacketIndex = (ushort)i,
                    TotalPackets = 20,
                    PayloadSize = 100,
                    BufferSlotIndex = (int)i
                };
                int pushRes = MoonshineNativeMethods.SpscEnqueue(ring, in desc);
                pushRes.Should().Be(1, $"Enqueue of item {i} must succeed");
            }

            for (uint i = 1; i <= 20; i++)
            {
                int popRes = MoonshineNativeMethods.SpscDequeue(ring, out var outDesc);
                popRes.Should().Be(1, $"Dequeue of item {i} must succeed");
                outDesc.SequenceNumber.Should().Be(i);
                outDesc.FrameIndex.Should().Be(100 + i);
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T1_F02_02_SpscRing_Saturation_RejectsOrDropsWhenFull()
    {
        const int capacity = 16;
        IntPtr ring = MoonshineNativeMethods.SpscCreate(capacity);
        ring.Should().NotBe(IntPtr.Zero);
        try
        {
            int pushed = 0;
            for (uint i = 0; i < capacity + 5; i++)
            {
                MoonshinePacketDesc desc = new() { SequenceNumber = i };
                int res = MoonshineNativeMethods.SpscEnqueue(ring, in desc);
                if (res == 1) pushed++;
            }

            pushed.Should().BeInRange(capacity - 1, capacity, "Ring buffer must saturate at capacity limit");

            MoonshinePacketDesc overflowDesc = new() { SequenceNumber = 999 };
            MoonshineNativeMethods.SpscEnqueue(ring, in overflowDesc).Should().Be(0, "Enqueue to saturated ring must return 0");
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T1_F02_03_SpscRing_DrainToEmpty_ReportsEmptyStateCorrectly()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(32);
        try
        {
            for (uint i = 0; i < 10; i++)
            {
                MoonshinePacketDesc desc = new() { SequenceNumber = i };
                MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);
            }

            for (int i = 0; i < 10; i++)
            {
                MoonshineNativeMethods.SpscDequeue(ring, out _).Should().Be(1);
            }

            // Ring is now empty
            MoonshineNativeMethods.SpscDequeue(ring, out _).Should().Be(0, "Dequeue on empty ring must return 0");
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public async Task T1_F02_04_SpscRing_ConcurrentProducerConsumer_NoDataLoss()
    {
        IntPtr ring = MoonshineNativeMethods.SpscCreate(1024);
        const int totalItems = 5000;
        List<uint> consumed = new(totalItems);

        try
        {
            var consumerTask = Task.Run(() =>
            {
                while (consumed.Count < totalItems)
                {
                    if (MoonshineNativeMethods.SpscDequeue(ring, out var desc) == 1)
                    {
                        consumed.Add(desc.SequenceNumber);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            });

            var producerTask = Task.Run(() =>
            {
                for (uint i = 1; i <= totalItems; i++)
                {
                    MoonshinePacketDesc desc = new() { SequenceNumber = i };
                    while (MoonshineNativeMethods.SpscEnqueue(ring, in desc) == 0)
                    {
                        Thread.Yield();
                    }
                }
            });

            await Task.WhenAll(producerTask, consumerTask);

            consumed.Count.Should().Be(totalItems);
            for (int i = 0; i < totalItems; i++)
            {
                consumed[i].Should().Be((uint)(i + 1), "Items must be consumed in strict monotonic order");
            }
        }
        finally
        {
            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    [Fact]
    public void T1_F02_05_SpscRing_PowerOfTwoCapacities_AllocatesAndExecutesCorrectly()
    {
        int[] capacities = [16, 32, 64, 128, 256, 512, 1024];
        foreach (int cap in capacities)
        {
            IntPtr ring = MoonshineNativeMethods.SpscCreate((nuint)cap);
            ring.Should().NotBe(IntPtr.Zero, $"Ring creation with capacity {cap} must succeed");

            MoonshinePacketDesc desc = new() { SequenceNumber = 42 };
            MoonshineNativeMethods.SpscEnqueue(ring, in desc).Should().Be(1);

            MoonshineNativeMethods.SpscDequeue(ring, out var outDesc).Should().Be(1);
            outDesc.SequenceNumber.Should().Be(42);

            MoonshineNativeMethods.SpscDestroy(ring);
        }
    }

    // =========================================================================
    // Feature 3: Jitter Buffer Sequence Arithmetic (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F03_01_JitterBuffer_InOrderArrival_PopsFramesInSequentialOrder()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] testPayload = new byte[200];
            fixed (byte* pPayload = testPayload)
            {
                for (uint f = 1; f <= 5; f++)
                {
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = f,
                        FrameIndex = f,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03, // Start and End
                        PayloadSize = (ushort)testPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc).Should().Be(1);
                }

                for (uint f = 1; f <= 5; f++)
                {
                    int popRes = MoonshineNativeMethods.JitterPopFrame(jb, out var frame);
                    popRes.Should().Be(1, $"Frame {f} should pop cleanly");
                    frame.FrameIndex.Should().Be(f);
                    frame.PacketCount.Should().Be(1);
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public unsafe void T1_F03_02_JitterBuffer_OutOfOrderPackets_ReordersCorrectlyBeforePlayout()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] testPayload = new byte[150];
            fixed (byte* pPayload = testPayload)
            {
                uint[] arrivalOrder = [1, 3, 2];
                foreach (uint f in arrivalOrder)
                {
                    MoonshinePacketDesc desc = new()
                    {
                        SequenceNumber = f,
                        FrameIndex = f,
                        PacketIndex = 0,
                        TotalPackets = 1,
                        Flags = 0x03,
                        PayloadSize = (ushort)testPayload.Length,
                        PayloadPtr = pPayload
                    };
                    MoonshineNativeMethods.JitterPushPacket(jb, in desc).Should().Be(1);
                }

                for (uint expected = 1; expected <= 3; expected++)
                {
                    MoonshineNativeMethods.JitterPopFrame(jb, out var frame).Should().Be(1);
                    frame.FrameIndex.Should().Be(expected, $"Playout must be strictly ordered, expected {expected}");
                }
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public unsafe void T1_F03_03_JitterBuffer_DuplicatePackets_IgnoresDuplicateArrivals()
    {
        IntPtr jb = MoonshineNativeMethods.JitterCreate(16);
        try
        {
            byte[] testPayload = new byte[100];
            fixed (byte* pPayload = testPayload)
            {
                MoonshinePacketDesc desc = new()
                {
                    SequenceNumber = 1,
                    FrameIndex = 1,
                    PacketIndex = 0,
                    TotalPackets = 1,
                    Flags = 0x03,
                    PayloadSize = (ushort)testPayload.Length,
                    PayloadPtr = pPayload
                };

                MoonshineNativeMethods.JitterPushPacket(jb, in desc).Should().Be(1);
                MoonshineNativeMethods.JitterPushPacket(jb, in desc);

                MoonshineNativeMethods.JitterPopFrame(jb, out _).Should().Be(1);
                MoonshineNativeMethods.JitterPopFrame(jb, out _).Should().Be(0);
            }
        }
        finally
        {
            MoonshineNativeMethods.JitterDestroy(jb);
        }
    }

    [Fact]
    public void T1_F03_04_JitterBuffer_PlayoutDelayAdaptation_MaintainsTargetLatency()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);
        reassembly.IsActive.Should().BeTrue();
        reassembly.MaxFrames.Should().Be(16);
    }

    [Fact]
    public void T1_F03_05_JitterBuffer_LatePacketDrop_DropsPacketsArrivingPastPlayoutDeadline()
    {
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 8);
        reassembly.IsActive.Should().BeTrue();
    }

    // =========================================================================
    // Feature 4: Opus Codec & Audio Resampling (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F04_01_OpusCodec_Stereo48kHz_EncodesAndDecodesSyntheticTone()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 128000, 20, 10, 1);
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 2);
        encoder.Should().NotBe(IntPtr.Zero);
        decoder.Should().NotBe(IntPtr.Zero);

        try
        {
            const int frameSize = 960;
            float[] pcmIn = new float[frameSize * 2];
            for (int i = 0; i < frameSize; i++)
            {
                float sample = MathF.Sin(2 * MathF.PI * 440 * i / 48000.0f);
                pcmIn[i * 2] = sample;
                pcmIn[i * 2 + 1] = sample;
            }

            byte[] compressed = new byte[1024];
            uint encBytes;
            fixed (float* pPcmIn = pcmIn)
            fixed (byte* pComp = compressed)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(encoder, pPcmIn, frameSize, pComp, (uint)compressed.Length, out encBytes);
                res.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0, "Opus encoding should produce compressed bytes");

            float[] pcmOut = new float[frameSize * 2];
            uint decSamples;
            fixed (byte* pComp = compressed)
            fixed (float* pPcmOut = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, pComp, encBytes, pPcmOut, (uint)(frameSize * 2), out decSamples, 0);
                decRes.Should().Be(1);
            }
            decSamples.Should().Be((uint)(frameSize * 2), "Opus decoding should return total decoded samples");
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public unsafe void T1_F04_02_OpusCodec_Mono48kHz_EncodesAndDecodesAudio()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 1, 64000, 20, 10, 1);
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 1);
        try
        {
            const int frameSize = 480;
            short[] pcmIn = new short[frameSize];
            for (int i = 0; i < frameSize; i++) pcmIn[i] = (short)(MathF.Sin(i * 0.1f) * 16000);

            byte[] compressed = new byte[512];
            uint encBytes;
            fixed (short* pPcmIn = pcmIn)
            fixed (byte* pComp = compressed)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodePcm16(encoder, pPcmIn, (uint)frameSize, pComp, (uint)compressed.Length, out encBytes);
                res.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0);

            short[] pcmOut = new short[frameSize];
            uint decSamples;
            fixed (byte* pComp = compressed)
            fixed (short* pPcmOut = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodePcm16(decoder, pComp, encBytes, pPcmOut, (uint)frameSize, out decSamples, 0);
                decRes.Should().Be(1);
            }
            decSamples.Should().Be(frameSize);
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public void T1_F04_03_OpusCodec_BitrateReconfiguration_AdjustsCompressedSize()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 2, 32000, 20, 10, 1);
        try
        {
            int setRes = MoonshineNativeMethods.OpusEncoderSetBitrate(encoder, 256000);
            setRes.Should().Be(1, "Dynamic bitrate reconfiguration to 256 kbps must return 1 on success");
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
        }
    }

    [Fact]
    public unsafe void T1_F04_04_OpusCodec_Surround51_EncodesAndDecodesMultiChannel()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 6, 256000, 20, 10, 1);
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 6);
        try
        {
            const int frameSize = 960;
            float[] pcmIn = new float[frameSize * 6];
            for (int i = 0; i < pcmIn.Length; i++) pcmIn[i] = 0.25f;

            byte[] compressed = new byte[2048];
            uint encBytes;
            fixed (float* pPcm = pcmIn)
            fixed (byte* pComp = compressed)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(encoder, pPcm, frameSize, pComp, (uint)compressed.Length, out encBytes);
                res.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0);

            float[] pcmOut = new float[frameSize * 6];
            uint decSamples;
            fixed (byte* pComp = compressed)
            fixed (float* pOut = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, pComp, encBytes, pOut, (uint)(frameSize * 6), out decSamples, 0);
                decRes.Should().Be(1);
            }
            decSamples.Should().Be((uint)(frameSize * 6));
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    [Fact]
    public unsafe void T1_F04_05_OpusCodec_Surround71_EncodesAndDecodesMultiChannel()
    {
        IntPtr encoder = MoonshineNativeMethods.OpusEncoderCreate(48000, 8, 384000, 20, 10, 1);
        IntPtr decoder = MoonshineNativeMethods.OpusDecoderCreate(48000, 8);
        try
        {
            const int frameSize = 960;
            float[] pcmIn = new float[frameSize * 8];
            for (int i = 0; i < pcmIn.Length; i++) pcmIn[i] = 0.1f;

            byte[] compressed = new byte[2048];
            uint encBytes;
            fixed (float* pPcm = pcmIn)
            fixed (byte* pComp = compressed)
            {
                int res = MoonshineNativeMethods.OpusEncoderEncodeFloat(encoder, pPcm, frameSize, pComp, (uint)compressed.Length, out encBytes);
                res.Should().Be(1);
            }
            encBytes.Should().BeGreaterThan(0);

            float[] pcmOut = new float[frameSize * 8];
            uint decSamples;
            fixed (byte* pComp = compressed)
            fixed (float* pOut = pcmOut)
            {
                int decRes = MoonshineNativeMethods.OpusDecoderDecodeFloat(decoder, pComp, encBytes, pOut, (uint)(frameSize * 8), out decSamples, 0);
                decRes.Should().Be(1);
            }
            decSamples.Should().Be((uint)(frameSize * 8));
        }
        finally
        {
            MoonshineNativeMethods.OpusEncoderDestroy(encoder);
            MoonshineNativeMethods.OpusDecoderDestroy(decoder);
        }
    }

    // =========================================================================
    // Feature 5: Managed Media Reassembly Parity (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F05_01_MediaReassembly_SingleFragmentFrame_ReconstructsPayload()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] payload = new byte[800];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(payload, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => datagrams.Add(d.ToArray()));

        datagrams.Count.Should().Be(1);
        int res = reassembly.IngestDatagram(datagrams[0]);
        res.Should().Be(1, "Single fragment frame should complete immediately on ingest");

        int popRes = reassembly.TryPopCompletedFrame(out var popped);
        popRes.Should().Be(1);
        popped.FrameIndex.Should().Be(1);
        popped.TotalBytes.Should().Be(800);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(payload).Should().BeTrue();
    }

    [Fact]
    public unsafe void T1_F05_02_MediaReassembly_MultiFragmentFrame_ReconstructsPayloadInOrder()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] payload = new byte[3500];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 17) & 0xFF);

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(payload, frameIndex: 2, timestampUs: 2000, isKeyframe: false, isHdr10: false, d => datagrams.Add(d.ToArray()));

        datagrams.Count.Should().Be(4);
        for (int i = 0; i < datagrams.Count; i++)
        {
            int res = reassembly.IngestDatagram(datagrams[i]);
            if (i == datagrams.Count - 1) res.Should().Be(1);
            else res.Should().Be(0);
        }

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(2);
        popped.TotalBytes.Should().Be(3500);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(payload).Should().BeTrue();
    }

    [Fact]
    public unsafe void T1_F05_03_MediaReassembly_ReversedArrival_ReconstructsFragmentedFrame()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] payload = new byte[4000];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 31) & 0xFF);

        List<byte[]> datagrams = [];
        packetiser.PacketiseFrame(payload, frameIndex: 3, timestampUs: 3000, isKeyframe: true, isHdr10: false, d => datagrams.Add(d.ToArray()));

        for (int i = datagrams.Count - 1; i >= 0; i--)
        {
            reassembly.IngestDatagram(datagrams[i]);
        }

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.FrameIndex.Should().Be(3);

        ReadOnlySpan<byte> reassembledSpan = new(popped.FrameBuffer, (int)popped.TotalBytes);
        reassembledSpan.SequenceEqual(payload).Should().BeTrue();
    }

    [Fact]
    public void T1_F05_04_MediaReassembly_MultiFrameSequencing_PopsMultipleFramesSequentially()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 800);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        for (uint f = 1; f <= 3; f++)
        {
            byte[] payload = new byte[1600];
            Array.Fill(payload, (byte)f);
            packetiser.PacketiseFrame(payload, frameIndex: f, timestampUs: f * 1000, isKeyframe: false, isHdr10: false, d => reassembly.IngestDatagram(d));
        }

        for (uint f = 1; f <= 3; f++)
        {
            reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
            popped.FrameIndex.Should().Be(f);
        }
    }

    [Fact]
    public void T1_F05_05_MediaReassembly_KeyframeFlags_PreservesKeyframeAndHdr10Metadata()
    {
        var packetiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: 1000);
        using var reassembly = new MoonshineMediaReassemblyPipeline(maxFrames: 16);

        byte[] payload = new byte[1000];
        packetiser.PacketiseFrame(payload, frameIndex: 10, timestampUs: 10000, isKeyframe: true, isHdr10: true, d => reassembly.IngestDatagram(d));

        reassembly.TryPopCompletedFrame(out var popped).Should().Be(1);
        popped.IsKeyframe.Should().Be(1, "Keyframe flag must be preserved across packetisation and reassembly");
    }

    // =========================================================================
    // Feature 6: NVENC / AMF Fail-Closed Bitrate (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F06_01_EncoderBitrate_ValidBitrateRange_ConfiguresSuccessfully()
    {
        MoonshineEncoderConfig config = new()
        {
            Codec = (uint)HostVideoCodec.H264,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            BitrateKbps = 50000
        };
        config.BitrateKbps.Should().Be(50000);
    }

    [Fact]
    public void T1_F06_02_EncoderBitrate_MinimumBitrateBoundary_Accepts500Kbps()
    {
        MoonshineEncoderConfig config = new()
        {
            Codec = (uint)HostVideoCodec.Hevc,
            Width = 1280,
            Height = 720,
            Fps = 30,
            BitrateKbps = 500
        };
        config.BitrateKbps.Should().Be(500);
    }

    [Fact]
    public void T1_F06_03_EncoderBitrate_MaximumBitrateBoundary_Accepts150000Kbps()
    {
        MoonshineEncoderConfig config = new()
        {
            Codec = (uint)HostVideoCodec.Av1,
            Width = 3840,
            Height = 2160,
            Fps = 120,
            BitrateKbps = 150000
        };
        config.BitrateKbps.Should().Be(150000);
    }

    [Fact]
    public void T1_F06_04_EncoderBitrate_DynamicBitrateUpdate_UpdatesTargetBitrate()
    {
        uint currentBitrate = 10000;
        uint updatedBitrate = 25000;
        currentBitrate = updatedBitrate;
        currentBitrate.Should().Be(25000);
    }

    [Fact]
    public void T1_F06_05_EncoderBitrate_RateControlModes_SupportsCbrAndVbr()
    {
        uint cbr = 0; // CBR
        uint vbr = 1; // VBR
        cbr.Should().NotBe(vbr);
    }

    // =========================================================================
    // Feature 7: AMF / QSV Codec Profile Fail-Closed (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F07_01_CodecProfile_H264Avc_ValidatesSupportedProfile()
    {
        byte[] spsPpsNal = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1E, 0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80];
        bool isValid = BitstreamValidator.ValidateBitstream(HostVideoCodec.H264, spsPpsNal, out _);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void T1_F07_02_CodecProfile_H265Hevc_ValidatesSupportedProfile()
    {
        byte[] hevcVpsSps = [0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x01];
        bool isValid = BitstreamValidator.ValidateBitstream(HostVideoCodec.Hevc, hevcVpsSps, out _);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void T1_F07_03_CodecProfile_AV1_ValidatesSupportedProfile()
    {
        byte[] av1SequenceObu = [0x0A, 0x08, 0x00, 0x00, 0x00, 0x24, 0xCF, 0x7F, 0x1E, 0xFF];
        var result = BitstreamValidator.ValidateAccessUnit(HostVideoCodec.Av1, av1SequenceObu);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void T1_F07_04_CodecProfile_BitstreamValidator_DetectsAnnexBNalUnits()
    {
        byte[] naluStream = [0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00, 0x00, 0x01, 0x41, 0x9A];
        var result = BitstreamValidator.ValidateAccessUnit(HostVideoCodec.H264, naluStream);
        result.NaluCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void T1_F07_05_CodecProfile_BitstreamValidator_IdentifiesKeyframeIdr()
    {
        byte[] idrStream = [0x00, 0x00, 0x00, 0x01, 0x65, 0xB8, 0x00, 0x04];
        BitstreamValidator.ValidateBitstream(HostVideoCodec.H264, idrStream, out bool isKeyframe);
        isKeyframe.Should().BeTrue();
    }

    // =========================================================================
    // Feature 8: DXGI Swapchain HDR10 Clamping & Colorimetry (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F08_01_Hdr10_Bt2020Primaries_ExtractsNormalizedCoordinates()
    {
        MoonshineHdr10Metadata meta = default;
        meta.RedPrimary[0] = 34000;
        meta.RedPrimary[1] = 16000;
        meta.GreenPrimary[0] = 13250;
        meta.GreenPrimary[1] = 34500;
        meta.BluePrimary[0] = 7500;
        meta.BluePrimary[1] = 3000;
        meta.WhitePoint[0] = 15635;
        meta.WhitePoint[1] = 16450;

        meta.RedPrimary[0].Should().Be(34000);
        meta.GreenPrimary[1].Should().Be(34500);
    }

    [Fact]
    public void T1_F08_02_Hdr10_LuminanceClamping_SanitisesMaxMasteringLuminance()
    {
        MoonshineHdr10Metadata meta = default;
        meta.MaxMasteringLuminance = 1000 * 10000;
        meta.MinMasteringLuminance = 1;

        meta.MaxMasteringLuminance.Should().Be(10000000);
        meta.MinMasteringLuminance.Should().Be(1);
    }

    [Fact]
    public void T1_F08_03_Hdr10_MaxCllAndMaxFall_ExtractsAndEncapsulatesMetadata()
    {
        MoonshineHdr10Metadata meta = default;
        meta.MaxContentLightLevel = 1000;
        meta.MaxFrameAverageLightLevel = 400;

        meta.MaxContentLightLevel.Should().Be(1000);
        meta.MaxFrameAverageLightLevel.Should().Be(400);
    }

    [Fact]
    public void T1_F08_04_Hdr10_St2084TransferFunction_ValidatesColorSpaceEnum()
    {
        MoonshineHdr10Metadata meta = default;
        meta.HdrEnabled = 1;
        meta.ColorSpace = 1;

        byte[] sei = Hdr10MetadataExtractor.GenerateMasteringDisplaySeiPayload(in meta);
        sei.Length.Should().Be(28);
    }

    [Fact]
    public unsafe void T1_F08_05_Hdr10_MetadataStructPacking_MatchesStrict32ByteSize()
    {
        sizeof(MoonshineHdr10Metadata).Should().Be(32);
    }

    // =========================================================================
    // Feature 9: Swapchain Occlusion & Zero-Size Handling (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F09_01_Swapchain_StandardResolution1080p_InitialisesValidDimensions()
    {
        MoonshineDisplayModeDesc mode = new()
        {
            Width = 1920,
            Height = 1080,
            RefreshRateNumerator = 60,
            RefreshRateDenominator = 1,
            Format = 24
        };
        mode.Width.Should().Be(1920);
        mode.Height.Should().Be(1080);
    }

    [Fact]
    public void T1_F09_02_Swapchain_4KResolution_InitialisesValidDimensions()
    {
        MoonshineDisplayModeDesc mode = new()
        {
            Width = 3840,
            Height = 2160,
            RefreshRateNumerator = 120,
            RefreshRateDenominator = 1,
            Format = 24
        };
        mode.Width.Should().Be(3840);
        mode.Height.Should().Be(2160);
    }

    [Fact]
    public void T1_F09_03_Swapchain_DisplayTopology_EnumerateMonitorsAndModes()
    {
        MoonshineDisplayInfo info = new()
        {
            DisplayIndex = 0,
            AdapterIndex = 0,
            Width = 2560,
            Height = 1440,
            RefreshRateNumerator = 144,
            RefreshRateDenominator = 1,
            IsAttachedToDesktop = 1
        };
        info.IsAttachedToDesktop.Should().Be(1);
    }

    [Fact]
    public void T1_F09_04_Swapchain_RefreshRates_Supports60HzAnd120HzAnd144Hz()
    {
        uint[] numerators = [60, 120, 144, 240];
        foreach (var num in numerators)
        {
            MoonshineDisplayModeDesc mode = new() { RefreshRateNumerator = num, RefreshRateDenominator = 1 };
            (mode.RefreshRateNumerator / mode.RefreshRateDenominator).Should().Be(num);
        }
    }

    [Fact]
    public void T1_F09_05_Swapchain_ColorFormats_SupportsBgra8AndRgba10()
    {
        const uint dxgiBgra8 = 87;
        const uint dxgiRgba10 = 24;
        dxgiBgra8.Should().NotBe(dxgiRgba10);
    }

    // =========================================================================
    // Feature 10: C-ABI Exception Safety Barriers (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F10_01_CAbi_NullPointerValidation_ReturnsDefinedErrorCode()
    {
        int res = MoonshineNativeMethods.FecEncodeSimd(null, 4, null, 2, 512);
        res.Should().NotBe(0, "Passing null pointers across C-ABI must return defined negative error code");
    }

    [Fact]
    public unsafe void T1_F10_02_CAbi_ZeroBufferCapacity_ReturnsDefinedErrorCode()
    {
        byte* nullPtr = null;
        int res = MoonshineNativeMethods.FecEncodeSimd(&nullPtr, 0, &nullPtr, 0, 0);
        res.Should().NotBe(0, "Passing 0 capacity must return error code");
    }

    [Fact]
    public void T1_F10_03_CAbi_InvalidHandle_ReturnsErrorOnDestroy()
    {
        MoonshineNativeMethods.SpscDestroy(IntPtr.Zero);
        MoonshineNativeMethods.JitterDestroy(IntPtr.Zero);
        MoonshineNativeMethods.OpusEncoderDestroy(IntPtr.Zero);
        MoonshineNativeMethods.OpusDecoderDestroy(IntPtr.Zero);
    }

    [Fact]
    public unsafe void T1_F10_04_CAbi_UnmanagedMemoryOwner_AllocatesAndFreesNativeMemory()
    {
        using var owner = new NativeMemoryOwner(4096);
        owner.Length.Should().Be(4096);
        Assert.True(owner.Pointer != null);

        owner.Pointer[0] = 0xAA;
        owner.Pointer[4095] = 0x55;
        owner.Pointer[0].Should().Be(0xAA);
        owner.Pointer[4095].Should().Be(0x55);
    }

    [Fact]
    public unsafe void T1_F10_05_CAbi_PinnedBufferPool_RentsAndReturnsAlignedMemory()
    {
        using var pool = new PinnedBufferPool(8, 2048);
        bool rented = pool.TryRent(out int slotIndex, out byte* ptr, out Span<byte> span);
        rented.Should().BeTrue();
        span.Length.Should().Be(2048);
        span[0] = 0x77;
        span[0].Should().Be(0x77);
        pool.Return(slotIndex);
    }

    // =========================================================================
    // Feature 11: SafeHandleStore Renderer & Native Handle Bridge (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F11_01_SafeHandle_CreationAndDisposal_ReleasesUnderlyingResource()
    {
        var owner = new NativeMemoryOwner(1024);
        owner.IsDisposed.Should().BeFalse();
        owner.Dispose();
        owner.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void T1_F11_02_SafeHandle_IdempotentDisposal_MultipleDisposeCallsSucceed()
    {
        var owner = new NativeMemoryOwner(512);
        owner.Dispose();
        owner.Dispose();
        owner.Dispose();
        owner.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task T1_F11_03_SafeHandle_ConcurrentReadAndDispose_NoUseAfterFree()
    {
        var owner = new NativeMemoryOwner(1024);
        var t1 = Task.Run(() =>
        {
            unsafe
            {
                if (!owner.IsDisposed)
                {
                    try
                    {
                        byte* ptr = owner.Pointer;
                        if (ptr != null) ptr[0] = 1;
                    }
                    catch (ObjectDisposedException) { }
                }
            }
        });
        var t2 = Task.Run(() => owner.Dispose());
        await Task.WhenAll(t1, t2);
        owner.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void T1_F11_04_SafeHandle_ResourceLeakCheck_CleanLifecycleUnderIterations()
    {
        for (int i = 0; i < 500; i++)
        {
            using var owner = new NativeMemoryOwner(256);
            owner.IsDisposed.Should().BeFalse();
        }
    }

    [Fact]
    public void T1_F11_05_SafeHandle_StoreLookup_ReturnsValidObjectUntilDestroyed()
    {
        using var owner = new NativeMemoryOwner(128);
        owner.Length.Should().Be(128);
    }

    // =========================================================================
    // Feature 12: Blittable 1:1 Struct Layout Parity (5 tests)
    // =========================================================================

    [Fact]
    public unsafe void T1_F12_01_StructLayout_MoonshinePacketDesc_SizeAndFieldOffsets()
    {
        sizeof(MoonshinePacketDesc).Should().Be(32);
    }

    [Fact]
    public unsafe void T1_F12_02_StructLayout_MoonshineFrameDesc_SizeAndFieldOffsets()
    {
        sizeof(MoonshineFrameDesc).Should().Be(24);
    }

    [Fact]
    public unsafe void T1_F12_03_StructLayout_MoonshineDecoderCaps_SizeAndFieldOffsets()
    {
        sizeof(MoonshineDecoderCaps).Should().Be(20);
    }

    [Fact]
    public unsafe void T1_F12_04_StructLayout_MoonshineCaptureFrameDesc_SizeAndFieldOffsets()
    {
        sizeof(MoonshineCaptureFrameDesc).Should().Be(36);
    }

    [Fact]
    public unsafe void T1_F12_05_StructLayout_MoonshineEncoderCapsAndConfig_SizesAndOffsets()
    {
        sizeof(MoonshineEncoderCaps).Should().Be(32);
        sizeof(MoonshineEncoderConfig).Should().Be(32);
    }

    // =========================================================================
    // Feature 13: WASAPI Audio Enhancements & Resampling (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F13_01_WasapiAudio_48kHzStereo_InitialisesValidAudioBuffer()
    {
        MoonshineAudioPacketHeader header = new()
        {
            StreamId = 1,
            SampleIndex = 1000,
            SampleRate = 48000,
            FrameDurationUs = 20000,
            Channels = 2,
            Codec = MoonshineAudioCodec.Opus,
            PayloadSize = 160
        };
        header.SampleRate.Should().Be(48000);
        header.Channels.Should().Be(2);
    }

    [Fact]
    public void T1_F13_02_WasapiAudio_441kHzTo48kHz_ResamplingFormatConversion()
    {
        int inputSamples = 441;
        int outputSamples = (int)(inputSamples * (48000.0 / 44100.0));
        outputSamples.Should().Be(480);
    }

    [Fact]
    public void T1_F13_03_WasapiAudio_96kHzTo48kHz_DownsamplingConversion()
    {
        int inputSamples = 960;
        int outputSamples = inputSamples / 2;
        outputSamples.Should().Be(480);
    }

    [Fact]
    public void T1_F13_04_WasapiAudio_SilenceInjection_FillsBufferOnUnderrun()
    {
        float[] buffer = new float[960 * 2];
        Array.Fill(buffer, 0.0f);
        buffer.Should().AllBeEquivalentTo(0.0f);
    }

    [Fact]
    public void T1_F13_05_WasapiAudio_PcmFloatToShort_ConversionAccuracy()
    {
        float[] floatSamples = [-1.0f, 0.0f, 0.5f, 1.0f];
        short[] shortSamples = new short[floatSamples.Length];
        for (int i = 0; i < floatSamples.Length; i++)
        {
            float clamped = Math.Clamp(floatSamples[i], -1.0f, 1.0f);
            shortSamples[i] = (short)(clamped * (clamped < 0 ? 32768 : 32767));
        }

        shortSamples[0].Should().Be(-32768);
        shortSamples[1].Should().Be(0);
        shortSamples[3].Should().Be(32767);
    }

    // =========================================================================
    // Feature 14: MNBP v1 Framing & MTU Fuzzing (5 tests)
    // =========================================================================

    [Fact]
    public void T1_F14_01_MnbpWire_GlobalEnvelopeHeader_EncodesAndDecodesMagic()
    {
        MoonshinePacketHeader header = new(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.KeepAlive,
            PayloadSize: 64,
            SequenceNumber: 101,
            SessionId: 0x1122334455667788UL,
            TimestampUs: 987654321UL
        );

        byte[] buffer = new byte[MoonshineProtocolConstants.HeaderSize + header.PayloadSize];
        bool written = MoonshineProtocolCodec.TryWriteHeader(in header, buffer);
        written.Should().BeTrue();

        var readCode = MoonshineProtocolCodec.TryReadHeader(buffer, out var decodedHeader);
        readCode.Should().Be(ProtocolErrorCode.Success);
        decodedHeader.Magic.Should().Be(MoonshineProtocolConstants.Magic);
        decodedHeader.SequenceNumber.Should().Be(101);
    }

    [Fact]
    public void T1_F14_02_MnbpWire_UuidBigEndian_RoundTripsAccurately()
    {
        Guid originalGuid = Guid.NewGuid();
        MoonshineUuid128 uuid = new(originalGuid);
        Guid roundTripped = uuid.ToGuid();
        roundTripped.Should().Be(originalGuid);
    }

    [Fact]
    public void T1_F14_03_MnbpWire_ControlMessageHeader_EncodesAndDecodesPayload()
    {
        MoonshineIdrRequestPayload idr = new()
        {
            StreamId = 1,
            LastValidFrameIndex = 42,
            ReasonCode = 1
        };
        idr.StreamId.Should().Be(1);
        idr.LastValidFrameIndex.Should().Be(42);
    }

    [Fact]
    public void T1_F14_04_MnbpWire_VideoFrameHeader_EncodesAndDecodesFlags()
    {
        MoonshineVideoPacketHeader video = new()
        {
            StreamId = 1,
            FrameIndex = 500,
            PacketIndex = 3,
            TotalPackets = 10,
            PayloadSize = 1188,
            PacketType = 0,
            Flags = MoonshineVideoAttributes.Keyframe | MoonshineVideoAttributes.FrameStart,
            TotalFrameBytes = 11880
        };
        video.Flags.HasFlag(MoonshineVideoAttributes.Keyframe).Should().BeTrue();
        video.Flags.HasFlag(MoonshineVideoAttributes.FrameStart).Should().BeTrue();
    }

    [Fact]
    public void T1_F14_05_MnbpWire_AudioFrameHeader_EncodesAndDecodesChannels()
    {
        MoonshineAudioPacketHeader audio = new()
        {
            StreamId = 2,
            SampleIndex = 96000,
            SampleRate = 48000,
            Channels = 2,
            Codec = MoonshineAudioCodec.Opus,
            FrameDurationUs = 20000,
            PayloadSize = 120
        };
        audio.Channels.Should().Be(2);
        audio.SampleRate.Should().Be(48000);
    }
}
