using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Moonshine.Protocol.RTSP;

namespace Moonshine.Core.RTSP;

public enum RtspClientState
{
    Disconnected,
    Connecting,
    Connected,
    OptionsReceived,
    Described,
    VideoSetup,
    AudioSetup,
    ControlSetup,
    Playing,
    Teardown
}

/// <summary>
/// Stateful, high-performance RTSP client managing stream control, dynamic parameter negotiation,
/// and telemetry announcements with Sunshine / GameStream hosts.
/// </summary>
public sealed class MoonshineRtspClient : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private int _cseq = 1;
    private string _hostIp = string.Empty;
    private int _port = 48010;
    private string? _sessionId;
    private RtspClientState _state = RtspClientState.Disconnected;

    public RtspClientState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(_state);
            }
        }
    }

    public string? SessionId => _sessionId;
    public int CurrentCSeq => _cseq;
    public SdpNegotiationResult? NegotiatedSdp { get; private set; }

    public event Action<RtspClientState>? StateChanged;
    public event Action<int>? BitrateUpdated;

    /// <summary>
    /// Connects to the host RTSP server over TCP (port 48010).
    /// </summary>
    public async Task ConnectAsync(string hostIp, int port = 48010, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _hostIp = hostIp;
            _port = port;
            State = RtspClientState.Connecting;

            _tcpClient = new TcpClient
            {
                NoDelay = true,
                ReceiveTimeout = 5000,
                SendTimeout = 5000
            };

            await _tcpClient.ConnectAsync(hostIp, port, ct).ConfigureAwait(false);
            _stream = _tcpClient.GetStream();
            State = RtspClientState.Connected;
        }
        catch
        {
            State = RtspClientState.Disconnected;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Executes OPTIONS query to verify server capabilities.
    /// </summary>
    public async Task<RtspMessage> SendOptionsAsync(CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Options, $"rtsp://{_hostIp}:{_port}");
        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            State = RtspClientState.OptionsReceived;
        }

        return resp;
    }

    /// <summary>
    /// Executes DESCRIBE query with client SDP parameters to negotiate resolution, fps, and codecs.
    /// </summary>
    public async Task<RtspMessage> SendDescribeAsync(MoonshineStreamConfiguration config, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Describe, $"rtsp://{_hostIp}:{_port}");
        req.Headers["Accept"] = "application/sdp";
        req.Body = SdpNegotiator.BuildClientSdp(config);

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            if (!string.IsNullOrEmpty(resp.Body))
            {
                NegotiatedSdp = SdpNegotiator.ParseServerSdp(resp.Body);
            }
            State = RtspClientState.Described;
        }

        return resp;
    }

    /// <summary>
    /// Sets up the video RTP and control channels on the server.
    /// </summary>
    public async Task<RtspMessage> SendSetupVideoAsync(int clientRtpPort = 47998, int clientControlPort = 47999, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Setup, $"rtsp://{_hostIp}:{_port}/streamid=video");
        req.Headers["Transport"] = string.Create(
            CultureInfo.InvariantCulture,
            $"unicast;client_port={clientRtpPort}-{clientControlPort}"
        );

        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            if (!string.IsNullOrEmpty(resp.SessionId))
            {
                _sessionId = resp.SessionId;
            }
            State = RtspClientState.VideoSetup;
        }

        return resp;
    }

    /// <summary>
    /// Sets up the audio RTP and control channels on the server.
    /// </summary>
    public async Task<RtspMessage> SendSetupAudioAsync(int clientRtpPort = 48000, int clientControlPort = 48001, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Setup, $"rtsp://{_hostIp}:{_port}/streamid=audio");
        req.Headers["Transport"] = string.Create(
            CultureInfo.InvariantCulture,
            $"unicast;client_port={clientRtpPort}-{clientControlPort}"
        );

        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            State = RtspClientState.AudioSetup;
        }

        return resp;
    }

    /// <summary>
    /// Sets up the control stream channel.
    /// </summary>
    public async Task<RtspMessage> SendSetupControlAsync(int clientControlPort = 47999, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Setup, $"rtsp://{_hostIp}:{_port}/streamid=control");
        req.Headers["Transport"] = string.Create(
            CultureInfo.InvariantCulture,
            $"unicast;client_port={clientControlPort}"
        );

        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            State = RtspClientState.ControlSetup;
        }

        return resp;
    }

    /// <summary>
    /// Issues PLAY command to initiate active streaming pipeline on host.
    /// </summary>
    public async Task<RtspMessage> SendPlayAsync(CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Play, $"rtsp://{_hostIp}:{_port}");
        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            State = RtspClientState.Playing;
        }

        return resp;
    }

    /// <summary>
    /// Transmits dynamic bitrate adaptation announcement to the server.
    /// </summary>
    public async Task<RtspMessage> SendAnnounceBitrateUpdateAsync(int targetBitrateKbps, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Announce, $"rtsp://{_hostIp}:{_port}/streamid=video");
        req.Headers["Content-Type"] = "application/x-nv-qos";
        req.Body = string.Create(CultureInfo.InvariantCulture, $"bitrate={targetBitrateKbps}\r\n");

        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == 200)
        {
            BitrateUpdated?.Invoke(targetBitrateKbps);
        }

        return resp;
    }

    /// <summary>
    /// Transmits packet loss telemetry announcement to host.
    /// </summary>
    public async Task<RtspMessage> SendAnnounceLossStatsAsync(uint packetsLost, uint totalPackets, CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Announce, $"rtsp://{_hostIp}:{_port}/streamid=video");
        req.Headers["Content-Type"] = "application/x-nv-loss-stats";
        req.Body = string.Create(CultureInfo.InvariantCulture, $"loss={packetsLost};total={totalPackets}\r\n");

        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        return await SendReceiveAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues TEARDOWN command to gracefully terminate the stream session.
    /// </summary>
    public async Task<RtspMessage> SendTeardownAsync(CancellationToken ct = default)
    {
        var req = CreateRequest(RtspMethod.Teardown, $"rtsp://{_hostIp}:{_port}");
        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.SessionId = _sessionId;
        }

        var resp = await SendReceiveAsync(req, ct).ConfigureAwait(false);

        State = RtspClientState.Teardown;
        return resp;
    }

    private RtspMessage CreateRequest(RtspMethod method, string uri)
    {
        int seq = Interlocked.Increment(ref _cseq);
        var msg = RtspMessage.CreateRequest(method, uri, seq);
        msg.Headers["User-Agent"] = "Moonshine/1.0";
        return msg;
    }

    private async Task<RtspMessage> SendReceiveAsync(RtspMessage request, CancellationToken ct)
    {
        if (_stream == null || _tcpClient == null || !_tcpClient.Connected)
        {
            throw new InvalidOperationException("RTSP client is not connected.");
        }

        var writer = new ArrayBufferWriter<byte>();
        request.Serialize(writer);
        byte[] requestBytes = writer.WrittenSpan.ToArray();

        await _stream.WriteAsync(requestBytes, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] buffer = new byte[8192];
        int bytesRead = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);

        if (bytesRead <= 0)
        {
            throw new IOException("Remote host closed RTSP connection.");
        }

        if (!RtspMessage.TryParse(buffer.AsSpan(0, bytesRead), out var response))
        {
            throw new InvalidOperationException("Failed to parse RTSP response from host.");
        }

        return response;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _lock.Dispose();
        State = RtspClientState.Disconnected;
    }
}
