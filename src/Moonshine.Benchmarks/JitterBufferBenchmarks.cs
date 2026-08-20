using BenchmarkDotNet.Attributes;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class JitterBufferBenchmarks
{
    private IntPtr _jitterHandle;
    private MoonshinePacketDesc _packet1;
    private MoonshinePacketDesc _packet2;

    [GlobalSetup]
    public void Setup()
    {
        _jitterHandle = MoonshineNativeMethods.JitterCreate(64);
        _packet1 = new MoonshinePacketDesc
        {
            SequenceNumber = 100,
            FrameIndex = 1,
            PacketIndex = 0,
            TotalPackets = 2,
            PayloadSize = 1400,
            PacketType = 0,
            Flags = 0,
            PayloadPtr = null
        };
        _packet2 = new MoonshinePacketDesc
        {
            SequenceNumber = 101,
            FrameIndex = 1,
            PacketIndex = 1,
            TotalPackets = 2,
            PayloadSize = 1400,
            PacketType = 0,
            Flags = 1, // End of frame
            PayloadPtr = null
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_jitterHandle != IntPtr.Zero)
        {
            MoonshineNativeMethods.JitterDestroy(_jitterHandle);
            _jitterHandle = IntPtr.Zero;
        }
    }

    [Benchmark]
    public int AssembleAndPopFrame()
    {
        MoonshineNativeMethods.JitterPushPacket(_jitterHandle, in _packet1);
        MoonshineNativeMethods.JitterPushPacket(_jitterHandle, in _packet2);
        return MoonshineNativeMethods.JitterPopFrame(_jitterHandle, out _);
    }
}
