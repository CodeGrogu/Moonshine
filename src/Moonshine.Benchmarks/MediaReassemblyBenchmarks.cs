using BenchmarkDotNet.Attributes;
using Moonshine.Core.Media;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class MediaReassemblyBenchmarks : IDisposable
{
    private MoonshineVideoPacketiser _losslessPacketiser = null!;
    private MoonshineVideoPacketiser _fecSingleBlockPacketiser = null!;
    private MoonshineVideoPacketiser _fecMultiBlockPacketiser = null!;

    private MoonshineMediaReassemblyPipeline _losslessPipeline = null!;
    private MoonshineMediaReassemblyPipeline _fecSingleBlockPipeline = null!;
    private MoonshineMediaReassemblyPipeline _fecMultiBlockPipeline = null!;

    private byte[][] _losslessPackets = null!;
    private byte[][] _fecSingleBlockPackets = null!;
    private byte[][] _fecMultiBlockPackets = null!;

    [GlobalSetup]
    public void Setup()
    {
        int mtu = 1188;
        _losslessPacketiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu);
        _fecSingleBlockPacketiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: 4, fecParityShards: 2);
        _fecMultiBlockPacketiser = new MoonshineVideoPacketiser(streamId: 1, sessionId: 100, mtuPayloadSize: mtu, fecDataShards: 4, fecParityShards: 2);

        _losslessPipeline = new MoonshineMediaReassemblyPipeline(maxFrames: 16, mtuPayloadSize: mtu);
        _fecSingleBlockPipeline = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: 4, fecParityShards: 2, mtuPayloadSize: mtu);
        _fecMultiBlockPipeline = new MoonshineMediaReassemblyPipeline(maxFrames: 16, fecDataShards: 4, fecParityShards: 2, mtuPayloadSize: mtu);

        // Lossless frame: 4 packets (4000 bytes)
        byte[] payload4k = new byte[4000];
        List<byte[]> lPackets = new();
        _losslessPacketiser.PacketiseFrame(payload4k, frameIndex: 1, timestampUs: 1000, isKeyframe: true, isHdr10: false, d => lPackets.Add(d.ToArray()));
        _losslessPackets = lPackets.ToArray();

        // Single block FEC frame: 4 data + 2 parity (drop data 1 and 3, feed data 0, 2, parity 0, 1)
        List<byte[]> sbPackets = new();
        _fecSingleBlockPacketiser.PacketiseFrame(payload4k, frameIndex: 2, timestampUs: 2000, isKeyframe: true, isHdr10: false, d => sbPackets.Add(d.ToArray()));
        _fecSingleBlockPackets = [sbPackets[0], sbPackets[2], sbPackets[4], sbPackets[5]];

        // Multi-block FEC frame: 8 data + 4 parity (8000 bytes). Drop data 1 from block 0 and data 6 from block 1
        byte[] payload8k = new byte[8000];
        List<byte[]> mbPackets = new();
        _fecMultiBlockPacketiser.PacketiseFrame(payload8k, frameIndex: 3, timestampUs: 3000, isKeyframe: true, isHdr10: false, d => mbPackets.Add(d.ToArray()));
        _fecMultiBlockPackets = [
            mbPackets[0], mbPackets[2], mbPackets[3], mbPackets[8],
            mbPackets[4], mbPackets[5], mbPackets[7], mbPackets[10]
        ];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _losslessPipeline?.Dispose();
        _fecSingleBlockPipeline?.Dispose();
        _fecMultiBlockPipeline?.Dispose();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public int IngestLosslessFrame_HotPath()
    {
        int res = 0;
        for (int i = 0; i < _losslessPackets.Length; i++)
        {
            res = _losslessPipeline.IngestDatagram(_losslessPackets[i]);
        }
        _losslessPipeline.TryPopCompletedFrame(out _);
        return res;
    }

    [Benchmark]
    public int IngestWithFecRecovery_SingleBlock()
    {
        int res = 0;
        for (int i = 0; i < _fecSingleBlockPackets.Length; i++)
        {
            res = _fecSingleBlockPipeline.IngestDatagram(_fecSingleBlockPackets[i]);
        }
        _fecSingleBlockPipeline.TryPopCompletedFrame(out _);
        return res;
    }

    [Benchmark]
    public int IngestWithFecRecovery_MultiBlock()
    {
        int res = 0;
        for (int i = 0; i < _fecMultiBlockPackets.Length; i++)
        {
            res = _fecMultiBlockPipeline.IngestDatagram(_fecMultiBlockPackets[i]);
        }
        _fecMultiBlockPipeline.TryPopCompletedFrame(out _);
        return res;
    }
}
