using BenchmarkDotNet.Attributes;
using Moonshine.Core.Congestion;
using Moonshine.Protocol.RTP;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class CongestionControlBenchmarks
{
    private CongestionController _controller = null!;
    private RtcpLossStatsPacket _lossStats;
    private byte[] _rtcpBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _controller = new CongestionController(initialBitrateKbps: 50000);
        _lossStats = new RtcpLossStatsPacket(
            Ssrc: 0x12345678,
            PacketsReceived: 10000,
            PacketsLost: 50,
            PacketsRecovered: 40,
            LastSequenceNumber: 60000,
            JitterMicros: 200
        );
        _rtcpBuffer = new byte[64];
        _lossStats.WriteTo(_rtcpBuffer);
    }

    [Benchmark(Baseline = true)]
    public int SerializeRtcpLossStats()
    {
        return _lossStats.WriteTo(_rtcpBuffer);
    }

    [Benchmark]
    public bool ParseRtcpLossStats()
    {
        return RtcpLossStatsPacket.TryParse(_rtcpBuffer, out _);
    }

    [Benchmark]
    public void ProcessFeedbackAndAdaptBitrate()
    {
        _controller.ProcessFeedback(_lossStats, rttMs: 8.5);
    }
}
