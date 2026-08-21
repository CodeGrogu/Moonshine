using BenchmarkDotNet.Attributes;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class RingBufferBenchmarks
{
    private IntPtr _ringHandle;
    private IntPtr _slotReturnHandle;
    private MoonshinePacketDesc _packet;

    [GlobalSetup]
    public void Setup()
    {
        _ringHandle = MoonshineNativeMethods.SpscCreate(1024);
        _slotReturnHandle = MoonshineNativeMethods.SlotReturnCreate(1024);
        _packet = new MoonshinePacketDesc
        {
            SequenceNumber = 100,
            FrameIndex = 1,
            PacketIndex = 0,
            TotalPackets = 1,
            PayloadSize = 1400,
            PacketType = 0,
            Flags = 0,
            BufferSlotIndex = 42,
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

        if (_slotReturnHandle != IntPtr.Zero)
        {
            MoonshineNativeMethods.SlotReturnDestroy(_slotReturnHandle);
            _slotReturnHandle = IntPtr.Zero;
        }
    }

    [Benchmark]
    public int EnqueueAndDequeue()
    {
        MoonshineNativeMethods.SpscEnqueue(_ringHandle, in _packet);
        return MoonshineNativeMethods.SpscDequeue(_ringHandle, out _);
    }

    [Benchmark]
    public int SlotReturnEnqueueAndDequeue()
    {
        int res = MoonshineNativeMethods.SlotReturnEnqueue(_slotReturnHandle, 42);
        return res & MoonshineNativeMethods.SlotReturnDequeue(_slotReturnHandle, out _);
    }
}
