using System.Buffers.Binary;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Moonshine.Core.Media;
using Moonshine.Host.Session;
using Moonshine.Protocol.Audio;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Feedback;
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
    private MoonshineHelloPayload _helloPayload;
    private MoonshineHelloResponsePayload _helloRespPayload;
    private MoonshineSessionSetupPayload _setupPayload;
    private MoonshineSessionSetupResponsePayload _setupRespPayload;
    private MoonshineInputKeyboardPayload _kbPayload;
    private MoonshineInputMousePayload _mousePayload;
    private MoonshineInputGamepadPayload _gamepadPayload;
    private MoonshineFeedbackLossStatsPayload _lossStatsPayload;
    private MoonshineIdrRequestPayload _idrPayload;

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

        _binaryCodecBuffer = new byte[512];
        _header = new MoonshinePacketHeader(
            Magic: MoonshineProtocolConstants.Magic,
            Version: MoonshineProtocolConstants.Version10,
            MessageType: MoonshineMessageType.InputKeyboard,
            PayloadSize: 12,
            SequenceNumber: 42,
            SessionId: 0x1234567890ABCDEFUL,
            TimestampUs: 12345678UL);

        _helloPayload = new MoonshineHelloPayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 0,
            CapabilitiesMask = MoonshineCapabilities.Hevc | MoonshineCapabilities.ReedSolomonFec | MoonshineCapabilities.HighPollRateInput,
            ClientNonce = 0xDEADBEEFCAFEUL,
            ClientUuid = new MoonshineUuid128(Guid.NewGuid())
        };

        _helloRespPayload = new MoonshineHelloResponsePayload
        {
            ServerVersionMajor = 1,
            ServerVersionMinor = 0,
            NegotiatedCapabilities = MoonshineCapabilities.Hevc | MoonshineCapabilities.ReedSolomonFec,
            AssignedSessionId = 0xCAFEBABE11223344UL,
            ServerNonce = 0x1122334455667788UL,
            ChallengeSalt = new MoonshineUuid128(Guid.NewGuid()),
            SessionLeaseSeconds = 3600,
            Reserved = 0
        };

        _setupPayload = new MoonshineSessionSetupPayload
        {
            VideoWidth = 1920,
            VideoHeight = 1080,
            VideoFps = 60,
            VideoBitrateKbps = 20000,
            VideoCodec = MoonshineVideoCodec.Hevc,
            VideoColorFormat = MoonshineColorFormat.Nv12,
            AudioChannels = 2,
            AudioCodec = MoonshineAudioCodec.Opus,
            AudioSampleRate = 48000,
            AudioBitrateKbps = 128,
            ClientUdpVideoPort = 48011,
            ClientUdpAudioPort = 48012,
            ClientUdpFeedbackPort = 48013,
            Reserved = 0,
            MtuPayloadSize = 1188
        };

        _setupRespPayload = new MoonshineSessionSetupResponsePayload
        {
            StatusCode = MoonshineErrorCode.Success,
            VideoStreamId = 1,
            AudioStreamId = 2,
            FeedbackStreamId = 3,
            HostUdpVideoPort = 48011,
            HostUdpAudioPort = 48012,
            HostUdpFeedbackPort = 48013,
            HostUdpInputPort = 48014,
            NegotiatedMtu = 1188,
            Reserved = 0
        };

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

        _gamepadPayload = new MoonshineInputGamepadPayload
        {
            GamepadIndex = 0,
            Reserved = 0,
            ButtonMask = 0x1000,
            LeftTrigger = 255,
            RightTrigger = 0,
            ThumbLx = 10000,
            ThumbLy = -10000,
            ThumbRx = 0,
            ThumbRy = 0,
            MotorLeft = 0,
            MotorRight = 0,
            TimestampOffsetUs = 0,
            Reserved2 = 0
        };

        _lossStatsPayload = new MoonshineFeedbackLossStatsPayload
        {
            StreamId = 1,
            LastReceivedFrameIndex = 120,
            PacketsReceived = 1000,
            PacketsLost = 2,
            PacketsRecoveredFec = 2,
            RoundTripTimeUs = 2500,
            JitterUs = 120,
            EstimatedBandwidthKbps = 25000,
            ReceiveQueueDepth = 1
        };

        _idrPayload = new MoonshineIdrRequestPayload
        {
            StreamId = 1,
            LastValidFrameIndex = 119,
            ReasonCode = 1
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
    public bool Session_BinaryContract_HelloRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool writeOk = MoonshineProtocolCodec.TryWriteHello(in _helloPayload, dest);
        bool readOk = MoonshineProtocolCodec.TryReadHello(dest[..32], out _);
        return writeOk && readOk;
    }

    [Benchmark]
    public bool Session_BinaryContract_SessionSetupRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool writeOk = MoonshineProtocolCodec.TryWriteSessionSetup(in _setupPayload, dest);
        var err = MoonshineProtocolCodec.TryReadSessionSetup(dest[..40], out _);
        return writeOk && err == MoonshineErrorCode.Success;
    }

    [Benchmark]
    public bool Session_BinaryContract_KeyboardPacketRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool hOk = MoonshineProtocolCodec.TryWriteHeader(in _header, dest);
        bool pOk = MoonshineProtocolCodec.TryWriteKeyboardInput(in _kbPayload, dest[MoonshineProtocolConstants.HeaderSize..]);
        var errH = MoonshineProtocolCodec.TryReadHeader(dest[..MoonshineProtocolConstants.HeaderSize], out _);
        var errP = MoonshineProtocolCodec.TryReadKeyboardInput(dest.Slice(MoonshineProtocolConstants.HeaderSize, 12), out _);
        return hOk && pOk && errH == MoonshineErrorCode.Success && errP == MoonshineErrorCode.Success;
    }

    [Benchmark]
    public bool Session_BinaryContract_MousePacketRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool hOk = MoonshineProtocolCodec.TryWriteHeader(in _header, dest);
        bool pOk = MoonshineProtocolCodec.TryWriteMouseInput(in _mousePayload, dest[MoonshineProtocolConstants.HeaderSize..]);
        var errH = MoonshineProtocolCodec.TryReadHeader(dest[..MoonshineProtocolConstants.HeaderSize], out _);
        var errP = MoonshineProtocolCodec.TryReadMouseInput(dest.Slice(MoonshineProtocolConstants.HeaderSize, 20), out _);
        return hOk && pOk && errH == MoonshineErrorCode.Success && errP == MoonshineErrorCode.Success;
    }

    [Benchmark]
    public bool Session_BinaryContract_GamepadPacketRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool hOk = MoonshineProtocolCodec.TryWriteHeader(in _header, dest);
        bool pOk = MoonshineProtocolCodec.TryWriteGamepadInput(in _gamepadPayload, dest[MoonshineProtocolConstants.HeaderSize..]);
        var errH = MoonshineProtocolCodec.TryReadHeader(dest[..MoonshineProtocolConstants.HeaderSize], out _);
        var errP = MoonshineProtocolCodec.TryReadGamepadInput(dest.Slice(MoonshineProtocolConstants.HeaderSize, 24), out _);
        return hOk && pOk && errH == MoonshineErrorCode.Success && errP == MoonshineErrorCode.Success;
    }

    [Benchmark]
    public bool Session_BinaryContract_LossStatsRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool writeOk = MoonshineProtocolCodec.TryWriteFeedbackLossStats(in _lossStatsPayload, dest);
        var err = MoonshineProtocolCodec.TryReadFeedbackLossStats(dest[..40], out _);
        return writeOk && err == MoonshineErrorCode.Success;
    }

    [Benchmark]
    public bool Session_BinaryContract_IdrRequestRoundtrip()
    {
        Span<byte> dest = _binaryCodecBuffer;
        bool writeOk = MoonshineProtocolCodec.TryWriteIdrRequest(in _idrPayload, dest);
        var err = MoonshineProtocolCodec.TryReadIdrRequest(dest[..16], out _);
        return writeOk && err == MoonshineErrorCode.Success;
    }
}
