using BenchmarkDotNet.Attributes;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class RingBufferBenchmarks
{
    private IntPtr _ringHandle;
    private MoonshinePacketDesc _packet;

    [GlobalSetup]
    public void Setup()
    {
        _ringHandle = MoonshineNativeMethods.SpscCreate(1024);
        _packet = new MoonshinePacketDesc
        {
            SequenceNumber = 100,
            FrameIndex = 1,
            PacketIndex = 0,
            TotalPackets = 1,
            PayloadSize = 1400,
            PacketType = 0,
            Flags = 0,
            PayloadPtr = null
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_ringHandle != IntPtr.Zero)
        {
            MoonshineNativeMethods.SpscDestroy(_ringHandle);
            _ringHandle = IntPtr.Zero;
        }
    }

    [Benchmark]
    public int EnqueueAndDequeue()
    {
        MoonshineNativeMethods.SpscEnqueue(_ringHandle, in _packet);
        return MoonshineNativeMethods.SpscDequeue(_ringHandle, out _);
    }
}
