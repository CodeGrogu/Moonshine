using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public unsafe class FecMatrixBenchmarks
{
    private const int ShardSize = 1400;

    private byte[][] _shards10 = null!;
    private byte*[] _shardPointers10 = null!;
    private GCHandle[] _handles10 = null!;
    private int[] _erasedIndices2 = null!;

    private byte[][] _shards20 = null!;
    private byte*[] _shardPointers20 = null!;
    private GCHandle[] _handles20 = null!;
    private int[] _erasedIndices4 = null!;

    private byte[][] _shards40 = null!;
    private byte*[] _shardPointers40 = null!;
    private GCHandle[] _handles40 = null!;
    private int[] _erasedIndices8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 10 + 2 Matrix
        _shards10 = new byte[12][];
        _shardPointers10 = new byte*[12];
        _handles10 = new GCHandle[12];
        for (int i = 0; i < 12; i++)
        {
            _shards10[i] = new byte[ShardSize];
            Array.Fill(_shards10[i], (byte)(i + 1));
            _handles10[i] = GCHandle.Alloc(_shards10[i], GCHandleType.Pinned);
            _shardPointers10[i] = (byte*)_handles10[i].AddrOfPinnedObject();
        }
        _erasedIndices2 = [1, 3];

        // 20 + 4 Matrix
        _shards20 = new byte[24][];
        _shardPointers20 = new byte*[24];
        _handles20 = new GCHandle[24];
        for (int i = 0; i < 24; i++)
        {
            _shards20[i] = new byte[ShardSize];
            Array.Fill(_shards20[i], (byte)(i + 1));
            _handles20[i] = GCHandle.Alloc(_shards20[i], GCHandleType.Pinned);
            _shardPointers20[i] = (byte*)_handles20[i].AddrOfPinnedObject();
        }
        _erasedIndices4 = [2, 5, 8, 12];

        // 40 + 8 Matrix
        _shards40 = new byte[48][];
        _shardPointers40 = new byte*[48];
        _handles40 = new GCHandle[48];
        for (int i = 0; i < 48; i++)
        {
            _shards40[i] = new byte[ShardSize];
            Array.Fill(_shards40[i], (byte)(i + 1));
            _handles40[i] = GCHandle.Alloc(_shards40[i], GCHandleType.Pinned);
            _shardPointers40[i] = (byte*)_handles40[i].AddrOfPinnedObject();
        }
        _erasedIndices8 = [1, 4, 7, 11, 15, 22, 28, 35];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        FreeHandles(_handles10);
        FreeHandles(_handles20);
        FreeHandles(_handles40);
    }

    private static void FreeHandles(GCHandle[] handles)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (handles[i].IsAllocated)
            {
                handles[i].Free();
            }
        }
    }

    [Benchmark(Baseline = true)]
    public int FecRecovery_Matrix_10_2()
    {
        fixed (byte** ptrs = _shardPointers10)
        fixed (int* erased = _erasedIndices2)
        {
            return MoonshineNativeMethods.FecRecoverSimd(ptrs, 12, ShardSize, erased, 2);
        }
    }

    [Benchmark]
    public int FecRecovery_Matrix_20_4()
    {
        fixed (byte** ptrs = _shardPointers20)
        fixed (int* erased = _erasedIndices4)
        {
            return MoonshineNativeMethods.FecRecoverSimd(ptrs, 24, ShardSize, erased, 4);
        }
    }

    [Benchmark]
    public int FecRecovery_Matrix_40_8()
    {
        fixed (byte** ptrs = _shardPointers40)
        fixed (int* erased = _erasedIndices8)
        {
            return MoonshineNativeMethods.FecRecoverSimd(ptrs, 48, ShardSize, erased, 8);
        }
    }
}
