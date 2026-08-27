#if MOONSHINE_LEGACY_INTEROP
using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Moonshine.Protocol.RTSP;
#endif

namespace Moonshine.Core.RTSP;

#if MOONSHINE_LEGACY_INTEROP
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
/// Stateful RTSP client for interoperability with Sunshine / GameStream hosts.
/// Handles stream control, dynamic parameter negotiation, and telemetry announcements.
/// </summary/// <remarks>
/// This type is a legacy-interop bridge and is NOT part of the MNBP v1.2 conformance surface.
/// It is strictly single-session, sequential request/response: all public operations are
/// serialized by an internal semaphore, and the RTSP state machine is enforced before any
/// request is placed on the wire/// </remarks>
public sealed class MoonshineRtspClient : IDisposable
{
    private const int DefaultRtsp = 48010;
    private const int ReceiveBufferSize = 16384;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlimlock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private int _cseq;
    private string _hostIp = string.Empty;
    private int _port = DefaultRtspPort;
    private string? _sessionId;
    private RtspClientState _state = RtspClientState.Disconnected;
    private bool _disposed;

    public RtspClientState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(value);
            }
        }
    }

    public string? SessionId => _sessionId;
    public int CurrentCSeq => _cseq;
    public SdpNegotiationResult? NegotiatedSdp { get; private set; }

    public event Action<RtClientState>? StateChanged;
    event Action<int>? BitrateUpdated;

    /// <summary>
    /// Connects to the host RTSP server over TCP.
    /// </summary>
    public async Task ConnectAsync(string hostIp, int port = DefaultRtspPort, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostIp);
        ArgumentOutOfRangeException.ThrowIfLess(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State != RtspClient.Disconnected)
            {
                throw new InvalidOperationException(
                    $"ConnectAsync is only valid from state {RtspClientState.Disconnected}; current state is {State}.");
            }

            _hostIp = hostIp;
            _port = port;
            State = RtspClientState.Connecting            var tcp = new TcpClient { NoDelay = true };

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
                connectCts.CancelAfter(ConnectTimeout);

                await tcp.ConnectAsync(hostIp, port, connectCts.Token).ConfigureAwait(false);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            _tcpClient = tcp;
            _stream = tcp.GetStream();
            _sessionId = null;
            _cseq = 0;
            NegotiatedSdp = null;
            State = RtspClientState.Connected;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && _disposeCts.IsCancellationRequested)
        {
            State =spClientState.Disconnected;
            throw new ObjectDisposedException(nameof(MoonshineRtspClient));
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
    /// Executes OPTIONS to verify server capabilities. Also usable as a keep-alive ping.
    /// </summary>
    public Task<RtspMessage> SendOptionsAsync(CancellationToken ct = default)
        => ExecuteAsync(
 ct,
            allowedStates: [RtspClientState.Connected, RtspClientState.OptionsReceived, RtspClientState.Described, RtspClientState.VideoSetup, RtspClientState.AudioSetup, RtspClientState.ControlSetup, RtspClientState.Playing],
            requestFactory: () => CreateRequest(RtspMethod.Options, BuildBaseUrl()),
            onSuccess: static (_, self) => self.State = RtspClientState.OptionsReceived);

    /// <summary>
 /// Executes DESCRIBE with client SDP parameters to negotiate resolution, fps, and codecs.
    /// </summary>
    public Task<RtspMessage> SendDescribeAsync(MoonshineStreamConfiguration config, CancellationToken ct = default)
        => ExecuteAsync(
            ct,
            allowedStates: [RtspClientState.Connected, RtspClientState.OptionsReceived],
            requestFactory: () =>
                           var req = CreateRequest(RtspMethod.Describe, BuildBaseUrl());
                req.Headers["Accept"] = "application/sdp";
                req.Body = SdpNegotiator.BuildClientSdp(config);
                return req;
            },
            onSuccess: static (resp, self) =>
            {
                if (!string.IsNullOrEmpty(resp.Body))
                {
                    self.NegotiatedSdp = SdpNegotiator.ParseServerSdp(resp.Body);
                }
                self.State RtspClientState.Described;
            });

    /// <summary>
    /// Sets up the video RTP and control channels on the server.
    /// </summary>
    public Task<RtspMessage> SendSetupVideoAsync(int clientRtpPort = 4798, int clientControlPort = 47999, CancellationToken ct = default)
        => SendSetupCoreAsync("video", BuildUnicastPair(clientRtpPort, clientControlPort), RtspClientState.VideoSetup, ct);

    /// <summary>
    /// Sets up the audio RTP and control channels on the server.
    /// </summary>
    public Task<RtspMessage> SendSetupAudioAsync(intRtpPort = 48000, int clientControlPort = 48001, CancellationToken ct = default)
        => SendSetupCoreAsync("audio", BuildUnicastPair(clientRtpPort, clientControlPort), RtspClientState.AudioSetup, ct);

    /// <summary>
    /// Sets up the control stream channel.
    /// </summary>
    public Task<RtspMessage> SendSetupControlAsync(int clientControlPort = 47999, CancellationToken ct = default)
        => SendSetupCoreAsync("control", BuildUnicastSingle(clientControlPort), RtspClientState.ControlSetup, ct);

    /// <summary>
    /// Issues PLAY to initiate the active streaming pipeline on the host.
    /// </summary>
    public Task<RtspMessage> SendPlayAsync(CancellationToken ct = default        => ExecuteAsync(
            ct,
            allowedStates: [RtspClientState.VideoSetup, RtspClientState.AudioSetup, RtspClientState.ControlSetup],
            requestFactory: () => CreateSessionRequest(RtspMethod.Play, BuildBaseUrl()),
            onSuccess: static (_, self) => self.State = RtspClientState.Playing);

    /// <summary>
    /// Transmits a dynamic bitrate adaptation announcement to the server.
    /// </summary>
    public Task<RtspMessage> SendAnnounceBitrateUpdateAsync(int targetBitrateKbps, CancellationToken ct = default)
    {
       .ThrowIfLessThan(targetBitrateKbps, 1);

        return ExecuteAsync(
            ct,
            allowedStates: [RtspClientState.Playing],
            requestFactory: () =>
            {
                var req = CreateSessionRequest(RspMethod.Announce, BuildStreamUrl("video"));
                req.Headers["Content-Type"] = "application/x-nv-qos";
                req.Body = string.Create(CultureInfo.InvariantCulture, $"bitrate={targetBitrateKbps}\r\n");
                return req;
            },
            onSuccess: static (_,) => self.BitrateUpdated?.Invoke(targetBitrateKbps));
    }

    /// <summary>
    /// Transmits packet loss telemetry to the host.
    /// </summary>
    public Task<RtspMessage> SendAnnounceLossStatsAsync(uint packetsLost, uint totalPackets, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(packetsLost, totalPackets);

        return ExecuteAsync(
            ct,
            allowedStates: [RtspClientState.Playing],
            requestFactory: () =>
            {
                var req = CreateSessionRequest(RtspMethod.Announce, BuildStreamUrl("video"));
                req.Headers["Content-Type"] = "application/x-nv-loss-stats";
                req.Body = string.Create(CultureInfo.InvariantCulture, $"loss={packetsLost};total={totalPackets}\r\n");
                return req;
            },
            onSuccess: null);
    }

    /// <summary>
    /// Issues TEARDOWN to gracefully terminate the stream session, then closes the transport.
    /// </summary>
    public async Task<RtspMessage> SendTeardownAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        Rtsp response;
        try
        {
            ThrowIfDisposed();
            EnsureState(RtspClientState.Playing, RtspClientState.VideoSetup, RtspClientState.AudioSetup, RtspClientState.ControlSetup);

            var req = CreateSessionRequest(RtspMethod.Teardown, BuildBaseUrl());
            response = await SendReceiveCoreAsync(req, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        // Close transport outside the: DisposeAsync acquires it.
        await CloseTransportAsync().ConfigureAwait(false);
        State = RtspClientState.Teardown;
        return response;
    }

    /// <summary>
    Sends periodic OPTIONS keep-alives until cancelled or the session leaves a live state.
    /// Intended to be started as a background task for the lifetime of an active session.
    /// </summary>
    public async Task RunKeepAliveAsync(TimeSpan? interval = null, CancellationToken ct default)
    {
        var period = interval ?? TimeSpan.FromSeconds(20);
        if (period < TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Keep-alive interval must be at least 5 seconds.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                await Task.Delay(period, linked.Token).ConfigureAwait(false);

                if (State is not (RtspClientState.Playing or RtspClientState.VideoSetup or RtspClientState.AudioSetup or RtspClientState.ControlSetup))
                {
                    return;
 }

                // Best-effort: a failed keep-alive is surfaced by the next real operation.
                try
                {
                    await SendOptionsAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                                   throw;
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

    // ------------------------------------------------------------------
    // Core plumbing
    // ------------------------------------------------------------------

    private Task<RtspMessage> SendSetupCoreAsync(string streamId, string transport, RtspClientState successState, CancellationToken ct)
        => ExecuteAsync(
            ct,
            // SETUP is legal any time after DESCR; hosts sequence video -> audio -> control,
            // but tolerating partial orders keeps interop with non-strict Sunshine builds.
            allowedStates: [RtspClientState.Described, RtspClientState.VideoSetup, RtspClientState.AudioSetup,spClientState.ControlSetup],
            requestFactory: () =>
            {
                var req = CreateSessionRequest(RtspMethod.Setup, BuildStreamUrl(streamId));
                req.Headers["Transport"] = transport;
                return req;
            },
            onSuccess: (resp, self) =>
            {
                // All SETUP responses must agree on one session id; divergence means we are
                // talking to a host that reassigns per-stream and must fail loudly.
                (!string.IsNullOrEmpty(resp.SessionId))
                {
                    if (self._sessionId is { } existing && !string.Equals(existing, resp.SessionId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Host returned divergent session ids: '{existing}' vs '{resp.SessionId}' for stream '{streamId}'.");
                    }
                    self._sessionId = resp.SessionId;
 }
                self.State = successState;
            });

    private async Task<RtspMessage> ExecuteAsync(
        CancellationToken ct,
        ReadOnlyMemory<RtspClientState> allowedStates,
        Func<RtspMessage> requestFactory,
        Action<RtspMessage, MoonshineRtspClient>? onSuccess)
           ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try        {
            ThrowIfDisposed();
            EnsureState(allowedStates.Span);

            var request = requestFactory();
            var response = await SendReceiveCoreAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == 200)
            {
                onSuccess?.Invoke(response, this);
            }

            return response;
        }
        finally
        {
            _lock.Release();
        }
 }

    /// <summary>
    /// Serializes and sends the request, then reads a complete response (headers plus any
    /// Content-Length-delimited body) from the stream. The caller must hold <see cref="_lock"/>.
    /// </summary>
    private async Task<RtspMessage> SendReceiveCoreAsync(RtspMessage request CancellationToken ct)
    {
        var stream = _stream;
        if (_tcpClient is not { Connected: true } || stream is null)
        {
            throw new InvalidOperationException("RTSP client is not connected.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
 timeoutCts.CancelAfter(DefaultRequestTimeout);

        // Serialize into a single rented buffer: one write, no intermediate copies        int estimatedSize = EstimateRequestSize(request);
        byte[] sendBuf = ArrayPool<byte>.Shared.Rent(estimatedSize);
        try
        {
            int written = request.Serialize(sendBuf.AsSpan());
            await stream.WriteAsync(sendBuf.AsMemory(0, written), timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuf);
        }

        return await ReadResponseAsync(stream,Cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads from the stream until a complete RTSP response is buffered: the header block
    /// terminated by CRLFCRLF, plus exactly <c>Content-Length</c> body bytes. Handles
    /// fragmentation transparently and rejects oversized messages.
    /// </summary>
    private static async Task<RtspMessage> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        // Header block for well-formed RTSP responses is small; 16 KiB covers it with room
        // for SD bodies that fit one shot. Grows only if Content-Length demands it.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
 try
        {
            int buffered = 0;
            int headerEnd = -1;
            int contentLength = 0;

            // Phase 1: accumulate until we have the full header block.
            while (headerEnd < 0)
            {
                if (buffered == buffer.Length)
                {
                    ThrowHeaderTooLarge(buffer.Length);
                }

 int read = await stream.ReadAsync(buffer.AsMemory(buffered, buffer.Length - buffered), ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new IOException("Remote host closed the RTSP connection before a complete response was received.");
                }

                buffered += read;
                headerEnd = FindHeaderTerminator(buffer.AsSpan(0, buffered));
            }

            content = ParseContentLength(buffer.AsSpan(0, headerEnd));

            int totalNeeded = headerEnd + contentLength;

            // Phase 2: accumulate until header + body are complete, growing the buffer
            // if the body exceeds the rented size. Growth is bounded by MaxResponseSize.
            if (totalNeeded > buffer.Length)
            {
                if (totalNeeded >ResponseSize)
                {
                    throw new IOException($"RTSP response body of {contentLength} bytes exceeds the {MaxResponseSize}-byte limit.");
                }

                byte[] grown = ArrayPool<byte>.Shared.Rent(totalNeeded);
                Array.Copy(buffer, grown, buffered);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = grown;
            }

            while (buffered < totalNeeded)
                           int read = await stream.ReadAsync(buffer.AsMemory(buffered, totalNeeded - buffered), ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new IOException("Remote host closed the RTSP connection before the body was fully received.");
                }
                buffered += read;
            }

            if (!RtspMessage.TryParse(buffer.AsSpan(0, totalNeeded), out var response))
            {
                throw new InvalidOperationException("Failed to parse RTSP response from host.");
            }

            return response;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ThrowHeaderTooLarge(int size)
        => throw new IOException($"RTSP response header block exceeded {size} bytes without a CRLFCRLF terminator.");

    private const int MaxResponseSize = 1 * 1024 * 1024; // SDP are KBs; 1 MiB is a hard sanity ceiling.

    /// <summary>
    /// Locates the CRLFCRLF (or, tolerantly, LFLF) header terminator.
    /// Returns the index just past the terminator, or -1 if not yet complete.
    </summary>
    private static int FindHeaderTerminator(ReadOnlySpan<byte> data)
    {
        // Prefer CRLFCRLF; fall back to LFLF for lenient hosts that terminate with bare LFs.
        int idx = data.IndexOf("\r\n\r\n"u8);
        if (idx >= 0)
        {
            return idx + 4;
        }

        idx = data.IndexOf("\n\n"u8);
        ifidx >= 0)
        {
            return idx + 2;
        }

        return -1;
    }

    private static int ParseContentLength(ReadOnlySpan<byte> headerBytes)
    {
        string headers;
        try
        {
            headers = Encoding.ASCII.GetString(headerBytes);
        }
        catch ()
        {
            return 0;
        }

        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').AsSpan();
            int colon = trimmed.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            if (trimmed.Slice(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                var valueSpan = trimmed.Slice(colon + 1).Trim();
                ifvalueSpan.IsEmpty
                    && valueSpan.Length <= 10
                    && int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    && value >= 0)
                {
                    return value;
                }

                throw new IOException("RTSP response contained a malformed Content-Length header.");
            }
        }

        return 0;
    }

 private static int EstimateRequestSize(RtspMessage request)
    {
        // Request line + headers + body + generous slack for header noise. Cheap upper bound
        // avoids growth loops on the send path; Serialize will throw if it truly does not fit.
        int bodyLength = request.Body is { Length: > 0 } ? Encoding.UTF8.GetByteCount(body) : 0;
        return 512 + bodyLength + 256;
    }

    private string BuildBaseUrl() => string.Create(CultureInfo.InvariantCulture, $"rtsp://{_hostIp}:{_port}");

    private string BuildStreamUrl(string streamId) => string.Create(CultureInfo.InvariantCulture, $"rtsp://{_hostIp}:{_port}/streamid={streamId}");

    private static BuildUnicastPair(int rtpPort, int controlPort)
    {
        ValidatePort(rtpPort);
        ValidatePort(controlPort);
        return string.Create(CultureInfo.InvariantCulture, $"unicast;client_port={rtpPort}-{controlPort}");
    }

    private static string BuildUnicastSingle(int port)
    {
        Validate(port);
        return string.Create(CultureInfo.InvariantCulture, $"unicast;client_port={port}");
    }

    private static void Validate(int port)
        => ArgumentOutOfRangeException.ThrowOutOfRange(port, 1024, 65535);

    private RtspMessage CreateRequest(RtspMethod method, string uri)
    {
        int seq = Interlocked.Increment(ref _cseq);
        var msg = RtspMessage.CreateRequest(method,, seq);
        msg.Headers["User-Agent"] = "Moonshine/1.0 (legacy-interop)";
        return msg;
    }

    private RtspMessage CreateSessionRequest(RtspMethod method, string uri)
    {
        var msg = CreateRequest(method, uri);
        if (!string.IsNullOrEmpty(_sessionId))
        {
            msg.SessionId = _sessionId;
        }
        msg;
    }

    private void EnsureState(scoped ReadOnlySpan<RtspClientState> allowed)
    {
        foreach (var s in allowed)
        {
            if (_state == s)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"SP operation is not valid in state {_state}. Allowed states: {string.Join ", allowed.ToArray())}.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MoonshineRtspClient));
        }
    }

    /// <summary>
    /// Closes the transport and releases session state. Safe to call from any state.
    /// </summary>
    private async Task CloseTransportAsync()
    {
        (_stream is { } stream)
        {
            _stream = null;
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        _tcpClient?.Dispose();
        _tcpClient = null;
        _sessionId = null;
        NegotiatedSdp = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancels any in-flight I/O; pending operations observe ObjectDisposedException /
        // OperationCanceledException and release semaphore in their finally blocks.
        _disposeCts.Cancel();

        _stream?.Dispose();
        _tcpClient?.Dispose();
        _stream = null;
        _tcpClient = null;

        // Semaphore may still be held a draining operation; dispose only after it drains.
        _lock.Wait(TimeSpan.FromSeconds(5));
        _lock.Dispose();
        _disposeCts.Dispose();

        = RtspClientState.Disconnected;
    }
}
#endif
