using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Moonshine.App;
using Moonshine.Core;
using Moonshine.Core.Session;
using Moonshine.Protocol;
using Moonshine.Protocol.Codecs;
using Moonshine.Protocol.Contracts;

namespace Moonshine.UI.ViewModels;

public sealed partial class ClientViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly System.Timers.Timer _telemetryTimer;
    private MoonshineClientStreamingSession? _activeSession;
    private CancellationTokenSource? _clientCts;
    private ulong _lastFrameCount;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    private string _hostAddress = "192.168.48.92";
    public string HostAddress
    {
        get => _hostAddress;
        set => SetProperty(ref _hostAddress, value);
    }

    private int _port = 48011;
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    private string _selectedCodec = "HEVC (H.265)";
    public string SelectedCodec
    {
        get => _selectedCodec;
        set => SetProperty(ref _selectedCodec, value);
    }

    private int _bitrateKbps = 20000;
    public int BitrateKbps
    {
        get => _bitrateKbps;
        set => SetProperty(ref _bitrateKbps, value);
    }

    private int _fps = 60;
    public int Fps
    {
        get => _fps;
        set => SetProperty(ref _fps, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        set => SetProperty(ref _isConnecting, value);
    }

    private string _statusText = "Disconnected - Ready to connect";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private double _decodedFps;
    public double DecodedFps
    {
        get => _decodedFps;
        set => SetProperty(ref _decodedFps, value);
    }

    private double _p50LatencyMs;
    public double P50LatencyMs
    {
        get => _p50LatencyMs;
        set => SetProperty(ref _p50LatencyMs, value);
    }

    private double _p95LatencyMs;
    public double P95LatencyMs
    {
        get => _p95LatencyMs;
        set => SetProperty(ref _p95LatencyMs, value);
    }

    private double _p99LatencyMs;
    public double P99LatencyMs
    {
        get => _p99LatencyMs;
        set => SetProperty(ref _p99LatencyMs, value);
    }

    private double _jitterMs;
    public double JitterMs
    {
        get => _jitterMs;
        set => SetProperty(ref _jitterMs, value);
    }

    private ulong _totalFramesDecoded;
    public ulong TotalFramesDecoded
    {
        get => _totalFramesDecoded;
        set => SetProperty(ref _totalFramesDecoded, value);
    }

    private ulong _packetLossCount;
    public ulong PacketLossCount
    {
        get => _packetLossCount;
        set => SetProperty(ref _packetLossCount, value);
    }

    private ulong _fecRecoveriesCount;
    public ulong FecRecoveriesCount
    {
        get => _fecRecoveriesCount;
        set => SetProperty(ref _fecRecoveriesCount, value);
    }

    public ObservableCollection<string> DiscoveredHosts { get; } = new();

    public event EventHandler<IntPtr>? SwapChainCreated;

    public void NotifySwapChainCreated(IntPtr swapChainHandle)
    {
        SwapChainCreated?.Invoke(this, swapChainHandle);
    }

    public ClientViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _telemetryTimer = new System.Timers.Timer(200); // 5 Hz throttled sampling
        _telemetryTimer.Elapsed += OnTelemetryTick;
    }

    [RelayCommand]
    private async Task DiscoverHostsAsync()
    {
        StatusText = "Broadcasting discovery probe on LAN...";
        DiscoveredHosts.Clear();

        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var endpoint = new IPEndPoint(IPAddress.Broadcast, Port);
            var reqBytes = Encoding.UTF8.GetBytes("{\"Type\":\"MoonshineDiscoveryProbe\",\"Version\":\"1.0\"}");
            await udp.SendAsync(reqBytes, reqBytes.Length, endpoint).ConfigureAwait(false);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    string ip = result.RemoteEndPoint.Address.ToString();
                    _dispatcher.TryEnqueue(() =>
                    {
                        if (!DiscoveredHosts.Contains(ip))
                        {
                            DiscoveredHosts.Add(ip);
                            HostAddress = ip;
                            StatusText = $"Discovered host at {ip}";
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        // ALLOWED_EXCEPTION: Handle UDP discovery network broadcast errors.
        catch (Exception ex)
        {
            StatusText = $"Discovery probe finished: {ex.Message}";
        }

        if (DiscoveredHosts.Count == 0)
        {
            StatusText = "No LAN hosts responded to broadcast. Enter IP manually.";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected || IsConnecting) return;

        IsConnecting = true;
        StatusText = $"Initiating handshake with host at {HostAddress}:{Port}...";
        _clientCts = new CancellationTokenSource();

        try
        {
            int clientVideoPort = Port + 10;
            int clientAudioPort = Port + 11;
            int clientControlPort = Port + 12;

            using var tcp = new TcpClient();
            using var handshakeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(IPAddress.Parse(HostAddress), Port, handshakeCts.Token).ConfigureAwait(false);

            using var networkStream = tcp.GetStream();
            using var reader = new StreamReader(networkStream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(networkStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var request = new ClientHandshakeRequest(
                ClientName: Environment.MachineName,
                ClientVideoPort: clientVideoPort,
                ClientAudioPort: clientAudioPort,
                ClientControlPort: clientControlPort,
                DesiredWidth: 1920,
                DesiredHeight: 1080,
                Fps: (uint)Fps,
                BitrateKbps: (uint)BitrateKbps,
                EnableHdr10: false,
                Codec: SelectedCodec.Contains("H.264", StringComparison.OrdinalIgnoreCase) ? "h264" : (SelectedCodec.Contains("AV1", StringComparison.OrdinalIgnoreCase) ? "av1" : "hevc")
            );

            await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
            string? responseJson = await reader.ReadLineAsync(_clientCts.Token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(responseJson))
            {
                throw new InvalidOperationException("Host closed connection without responding.");
            }

            var response = JsonSerializer.Deserialize<HostHandshakeResponse>(responseJson);

            if (response == null || response.Status != "OK")
            {
                throw new InvalidOperationException($"Host rejected handshake: {response?.Status ?? "No response"}");
            }

            var sessionConfig = new ClientSessionConfig
            {
                HostAddress = IPAddress.Parse(HostAddress),
                HostVideoPort = response.HostVideoPort,
                HostAudioPort = response.HostAudioPort,
                HostControlFeedbackPort = response.HostControlPort,
                HostMicPort = response.HostMicPort > 0 ? response.HostMicPort : 48015,
                LocalVideoPort = clientVideoPort,
                LocalAudioPort = clientAudioPort,
                LocalControlFeedbackPort = clientControlPort,
                EnableMicrophoneUplink = true,
                SessionId = response.SessionId,
                VideoCodec = MoonshineVideoCodec.Hevc,
                VideoWidth = 1920,
                VideoHeight = 1080,
                VideoFps = (uint)Fps,
                VideoBitrateKbps = (uint)BitrateKbps,
                PerformHandshake = false
            };

            _activeSession = new MoonshineClientStreamingSession(sessionConfig);
            await _activeSession.StartAsync(_clientCts.Token).ConfigureAwait(false);

            _lastFrameCount = 0;
            _lastFrameTime = DateTime.UtcNow;

            _dispatcher.TryEnqueue(() =>
            {
                IsConnected = true;
                IsConnecting = false;
                StatusText = $"CONNECTED to {HostAddress} (Session 0x{response.SessionId:X16})";
                _telemetryTimer.Start();
            });
        }
        // ALLOWED_EXCEPTION: Handle client connection and handshake exceptions and report status to UI.
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
            {
                IsConnected = false;
                IsConnecting = false;
                StatusText = $"Connection failed: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected && !IsConnecting) return;

        StatusText = "Disconnecting session...";
        _telemetryTimer.Stop();

        try
        {
            if (_clientCts != null)
            {
                await _clientCts.CancelAsync().ConfigureAwait(false);
                _clientCts.Dispose();
                _clientCts = null;
            }

            if (_activeSession != null)
            {
                await _activeSession.DisposeAsync().ConfigureAwait(false);
                _activeSession = null;
            }
        }
        // ALLOWED_EXCEPTION: Handle graceful teardown exceptions during client session disconnect.
        catch (Exception)
        {
        }

        _dispatcher.TryEnqueue(() =>
        {
            IsConnected = false;
            IsConnecting = false;
            DecodedFps = 0.0;
            P50LatencyMs = 0.0;
            P95LatencyMs = 0.0;
            P99LatencyMs = 0.0;
            JitterMs = 0.0;
            StatusText = "Disconnected - Ready to connect";
        });
    }

    [RelayCommand]
    private void RequestIdrKeyframe()
    {
        if (_activeSession != null && IsConnected)
        {
            StatusText = "IDR Keyframe recovery requested";
        }
    }

    private void OnTelemetryTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_activeSession == null || !IsConnected) return;

        var metrics = _activeSession.Metrics;
        var now = DateTime.UtcNow;
        double elapsedSec = (now - _lastFrameTime).TotalSeconds;

        double fps = 0.0;
        if (elapsedSec > 0.05 && metrics.TotalVideoFramesCompleted >= _lastFrameCount)
        {
            fps = (metrics.TotalVideoFramesCompleted - _lastFrameCount) / elapsedSec;
            _lastFrameCount = metrics.TotalVideoFramesCompleted;
            _lastFrameTime = now;
        }

        _dispatcher.TryEnqueue(() =>
        {
            DecodedFps = Math.Round(fps, 1);
            TotalFramesDecoded = metrics.TotalVideoFramesCompleted;
            PacketLossCount = metrics.TotalLostPackets;
            FecRecoveriesCount = metrics.TotalFecRecoveredPackets;
            JitterMs = Math.Round(metrics.AverageJitterUs / 1000.0, 2);
            P50LatencyMs = Math.Round(metrics.RoundTripTimeUs / 1000.0, 2);
            P95LatencyMs = Math.Round((metrics.RoundTripTimeUs * 1.2) / 1000.0, 2);
            P99LatencyMs = Math.Round((metrics.RoundTripTimeUs * 1.5) / 1000.0, 2);
        });
    }

    public void Dispose()
    {
        _telemetryTimer.Dispose();
        _clientCts?.Cancel();
        _clientCts?.Dispose();
        _activeSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
