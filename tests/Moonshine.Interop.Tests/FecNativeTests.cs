using FluentAssertions;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Interop.Tests;

public class FecNativeTests
{
    [Fact]
    public void FecGetSimdArchitecture_ReturnsValidInstructionSet()
    {
        uint arch = MoonshineNativeMethods.FecGetSimdArchitecture();
        // 0: Scalar, 1: AVX2, 2: AVX512, 3: GFNI + AVX512
        arch.Should().BeInRange(0, 3);
    }

    [Fact]
    public unsafe void FecRecoverSimd_SingleLostShard_RecoversExactData()
    {
        const int shardCount = 4;
        const int shardSize = 1024;

        byte[][] shards = new byte[shardCount][];
        for (int i = 0; i < shardCount - 1; i++)
        {
            shards[i] = new byte[shardSize];
            Array.Fill(shards[i], (byte)(i + 7));
        }
        shards[shardCount - 1] = new byte[shardSize]; // Parity

        // Compute XOR parity
        for (int i = 0; i < shardCount - 1; i++)
        {
            for (int b = 0; b < shardSize; b++)
            {
                shards[shardCount - 1][b] ^= shards[i][b];
            }
        }

        // Simulate loss of shard 1
        byte[] originalShard1 = (byte[])shards[1].Clone();
        Array.Clear(shards[1]);

        int[] erased = [1];

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2], s3 = shards[3])
        fixed (int* erasedPtr = erased)
        {
            byte** shardPtrs = stackalloc byte*[shardCount];
            shardPtrs[0] = s0;
            shardPtrs[1] = s1;
            shardPtrs[2] = s2;
            shardPtrs[3] = s3;

            int res = MoonshineNativeMethods.FecRecoverSimd(shardPtrs, shardCount, shardSize, erasedPtr, 1);
            res.Should().Be(0);
        }

        shards[1].Should().Equal(originalShard1);
    }

    [Fact]
    public unsafe void FecEncodeAndReconstruct_MultiShardCauchy_RestoresExactPayload()
    {
        const int dataCount = 10;
        const int parityCount = 2;
        const int totalCount = dataCount + parityCount;
        const int shardSize = 1400;

        byte[][] shards = new byte[totalCount][];
        byte[][] backup = new byte[totalCount][];

        for (int i = 0; i < dataCount; i++)
        {
            shards[i] = new byte[shardSize];
            for (int b = 0; b < shardSize; b++)
            {
                shards[i][b] = (byte)((i + 1) * 19 + b * 5);
            }
            backup[i] = (byte[])shards[i].Clone();
        }

        for (int p = 0; p < parityCount; p++)
        {
            shards[dataCount + p] = new byte[shardSize];
        }

        fixed (byte* s0 = shards[0], s1 = shards[1], s2 = shards[2], s3 = shards[3], s4 = shards[4],
                     s5 = shards[5], s6 = shards[6], s7 = shards[7], s8 = shards[8], s9 = shards[9],
                     p0 = shards[10], p1 = shards[11])
        {
            byte** dataPtrs = stackalloc byte*[dataCount];
            dataPtrs[0] = s0; dataPtrs[1] = s1; dataPtrs[2] = s2; dataPtrs[3] = s3; dataPtrs[4] = s4;
            dataPtrs[5] = s5; dataPtrs[6] = s6; dataPtrs[7] = s7; dataPtrs[8] = s8; dataPtrs[9] = s9;

            byte** parityPtrs = stackalloc byte*[parityCount];
            parityPtrs[0] = p0; parityPtrs[1] = p1;

            int encodeRes = MoonshineNativeMethods.FecEncodeSimd(dataPtrs, dataCount, parityPtrs, parityCount, shardSize);
            encodeRes.Should().Be(0);

            backup[10] = (byte[])shards[10].Clone();
            backup[11] = (byte[])shards[11].Clone();

            // Erase shard 1 (data) and shard 11 (parity)
            Array.Clear(shards[1]);
            Array.Clear(shards[11]);

            int[] erased = [1, 11];
            fixed (int* erasedPtr = erased)
            {
                byte** allPtrs = stackalloc byte*[totalCount];
                for (int i = 0; i < dataCount; i++) allPtrs[i] = dataPtrs[i];
                allPtrs[10] = p0; allPtrs[11] = p1;

                int recoverRes = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, erasedPtr, erased.Length);
                recoverRes.Should().Be(0);
            }
        }

        shards[1].Should().Equal(backup[1]);
        shards[11].Should().Equal(backup[11]);
    }

    [Fact]
    public unsafe void FecReconstructSimd_TooManyErasures_ReturnsError()
    {
        const int dataCount = 5;
        const int parityCount = 1;
        const int shardSize = 512;

        byte*[] ptrs = new byte*[6];
        byte[] buffer = new byte[shardSize];
        fixed (byte* b = buffer)
        {
            for (int i = 0; i < 6; i++) ptrs[i] = b;
            int[] erased = [0, 1]; // 2 erasures when M = 1
            fixed (byte** allPtrs = ptrs)
            fixed (int* erasedPtr = erased)
            {
                int res = MoonshineNativeMethods.FecReconstructSimd(allPtrs, dataCount, parityCount, shardSize, erasedPtr, erased.Length);
                res.Should().Be(-2);
            }
        }
    }
}
