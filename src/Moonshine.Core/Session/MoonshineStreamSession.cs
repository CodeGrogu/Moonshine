using System.Net.Sockets;
using Moonshine.Interop;
using Moonshine.Protocol.FEC;
using Moonshine.Protocol.RTP;
using Moonshine.Protocol.RTSP;

namespace Moonshine.Core.Session;

public record StreamConfiguration(
    int Width,
    int Height,
    int Fps,
    int BitrateKbps,
    int Codec, // 0: H264, 1: HEVC, 2: AV1
    bool EnableHdr,
    int AudioChannels
);

/// <summary>
/// Manages active game streaming session, RTSP negotiation, and real-time packet dispatch.
/// </summary>
public sealed class MoonshineStreamSession : IAsyncDisposable
{
    private readonly string _hostIp;
    private readonly int _rtspPort;
    private readonly StreamConfiguration _config;
    private IntPtr _spscRing;
    private IntPtr _jitterBuffer;
    private bool _isRunning;
    private int _cseq = 1;

    public MoonshineStreamSession(string hostIp, StreamConfiguration config, int rtspPort = 48010)
    {
        _hostIp = hostIp;
        _rtspPort = rtspPort;
        _config = config;
        _spscRing = MoonshineNativeMethods.SpscCreate(1024);
        _jitterBuffer = MoonshineNativeMethods.JitterCreate(16);
    }

    public async Task StartSessionAsync(CancellationToken ct = default)
    {
        // 1. Perform RTSP Handshake with Sunshine Host
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(_hostIp, _rtspPort, ct).ConfigureAwait(false);
        var stream = tcpClient.GetStream();

        // Send RTSP OPTIONS
        var optionsReq = RtspMessage.CreateRequest(RtspMethod.Options, $"rtsp://{_hostIp}:{_rtspPort}", _cseq++);
        var pipe = new System.IO.Pipelines.Pipe();
        optionsReq.Serialize(pipe.Writer);
        await pipe.Writer.FlushAsync(ct);

        _isRunning = true;
    }

    public unsafe bool ProcessIncomingPacket(ReadOnlySpan<byte> packetData)
    {
        if (!_isRunning || packetData.Length < RtpHeader.Size)
        {
            return false;
        }

        if (!RtpHeader.TryParse(packetData, out var rtpHeader, out var payload))
        {
            return false;
        }

        fixed (byte* payloadPtr = payload)
        {
            var packetDesc = new MoonshinePacketDesc
            {
                SequenceNumber = rtpHeader.SequenceNumber,
                FrameIndex = rtpHeader.Timestamp,
                PacketIndex = 0,
                TotalPackets = 1,
                PayloadSize = (ushort)payload.Length,
                PacketType = (byte)(rtpHeader.PayloadId == 96 ? 0 : 1),
                Flags = (byte)(rtpHeader.Marker ? 0x02 : 0x00),
                BufferSlotIndex = MoonshinePacketDesc.NoBufferSlot,
                PayloadPtr = payloadPtr
            };

            // Dispatch to lock-free native SPSC ring buffer and predictive jitter buffer
            MoonshineNativeMethods.SpscEnqueue(_spscRing, in packetDesc);
            MoonshineNativeMethods.JitterPushPacket(_jitterBuffer, in packetDesc);
        }

        return true;
    }

    public ValueTask DisposeAsync()
    {
        _isRunning = false;
        if (_jitterBuffer != IntPtr.Zero)
        {
            MoonshineNativeMethods.JitterDestroy(_jitterBuffer);
            _jitterBuffer = IntPtr.Zero;
        }
        if (_spscRing != IntPtr.Zero)
        {
            MoonshineNativeMethods.SpscDestroy(_spscRing);
            _spscRing = IntPtr.Zero;
        }
        return ValueTask.CompletedTask;
    }
}
