using BenchmarkDotNet.Attributes;
using Moonshine.Core.Media;
using Moonshine.Host.Session;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class SessionBenchmarks
{
    private MoonshineVideoPacketiser _packetiser = null!;
    private byte[] _testFrame = null!;
    private VideoPacketSink _sink = null!;
    private ulong _sinkPacketCount;

    [GlobalSetup]
    public void Setup()
    {
        _packetiser = new MoonshineVideoPacketiser(
            streamId: 1,
            sessionId: 1001,
            mtuPayloadSize: 1188,
            fecDataShards: 10,
            fecParityShards: 2);

        // 64 KB typical 4K HEVC/AV1 P-frame
        _testFrame = new byte[64 * 1024];
        new Random(42).NextBytes(_testFrame);

        _sink = span =>
        {
            _sinkPacketCount += (ulong)span.Length;
        };
    }

    [Benchmark]
    public int Session_VideoFramePacketise_DirectHotPath()
    {
        return _packetiser.PacketiseFrame(
            _testFrame,
            frameIndex: 120,
            timestampUs: 2000000,
            isKeyframe: false,
            isHdr10: true,
            sink: _sink);
    }
}
