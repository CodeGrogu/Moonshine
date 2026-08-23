#if MOONSHINE_LEGACY_INTEROP
using BenchmarkDotNet.Attributes;
using Moonshine.Core.Pipelines;
using Moonshine.Interop;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public unsafe class UdpIngestionBenchmarks : IDisposable
{
    private byte[] _rawRtpPacket = null!;
    private UdpSocketPipeline _pipeline = null!;
    private PinnedBufferPool _pool = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pool = new PinnedBufferPool(2048, 2048);
        _pipeline = new UdpSocketPipeline(0);

        _rawRtpPacket = new byte[1400];
        _rawRtpPacket[0] = 0x80; // V=2
        _rawRtpPacket[1] = 98;   // HEVC Payload type
        _rawRtpPacket[2] = 0x12; // Seq
        _rawRtpPacket[3] = 0x34;
        _rawRtpPacket[4] = 0x00; // Timestamp
        _rawRtpPacket[5] = 0x01;
        _rawRtpPacket[6] = 0x02;
        _rawRtpPacket[7] = 0x03;
        _rawRtpPacket[8] = 0xDE; // SSRC
        _rawRtpPacket[9] = 0xAD;
        _rawRtpPacket[10] = 0xBE;
        _rawRtpPacket[11] = 0xEF;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    [Benchmark(Baseline = true)]
    public int PinnedBufferRentAndReturn()
    {
        if (_pool.TryRent(out int slot, out _, out _))
        {
            _pool.Return(slot);
            return slot;
        }
        return -1;
    }

    [Benchmark]
    public void ProcessUdpDatagramZeroAlloc()
    {
        _pipeline.ProcessDatagram(_rawRtpPacket);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _pool?.Dispose();
        GC.SuppressFinalize(this);
    }
}
#endif
