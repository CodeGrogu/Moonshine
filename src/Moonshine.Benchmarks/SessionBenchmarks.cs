using System.Buffers.Binary;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Moonshine.Core.Media;
using Moonshine.Host.Session;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Input;
using Moonshine.Protocol.Video;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class SessionBenchmarks
{
    private MoonshineVideoPacketiser _packetiser = null!;
    private byte[] _testFrame = null!;
    private VideoPacketSink _sink = null!;
    private ulong _sinkPacketCount;

    private byte[] _binaryCodecBuffer = null!;
    private MoonshinePacketHeader _header;
    private MoonshineInputKeyboardPayload _kbPayload;
    private MoonshineInputMousePayload _mousePayload;

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

        _binaryCodecBuffer = new byte[256];
        _header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputKeyboard,
            PayloadSize: 12,
            SequenceNumber: 42,
            SessionId: 0x1234567890ABCDEFUL,
            TimestampUs: 12345678UL);

        _kbPayload = new MoonshineInputKeyboardPayload
        {
            KeyCode = 0x57,
            ScanCode = 0x11,
            IsDown = 1,
            Modifiers = 0,
            Reserved = 0,
            TimestampOffsetUs = 0
        };

        _mousePayload = new MoonshineInputMousePayload
        {
            X = 100,
            Y = -50,
            WheelDeltaY = 120,
            WheelDeltaX = 0,
            ButtonFlags = 1,
            IsAbsolute = 1,
            Reserved = 0,
            TimestampOffsetUs = 0
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

    [Benchmark]
    public bool Session_BinaryContract_KeyboardPacketEncoding()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool hOk = MoonshineProtocolCodec.TryWriteHeader(in _header, dest);
        bool pOk = MoonshineProtocolCodec.TryWriteKeyboardInput(in _kbPayload, dest[MoonshineProtocolConstants.HeaderSize..]);
        return hOk && pOk;
    }

    [Benchmark]
    public bool Session_BinaryContract_MousePacketEncoding()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool hOk = MoonshineProtocolCodec.TryWriteHeader(in _header, dest);
        bool pOk = MoonshineProtocolCodec.TryWriteMouseInput(in _mousePayload, dest[MoonshineProtocolConstants.HeaderSize..]);
        return hOk && pOk;
    }
}
