using BenchmarkDotNet.Attributes;
using Moonshine.Protocol.RTP;

namespace Moonshine.Benchmarks;

[MemoryDiagnoser]
public class RtpParsingBenchmarks
{
    private byte[] _rawPacket = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rawPacket = new byte[1400];
        _rawPacket[0] = 0x80; // V=2
        _rawPacket[1] = 96;   // Dynamic Payload type
        _rawPacket[2] = 0x12; // Seq
        _rawPacket[3] = 0x34;
        _rawPacket[4] = 0x00; // Timestamp
        _rawPacket[5] = 0x01;
        _rawPacket[6] = 0x02;
        _rawPacket[7] = 0x03;
        _rawPacket[8] = 0xDE; // SSRC
        _rawPacket[9] = 0xAD;
        _rawPacket[10] = 0xBE;
        _rawPacket[11] = 0xEF;
    }

    [Benchmark(Baseline = true)]
    public (ushort seq, uint ts, int payloadLen) ClassicByteParsing()
    {
        ushort seq = (ushort)((_rawPacket[2] << 8) | _rawPacket[3]);
        uint ts = ((uint)_rawPacket[4] << 24) | ((uint)_rawPacket[5] << 16) | ((uint)_rawPacket[6] << 8) | _rawPacket[7];
        byte[] payload = new byte[_rawPacket.Length - 12];
        Array.Copy(_rawPacket, 12, payload, 0, payload.Length);
        return (seq, ts, payload.Length);
    }

    [Benchmark]
    public (ushort seq, uint ts, int payloadLen) ZeroAllocSpanParsing()
    {
        if (RtpHeader.TryParse(_rawPacket, out var header, out var payload))
        {
            return (header.SequenceNumber, header.Timestamp, payload.Length);
        }
        return default;
    }
}
