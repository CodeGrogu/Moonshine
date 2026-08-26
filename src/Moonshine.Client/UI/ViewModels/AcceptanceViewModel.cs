using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Moonshine.App;
using Moonshine.Host.Acceptance;
using Moonshine.Protocol.Contracts;

namespace Moonshine.UI.ViewModels;

public sealed class AcceptanceStepDisplayItem : ObservableObject
{
    private int _stepNumber;
    public int StepNumber
    {
        get => _stepNumber;
        set => SetProperty(ref _stepNumber, value);
    }

    private string _stepName = string.Empty;
    public string StepName
    {
        get => _stepName;
        set => SetProperty(ref _stepName, value);
    }

    private string _status = "Pending";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _duration = "0 ms";
    public string Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    private string _evidenceSummary = string.Empty;
    public string EvidenceSummary
    {
        get => _evidenceSummary;
        set => SetProperty(ref _evidenceSummary, value);
    }

    private string _details = string.Empty;
    public string Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }
}

public sealed partial class AcceptanceViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _acceptanceCts;

    private string _acceptanceRole = "Client";
    public string AcceptanceRole
    {
        get => _acceptanceRole;
        set => SetProperty(ref _acceptanceRole, value);
    }

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

    private string _runId = AcceptanceRunId.Generate().ToString();
    public string RunId
    {
        get => _runId;
        set => SetProperty(ref _runId, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    private string _statusText = "Ready to start production acceptance run";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set => SetProperty(ref _currentStepIndex, value);
    }

    private string _overallVerdict = "PENDING";
    public string OverallVerdict
    {
        get => _overallVerdict;
        set => SetProperty(ref _overallVerdict, value);
    }

    private string _reportMarkdown = "# Production Acceptance Report\n\nRun not started yet.";
    public string ReportMarkdown
    {
        get => _reportMarkdown;
        set => SetProperty(ref _reportMarkdown, value);
    }

    // Interactive Human Observation Prompts
    private bool _isHumanPromptActive;
    public bool IsHumanPromptActive
    {
        get => _isHumanPromptActive;
        set => SetProperty(ref _isHumanPromptActive, value);
    }

    private string _promptTitle = "Physical Human Observation";
    public string PromptTitle
    {
        get => _promptTitle;
        set => SetProperty(ref _promptTitle, value);
    }

    private string _promptQuestion = "Please evaluate the live stream:";
    public string PromptQuestion
    {
        get => _promptQuestion;
        set => SetProperty(ref _promptQuestion, value);
    }

    private bool _videoSharp = true;
    public bool VideoSharp
    {
        get => _videoSharp;
        set => SetProperty(ref _videoSharp, value);
    }

    private bool _audioAudible = true;
    public bool AudioAudible
    {
        get => _audioAudible;
        set => SetProperty(ref _audioAudible, value);
    }

    private bool _inputResponsive = true;
    public bool InputResponsive
    {
        get => _inputResponsive;
        set => SetProperty(ref _inputResponsive, value);
    }

    private string _operatorNotes = string.Empty;
    public string OperatorNotes
    {
        get => _operatorNotes;
        set => SetProperty(ref _operatorNotes, value);
    }

    private TaskCompletionSource<bool>? _observationTcs;

    public ObservableCollection<AcceptanceStepDisplayItem> Steps { get; } = new();

    public AcceptanceViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        InitialiseStepList();
    }

    private void InitialiseStepList()
    {
        Steps.Clear();
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 1, StepName = "Physical Environment & Hardware Inventory" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 2, StepName = "Real Video Pipeline (D3D11 NVENC -> D3D11 Decode)" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 3, StepName = "Real Host Audio Pipeline (WASAPI -> Opus -> WASAPI)" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 4, StepName = "Real Client Microphone Uplink Channel" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 5, StepName = "Real Remote Input Injection Pipeline" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 6, StepName = "Remote Host Configuration & Instant IDR Recovery" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 7, StepName = "Transport Resilience & Automatic Reconnect" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 8, StepName = "Network Impairment & Jitter Buffer Tolerance" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 9, StepName = "Sustained Streaming & Telemetry Profiling" });
        Steps.Add(new AcceptanceStepDisplayItem { StepNumber = 10, StepName = "Physical Human Observation Confirmation" });
    }

    [RelayCommand]
    private async Task StartAcceptanceRunAsync()
    {
        if (IsRunning) return;

        IsRunning = true;
        OverallVerdict = "RUNNING";
        StatusText = "Executing Acceptance Suite...";
        _acceptanceCts = new CancellationTokenSource();
        InitialiseStepList();

        try
        {
            if (AcceptanceRole == "Host")
            {
                StatusText = $"Host Acceptance Server listening on port {Port}... Waiting for client connection.";
                await HostServerRunner.RunHostAsync(new CliOptions { Port = Port }, _acceptanceCts.Token).ConfigureAwait(false);
            }
            else
            {
                StatusText = $"Connecting to Host at {HostAddress}:{Port} for Acceptance Test...";
                int exitCode = await ClientAcceptanceTestRunner.RunAcceptanceSuiteAsync(
                    hostIp: HostAddress,
                    hostPort: Port,
                    autoConfirm: false,
                    soakDurationSeconds: 30, // Default interactive soak
                    onStepCompleted: stepResult =>
                    {
                        _dispatcher.TryEnqueue(() =>
                        {
                            int idx = (int)stepResult.StepId;
                            if (idx >= 0 && idx < Steps.Count)
                            {
                                Steps[idx].Status = stepResult.Status == AcceptanceStepStatus.Passed ? "PASS" : "FAIL";
                                Steps[idx].Duration = $"{stepResult.DurationMs:F0} ms";
                                Steps[idx].EvidenceSummary = stepResult.EvidenceSummary;
                            }
                        });
                    },
                    humanPromptCallback: PromptOperatorObservationAsync,
                    ct: _acceptanceCts.Token).ConfigureAwait(false);

                _dispatcher.TryEnqueue(() =>
                {
                    OverallVerdict = exitCode == 0 ? "PASS" : "FAIL (EVALUATION REJECTED)";
                    StatusText = exitCode == 0 ? "Acceptance Suite PASSED" : "Acceptance Suite FAILED gatekeeper requirements";
                });
            }

            // Load generated report if present
            string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "ACCEPTANCE-REPORT.md");
            if (File.Exists(reportPath))
            {
                string md = File.ReadAllText(reportPath);
                _dispatcher.TryEnqueue(() => ReportMarkdown = md);
            }
        }
        // ALLOWED_EXCEPTION: Handle cancellation or network exceptions during acceptance test execution.
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
            {
                OverallVerdict = "ERROR";
                StatusText = $"Acceptance run encountered error: {ex.Message}";
            });
        }
        finally
        {
            _dispatcher.TryEnqueue(() => IsRunning = false);
        }
    }

    public Task<bool> PromptOperatorObservationAsync()
    {
        _observationTcs = new TaskCompletionSource<bool>();
        IsHumanPromptActive = true;
        return _observationTcs.Task;
    }

    [RelayCommand]
    private void ConfirmObservation()
    {
        IsHumanPromptActive = false;
        _observationTcs?.TrySetResult(VideoSharp && AudioAudible && InputResponsive);
    }

    [RelayCommand]
    private void DeclineObservation()
    {
        IsHumanPromptActive = false;
        _observationTcs?.TrySetResult(false);
    }

    public void Dispose()
    {
        _acceptanceCts?.Cancel();
        _acceptanceCts?.Dispose();
    }
}
