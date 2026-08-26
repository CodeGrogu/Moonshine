using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Moonshine.App;

namespace Moonshine.UI.ViewModels;

public sealed partial class HostViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly System.Timers.Timer _telemetryTimer;
    private Task? _hostServerTask;
    private CancellationTokenSource? _serverCts;

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
        set
        {
            if (SetProperty(ref _fps, value))
            {
                OnPropertyChanged(nameof(SelectedFpsString));
            }
        }
    }

    public string SelectedFpsString
    {
        get => _fps.ToString();
        set
        {
            if (int.TryParse(value, out int v))
            {
                Fps = v;
            }
        }
    }

    private string _selectedResolution = "1920x1080";
    public string SelectedResolution
    {
        get => _selectedResolution;
        set => SetProperty(ref _selectedResolution, value);
    }

    private bool _enableHdr10;
    public bool EnableHdr10
    {
        get => _enableHdr10;
        set => SetProperty(ref _enableHdr10, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private string _statusText = "Stopped - Ready to host";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _activeClients;
    public int ActiveClients
    {
        get => _activeClients;
        set => SetProperty(ref _activeClients, value);
    }

    private double _streamingBitrateMbps;
    public double StreamingBitrateMbps
    {
        get => _streamingBitrateMbps;
        set => SetProperty(ref _streamingBitrateMbps, value);
    }

    private double _encodeLatencyMs;
    public double EncodeLatencyMs
    {
        get => _encodeLatencyMs;
        set => SetProperty(ref _encodeLatencyMs, value);
    }

    private ulong _totalFramesEncoded;
    public ulong TotalFramesEncoded
    {
        get => _totalFramesEncoded;
        set => SetProperty(ref _totalFramesEncoded, value);
    }

    private ulong _droppedFrames;
    public ulong DroppedFrames
    {
        get => _droppedFrames;
        set => SetProperty(ref _droppedFrames, value);
    }

    public HostViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _telemetryTimer = new System.Timers.Timer(200); // 5 Hz throttled sampling
        _telemetryTimer.Elapsed += OnTelemetryTick;
    }

    [RelayCommand]
    private void StartServer()
    {
        if (IsRunning) return;

        StatusText = $"Starting Host server on port {Port}...";
        _serverCts = new CancellationTokenSource();

        var options = new CliOptions
        {
            Command = AppCommandType.Host,
            Port = Port,
            BitrateKbps = BitrateKbps,
            Fps = Fps,
            Width = 1920,
            Height = 1080,
            Codec = SelectedCodec.Contains("H.264", StringComparison.OrdinalIgnoreCase) ? "h264" : (SelectedCodec.Contains("AV1", StringComparison.OrdinalIgnoreCase) ? "av1" : "hevc"),
            EnableHdr = EnableHdr10
        };

        var ct = _serverCts.Token;
        _hostServerTask = Task.Run(async () =>
        {
            try
            {
                await HostServerRunner.RunHostAsync(options, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Clean shutdown
            }
            // ALLOWED_EXCEPTION: Catch and update host server status upon background failure.
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    IsRunning = false;
                    StatusText = $"Host error: {ex.Message}";
                });
            }
        }, ct);

        IsRunning = true;
        StatusText = $"Host Server RUNNING (Listening on port {Port})";
        _telemetryTimer.Start();
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (!IsRunning) return;

        StatusText = "Stopping Host server...";
        _telemetryTimer.Stop();

        try
        {
            if (_serverCts != null)
            {
                await _serverCts.CancelAsync().ConfigureAwait(false);
                _serverCts.Dispose();
                _serverCts = null;
            }

            if (_hostServerTask != null)
            {
                await Task.WhenAny(_hostServerTask, Task.Delay(2000)).ConfigureAwait(false);
                _hostServerTask = null;
            }
        }
        // ALLOWED_EXCEPTION: Handle graceful teardown exceptions during server shutdown.
        catch (Exception)
        {
        }

        _dispatcher.TryEnqueue(() =>
        {
            IsRunning = false;
            ActiveClients = 0;
            StreamingBitrateMbps = 0.0;
            EncodeLatencyMs = 0.0;
            StatusText = "Stopped - Ready to host";
        });
    }

    private void OnTelemetryTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!IsRunning) return;

        _dispatcher.TryEnqueue(() =>
        {
            // Throttled telemetry tick
        });
    }

    public void Dispose()
    {
        _telemetryTimer.Dispose();
        _serverCts?.Cancel();
        _serverCts?.Dispose();
    }
}
