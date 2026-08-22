using BenchmarkDotNet.Attributes;
using Moonshine.Core.Congestion;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;
using Moonshine.Protocol.RTP;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class CongestionControlBenchmarks
{
    private CongestionController _controller = null!;
    private RtcpLossStatsPacket _lossStats;
    private MoonshineFeedbackLossStatsPayload _moonshineLossStats;
    private MoonshineIdrRequestPayload _idrRequest;
    private byte[] _rtcpBuffer = null!;
    private byte[] _nativeLossStatsBuffer = null!;
    private byte[] _nativeIdrBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _controller = new CongestionController(
            initialBitrateKbps: 50000,
            hysteresisHoldMs: 0);

        _lossStats = new RtcpLossStatsPacket(
            Ssrc: 0x12345678,
            PacketsReceived: 10000,
            PacketsLost: 50,
            PacketsRecovered: 40,
            LastSequenceNumber: 60000,
            JitterMicros: 200
        );

        _moonshineLossStats = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 5000,
            PacketsReceived = 10000,
            PacketsLost = 50,
            PacketsRecoveredFec = 40,
            RoundTripTimeUs = 8500,
            JitterUs = 200,
            EstimatedBandwidthKbps = 55000,
            ReceiveQueueDepth = 2
        };

        _idrRequest = new MoonshineIdrRequestPayload
        {
            StreamId = 1,
            LastValidFrameIndex = 4990,
            ReasonCode = 1
        };

        _rtcpBuffer = new byte[64];
        _lossStats.WriteTo(_rtcpBuffer);

        _nativeLossStatsBuffer = new byte[MoonshineFeedbackCodec.LossStatsPacketSize];
        MoonshineFeedbackCodec.TryWriteLossStats(in _moonshineLossStats, _nativeLossStatsBuffer, out _, sessionId: 0x1234);

        _nativeIdrBuffer = new byte[MoonshineFeedbackCodec.IdrRequestPacketSize];
        MoonshineFeedbackCodec.TryWriteIdrRequest(in _idrRequest, _nativeIdrBuffer, out _, sessionId: 0x1234);
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
    public bool SerializeMoonshineLossStats()
    {
        return MoonshineFeedbackCodec.TryWriteLossStats(
            in _moonshineLossStats,
            _nativeLossStatsBuffer,
            out _,
            sessionId: 0x1234);
    }

    [Benchmark]
    public MoonshineErrorCode ParseMoonshineLossStats()
    {
        return MoonshineFeedbackCodec.TryReadLossStats(
            _nativeLossStatsBuffer,
            out _,
            out _);
    }

    [Benchmark]
    public bool SerializeMoonshineIdrRequest()
    {
        return MoonshineFeedbackCodec.TryWriteIdrRequest(
            in _idrRequest,
            _nativeIdrBuffer,
            out _,
            sessionId: 0x1234);
    }

    [Benchmark]
    public MoonshineErrorCode ParseMoonshineIdrRequest()
    {
        return MoonshineFeedbackCodec.TryReadIdrRequest(
            _nativeIdrBuffer,
            out _,
            out _);
    }

    [Benchmark]
    public void ProcessFeedbackAndAdaptBitrate()
    {
        _controller.ProcessFeedback(in _moonshineLossStats);
    }
}
