using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public unsafe class FecBenchmarks
{
    private const int ShardCount = 10;
    private const int ShardSize = 1400;
    private byte[][] _shards = null!;
    private byte*[] _shardPointers = null!;
    private GCHandle[] _handles = null!;
    private int[] _erasedIndices = null!;

    [GlobalSetup]
    public void Setup()
    {
        _shards = new byte[ShardCount][];
        _shardPointers = new byte*[ShardCount];
        _handles = new GCHandle[ShardCount];

        for (int i = 0; i < ShardCount; i++)
        {
            _shards[i] = new byte[ShardSize];
            Array.Fill(_shards[i], (byte)(i + 1));
            _handles[i] = GCHandle.Alloc(_shards[i], GCHandleType.Pinned);
            _shardPointers[i] = (byte*)_handles[i].AddrOfPinnedObject();
        }

        _erasedIndices = [1];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        for (int i = 0; i < ShardCount; i++)
        {
            if (_handles[i].IsAllocated)
            {
                _handles[i].Free();
            }
        }
    }

    [Benchmark(Baseline = true)]
    public void ScalarXorRecovery()
    {
        byte* dest = _shardPointers[1];
        byte* src = _shardPointers[0];
        for (int i = 0; i < ShardSize; i++)
        {
            dest[i] ^= src[i];
        }
    }

    [Benchmark]
    public void SimdVectorXor()
    {
        fixed (byte** ptrs = _shardPointers)
        {
            MoonshineNativeMethods.VectorXor(ptrs[1], ptrs[0], (nuint)ShardSize);
        }
    }

    [Benchmark]
    public int SimdReedSolomonFecRecovery()
    {
        fixed (byte** ptrs = _shardPointers)
        fixed (int* erased = _erasedIndices)
        {
            return MoonshineNativeMethods.FecRecoverSimd(ptrs, ShardCount, ShardSize, erased, _erasedIndices.Length);
        }
    }
}
