using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moonshine.Protocol.Contracts;

/// <summary>
/// Distinct step identifiers for the 10 production acceptance criteria defined in TODO-049.
/// </summary>
public enum AcceptanceStepId : ushort
{
    None = 0,
    Step01_EnvironmentInventory = 1,
    Step02_RealVideoPipeline = 2,
    Step03_RealAudioPipeline = 3,
    Step04_RealMicrophoneUplink = 4,
    Step05_RealInputInjection = 5,
    Step06_RemoteHostConfiguration = 6,
    Step07_DisconnectReconnectRecovery = 7,
    Step08_NetworkImpairmentTolerance = 8,
    Step09_SustainedStreamingTelemetry = 9,
    Step10_HumanObservationConfirmation = 10
}

/// <summary>
/// Status of an individual acceptance test step.
/// </summary>
public enum AcceptanceStepStatus : byte
{
    Pending = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// Overall lifecycle state of an active two-device production acceptance run.
/// </summary>
public enum AcceptanceSessionState : byte
{
    Idle = 0,
    Initialising = 1,
    RunningSteps = 2,
    AwaitingHumanConfirmation = 3,
    CollectingEvidence = 4,
    UploadingEvidence = 5,
    EvaluatingReport = 6,
    Completed = 7,
    Failed = 8
}

/// <summary>
/// Universally unique identifier correlating Host and Client evidence logs for a single acceptance test run.
/// </summary>
public readonly struct AcceptanceRunId : IEquatable<AcceptanceRunId>
{
    public string Value { get; }

    public AcceptanceRunId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value) ? Generate().Value : value.Trim();
    }

    public static AcceptanceRunId Generate()
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string shortGuid = Guid.NewGuid().ToString("N")[..8];
        return new AcceptanceRunId($"acc-{timestamp}-{shortGuid}");
    }

    public bool Equals(AcceptanceRunId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is AcceptanceRunId other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(AcceptanceRunId left, AcceptanceRunId right) => left.Equals(right);
    public static bool operator !=(AcceptanceRunId left, AcceptanceRunId right) => !left.Equals(right);
}

/// <summary>
/// Hardware, OS, and toolchain provenance gathered from a physical device during acceptance testing.
/// </summary>
public sealed record DeviceEnvironmentEvidence
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "Unknown"; // "Host" or "Client"

    [JsonPropertyName("ip_address")]
    public string IpAddress { get; init; } = string.Empty;

    [JsonPropertyName("machine_name")]
    public string MachineName { get; init; } = Environment.MachineName;

    [JsonPropertyName("os_description")]
    public string OsDescription { get; init; } = Environment.OSVersion.VersionString;

    [JsonPropertyName("cpu_model")]
    public string CpuModel { get; init; } = "Physical CPU";

    [JsonPropertyName("hardware_threads")]
    public int HardwareThreads { get; init; } = Environment.ProcessorCount;

    [JsonPropertyName("simd_architecture")]
    public string SimdArchitecture { get; init; } = "AVX2";

    [JsonPropertyName("gpus")]
    public List<string> Gpus { get; init; } = [];

    [JsonPropertyName("primary_gpu")]
    public string PrimaryGpu { get; init; } = string.Empty;

    [JsonPropertyName("hardware_encoder_supported")]
    public bool HardwareEncoderSupported { get; init; }

    [JsonPropertyName("hardware_decoder_supported")]
    public bool HardwareDecoderSupported { get; init; }

    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; init; } = string.Empty;

    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed result metrics and logs recorded for an individual acceptance test step.
/// </summary>
public sealed record AcceptanceStepResult
{
    [JsonPropertyName("step_id")]
    public AcceptanceStepId StepId { get; init; }

    [JsonPropertyName("step_name")]
    public string StepName { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public AcceptanceStepStatus Status { get; init; } = AcceptanceStepStatus.Pending;

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; init; }

    [JsonPropertyName("frames_observed")]
    public ulong FramesObserved { get; init; }

    [JsonPropertyName("packets_observed")]
    public ulong PacketsObserved { get; init; }

    [JsonPropertyName("loss_count")]
    public ulong LossCount { get; init; }

    [JsonPropertyName("p50_latency_us")]
    public double P50LatencyUs { get; init; }

    [JsonPropertyName("p95_latency_us")]
    public double P95LatencyUs { get; init; }

    [JsonPropertyName("p99_latency_us")]
    public double P99LatencyUs { get; init; }

    [JsonPropertyName("average_jitter_us")]
    public double AverageJitterUs { get; init; }

    [JsonPropertyName("bitrate_kbps")]
    public double BitrateKbps { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("evidence_summary")]
    public string EvidenceSummary { get; init; } = string.Empty;

    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Evidence bundle compiled by the Client device during the acceptance test run.
/// </summary>
public sealed record ClientEvidenceBundle
{
    [JsonPropertyName("acceptance_run_id")]
    public string AcceptanceRunId { get; init; } = string.Empty;

    [JsonPropertyName("environment")]
    public DeviceEnvironmentEvidence Environment { get; init; } = new();

    [JsonPropertyName("steps")]
    public List<AcceptanceStepResult> Steps { get; init; } = [];

    [JsonPropertyName("human_confirmation_passed")]
    public bool HumanConfirmationPassed { get; init; }

    [JsonPropertyName("human_confirmation_notes")]
    public string HumanConfirmationNotes { get; init; } = string.Empty;

    [JsonPropertyName("auto_confirm_used")]
    public bool AutoConfirmUsed { get; init; }

    [JsonPropertyName("soak_duration_seconds")]
    public int SoakDurationSeconds { get; init; }

    [JsonPropertyName("completed_utc")]
    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("sha256_checksum")]
    public string Sha256Checksum { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    public string ComputeChecksum()
    {
        string json = JsonSerializer.Serialize(this with { Sha256Checksum = string.Empty }, s_jsonOptions);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// Evidence bundle compiled by the Host device during the acceptance test run.
/// </summary>
public sealed record HostEvidenceBundle
{
    [JsonPropertyName("acceptance_run_id")]
    public string AcceptanceRunId { get; init; } = string.Empty;

    [JsonPropertyName("environment")]
    public DeviceEnvironmentEvidence Environment { get; init; } = new();

    [JsonPropertyName("steps")]
    public List<AcceptanceStepResult> Steps { get; init; } = [];

    [JsonPropertyName("completed_utc")]
    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("sha256_checksum")]
    public string Sha256Checksum { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    public string ComputeChecksum()
    {
        string json = JsonSerializer.Serialize(this with { Sha256Checksum = string.Empty }, s_jsonOptions);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// Final correlated acceptance manifest produced by merging Host and Client evidence.
/// </summary>
public sealed record AcceptanceManifest
{
    [JsonPropertyName("acceptance_run_id")]
    public string AcceptanceRunId { get; init; } = string.Empty;

    [JsonPropertyName("host_evidence")]
    public HostEvidenceBundle HostEvidence { get; init; } = new();

    [JsonPropertyName("client_evidence")]
    public ClientEvidenceBundle ClientEvidence { get; init; } = new();

    [JsonPropertyName("all_steps_passed")]
    public bool AllStepsPassed { get; init; }

    [JsonPropertyName("human_confirmation_verified")]
    public bool HumanConfirmationVerified { get; init; }

    [JsonPropertyName("auto_confirm_used")]
    public bool AutoConfirmUsed { get; init; }

    [JsonPropertyName("cryptographic_integrity_verified")]
    public bool CryptographicIntegrityVerified { get; init; }

    [JsonPropertyName("overall_result")]
    public string OverallResult { get; init; } = "FAIL"; // "PASS" or "FAIL"

    [JsonPropertyName("evaluation_reasons")]
    public List<string> EvaluationReasons { get; init; } = [];

    [JsonPropertyName("generated_utc")]
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
}
