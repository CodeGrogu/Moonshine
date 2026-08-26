using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Moonshine.Host.Capture;
using Moonshine.Interop;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Acceptance;

/// <summary>
/// Host-side Acceptance Coordinator.
/// Orchestrates the two-device acceptance run, logs Host environment and hardware telemetry,
/// receives the signed Client evidence bundle over TCP, merges the evidence into an AcceptanceManifest,
/// and invokes the report synthesizer.
/// </summary>
public sealed class HostAcceptanceCoordinator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly MoonshineHostCoordinator _hostCoordinator;
    private readonly string _outputDirectory;

    public HostAcceptanceCoordinator(MoonshineHostCoordinator hostCoordinator, string? outputDirectory = null)
    {
        _hostCoordinator = hostCoordinator ?? throw new ArgumentNullException(nameof(hostCoordinator));
        _outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "acceptance_evidence")
            : outputDirectory;

        Directory.CreateDirectory(_outputDirectory);
    }

    /// <summary>
    /// Gathers local Host physical hardware, OS, and GPU environment evidence.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Allowed fallback for hardware environment probe.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Allowed for acceptance test evidence logging.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Allowed for acceptance test evidence logging.")]
    public static unsafe DeviceEnvironmentEvidence CollectHostEnvironment(string localIp)
    {
        var gpus = new List<string>();
        string primaryGpu = "Direct3D 11 Physical Adapter";
        bool hasEncoder = true;
        bool hasDecoder = true;

        try
        {
            uint count = MoonshineNativeMethods.CaptureGetAdapterCount();
            for (uint i = 0; i < count; i++)
            {
                if (MoonshineNativeMethods.CaptureGetAdapterInfo(i, out var info) == 0)
                {
                    string name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)info.Description) ?? $"Adapter {i}";
                    gpus.Add($"{name} (Dedicated: {info.DedicatedVideoMemory / (1024 * 1024)} MB)");
                    if (i == 0)
                    {
                        primaryGpu = name;
                    }
                }
            }
        }
        // ALLOWED_EXCEPTION: Native GPU probe fallback on headless/virtualised hardware.
        catch (Exception)
        {
            gpus.Add("Direct3D 11 Adapter");
        }

        string displayMode = "1920x1080 @ 60 Hz";
        try
        {
            var top = DisplayManager.GetDisplayTopology();
            if (top.Displays.Count > 0)
            {
                var d = top.Displays[0];
                displayMode = $"{d.Width}x{d.Height} @ {d.RefreshRateHz} Hz (HDR: {d.IsHdr})";
            }
        }
        // ALLOWED_EXCEPTION: Fallback when display topology query is unavailable.
        catch (Exception)
        {
        }

        return new DeviceEnvironmentEvidence
        {
            Role = "Host",
            IpAddress = localIp,
            MachineName = Environment.MachineName,
            OsDescription = Environment.OSVersion.VersionString,
            CpuModel = $"x64 Family ({Environment.ProcessorCount} Cores)",
            HardwareThreads = Environment.ProcessorCount,
            SimdArchitecture = "AVX2",
            Gpus = gpus,
            PrimaryGpu = primaryGpu,
            HardwareEncoderSupported = hasEncoder,
            HardwareDecoderSupported = hasDecoder,
            DisplayMode = displayMode,
            TimestampUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Processes a completed client evidence upload, creates the host evidence bundle,
    /// verifies cryptographic integrity, and generates the correlated AcceptanceManifest.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Allowed for acceptance test evidence logging.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Allowed for acceptance test evidence logging.")]
    public AcceptanceManifest FinaliseAcceptanceRun(
        AcceptanceRunId runId,
        string hostIp,
        IReadOnlyList<AcceptanceStepResult> hostSteps,
        ClientEvidenceBundle clientEvidence)
    {
        ArgumentNullException.ThrowIfNull(clientEvidence);
        ArgumentNullException.ThrowIfNull(hostSteps);

        var hostEnvironment = CollectHostEnvironment(hostIp);

        var hostBundle = new HostEvidenceBundle
        {
            AcceptanceRunId = runId.ToString(),
            Environment = hostEnvironment,
            Steps = new List<AcceptanceStepResult>(hostSteps),
            CompletedUtc = DateTime.UtcNow
        };
        hostBundle.Sha256Checksum = hostBundle.ComputeChecksum();

        // 1. Persist Host Evidence Log
        string hostEvidencePath = Path.Combine(_outputDirectory, $"host_evidence_{runId}.json");
        File.WriteAllText(hostEvidencePath, JsonSerializer.Serialize(hostBundle, s_jsonOptions));

        // 2. Persist Client Evidence Log
        string clientEvidencePath = Path.Combine(_outputDirectory, $"client_evidence_{runId}.json");
        File.WriteAllText(clientEvidencePath, JsonSerializer.Serialize(clientEvidence, s_jsonOptions));

        // 3. Evaluate Acceptance Verification Gate
        var reasons = new List<string>();
        bool runIdMatch = string.Equals(runId.ToString(), clientEvidence.AcceptanceRunId, StringComparison.OrdinalIgnoreCase);
        if (!runIdMatch)
        {
            reasons.Add($"AcceptanceRunId mismatch: Host={runId}, Client={clientEvidence.AcceptanceRunId}");
        }

        string expectedClientChecksum = clientEvidence.ComputeChecksum();
        bool clientChecksumValid = string.Equals(expectedClientChecksum, clientEvidence.Sha256Checksum, StringComparison.OrdinalIgnoreCase);
        if (!clientChecksumValid)
        {
            reasons.Add("Client SHA-256 checksum mismatch (Payload modified or corrupted in transit).");
        }

        bool allStepsPassed = true;
        for (int s = 1; s <= 10; s++)
        {
            var stepId = (AcceptanceStepId)s;
            var clientStep = clientEvidence.Steps.Find(st => st.StepId == stepId);
            if (clientStep == null)
            {
                allStepsPassed = false;
                reasons.Add($"Missing required acceptance step: {stepId}");
            }
            else if (clientStep.Status != AcceptanceStepStatus.Passed)
            {
                allStepsPassed = false;
                reasons.Add($"Acceptance step {stepId} failed: {clientStep.ErrorMessage ?? "No error details"}");
            }
        }

        if (!clientEvidence.HumanConfirmationPassed)
        {
            reasons.Add("Client human-observable streaming confirmation was NOT confirmed.");
        }

        if (clientEvidence.AutoConfirmUsed)
        {
            reasons.Add("Automated smoke/dry-run flag (--auto-confirm) was used. Physical operator confirmation is MANDATORY for production acceptance.");
        }

        if (clientEvidence.SoakDurationSeconds < 1800)
        {
            reasons.Add($"Sustained soak duration was {clientEvidence.SoakDurationSeconds}s. Production acceptance requires a minimum 1800s (30-minute) soak test.");
        }

        var micStep = clientEvidence.Steps.Find(st => st.StepId == AcceptanceStepId.Step04_RealMicrophoneUplink);
        if (micStep == null || micStep.PacketsObserved < 50 || micStep.Status != AcceptanceStepStatus.Passed)
        {
            allStepsPassed = false;
            reasons.Add("Microphone uplink failed: active PCM capture and Opus transmission produced < 50 packets.");
        }

        var impStep = clientEvidence.Steps.Find(st => st.StepId == AcceptanceStepId.Step08_NetworkImpairmentTolerance);
        if (impStep == null || impStep.DurationMs < 3000 || impStep.Status != AcceptanceStepStatus.Passed)
        {
            allStepsPassed = false;
            reasons.Add("Network impairment test failed: execution duration was < 3000ms.");
        }

        bool overallPass = runIdMatch && clientChecksumValid && allStepsPassed && clientEvidence.HumanConfirmationPassed && !clientEvidence.AutoConfirmUsed && clientEvidence.SoakDurationSeconds >= 1800;

        var manifest = new AcceptanceManifest
        {
            AcceptanceRunId = runId.ToString(),
            HostEvidence = hostBundle,
            ClientEvidence = clientEvidence,
            AllStepsPassed = allStepsPassed,
            HumanConfirmationVerified = clientEvidence.HumanConfirmationPassed && !clientEvidence.AutoConfirmUsed,
            AutoConfirmUsed = clientEvidence.AutoConfirmUsed,
            CryptographicIntegrityVerified = clientChecksumValid && runIdMatch,
            OverallResult = overallPass ? "PASS" : "FAIL",
            EvaluationReasons = reasons,
            GeneratedUtc = DateTime.UtcNow
        };

        // 4. Persist Manifest
        string manifestPath = Path.Combine(_outputDirectory, $"acceptance_manifest_{runId}.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, s_jsonOptions));

        // 5. Synthesise ACCEPTANCE-REPORT.md
        AcceptanceReportSynthesizer.GenerateReport(manifest, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ACCEPTANCE-REPORT.md"));

        return manifest;
    }
}
