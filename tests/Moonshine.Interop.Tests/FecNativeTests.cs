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
}
