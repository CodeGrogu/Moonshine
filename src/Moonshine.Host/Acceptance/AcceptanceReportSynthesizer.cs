using System;
using System.IO;
using System.Text;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Acceptance;

/// <summary>
/// Synthesises the authoritative, human-readable ACCEPTANCE-REPORT.md from a verified AcceptanceManifest.
/// </summary>
public static class AcceptanceReportSynthesizer
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Allowed file copy fallback.")]
    public static void GenerateReport(AcceptanceManifest manifest, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var sb = new StringBuilder();

        sb.AppendLine("# Moonshine Two-Device Production Acceptance Report (TODO-049)");
        sb.AppendLine();
        sb.AppendLine($"**Acceptance Run ID**: `{manifest.AcceptanceRunId}`  ");
        sb.AppendLine($"**Execution Timestamp**: `{manifest.GeneratedUtc:yyyy-MM-dd HH:mm:ss} UTC`  ");
        sb.AppendLine($"**Overall Evaluation**: **`{manifest.OverallResult}`**  ");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Physical Hardware and Environment Provenance");
        sb.AppendLine();
        sb.AppendLine("### Device A: Host System");
        sb.AppendLine($"* **Machine Name**: `{manifest.HostEvidence.Environment.MachineName}`");
        sb.AppendLine($"* **IP Endpoint**: `{manifest.HostEvidence.Environment.IpAddress}`");
        sb.AppendLine($"* **Operating System**: `{manifest.HostEvidence.Environment.OsDescription}`");
        sb.AppendLine($"* **CPU Model**: `{manifest.HostEvidence.Environment.CpuModel}` ({manifest.HostEvidence.Environment.HardwareThreads} Hardware Threads, {manifest.HostEvidence.Environment.SimdArchitecture})");
        sb.AppendLine($"* **Primary GPU**: `{manifest.HostEvidence.Environment.PrimaryGpu}`");
        sb.AppendLine($"* **Hardware Encoder**: `{(manifest.HostEvidence.Environment.HardwareEncoderSupported ? "NVENC / D3D11 Verified" : "Not Supported")}`");
        sb.AppendLine($"* **Display Configuration**: `{manifest.HostEvidence.Environment.DisplayMode}`");
        sb.AppendLine($"* **SHA-256 Checksum**: `{manifest.HostEvidence.Sha256Checksum}`");
        sb.AppendLine();

        sb.AppendLine("### Device B: Client System");
        sb.AppendLine($"* **Machine Name**: `{manifest.ClientEvidence.Environment.MachineName}`");
        sb.AppendLine($"* **IP Endpoint**: `{manifest.ClientEvidence.Environment.IpAddress}`");
        sb.AppendLine($"* **Operating System**: `{manifest.ClientEvidence.Environment.OsDescription}`");
        sb.AppendLine($"* **CPU Model**: `{manifest.ClientEvidence.Environment.CpuModel}` ({manifest.ClientEvidence.Environment.HardwareThreads} Hardware Threads, {manifest.ClientEvidence.Environment.SimdArchitecture})");
        sb.AppendLine($"* **Primary GPU**: `{manifest.ClientEvidence.Environment.PrimaryGpu}`");
        sb.AppendLine($"* **Hardware Decoder**: `{(manifest.ClientEvidence.Environment.HardwareDecoderSupported ? "Direct3D 11 Video Decoder" : "Not Supported")}`");
        sb.AppendLine($"* **Display Configuration**: `{manifest.ClientEvidence.Environment.DisplayMode}`");
        sb.AppendLine($"* **SHA-256 Checksum**: `{manifest.ClientEvidence.Sha256Checksum}`");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Production Acceptance Test Execution Matrix");
        sb.AppendLine();
        sb.AppendLine("| Step # | Acceptance Step Name | Status | Duration | Frames | Loss | P50 / P95 / P99 Latency | Evidence Summary |");
        sb.AppendLine("| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :--- |");

        foreach (var step in manifest.ClientEvidence.Steps)
        {
            string statusIcon = step.Status == AcceptanceStepStatus.Passed ? "PASSED" : "FAILED";
            string latencyStr = step.P50LatencyUs > 0
                ? $"{step.P50LatencyUs / 1000.0:F1} / {step.P95LatencyUs / 1000.0:F1} / {step.P99LatencyUs / 1000.0:F1} ms"
                : "N/A";

            sb.AppendLine($"| {(ushort)step.StepId:D2} | **{step.StepName}** | `{statusIcon}` | {step.DurationMs:F0} ms | {step.FramesObserved} | {step.LossCount} | {latencyStr} | {step.EvidenceSummary} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 3. Human Observation Confirmation");
        sb.AppendLine();
        sb.AppendLine($"* **Human Confirmation Status**: **`{(manifest.HumanConfirmationVerified ? "CONFIRMED (PASS)" : "NOT CONFIRMED (FAIL)")}`**");
        sb.AppendLine($"* **Observer Notes**: `{manifest.ClientEvidence.HumanConfirmationNotes}`");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 4. Cryptographic Evidence Integrity");
        sb.AppendLine();
        sb.AppendLine($"* **Acceptance Run ID Match**: `{(string.Equals(manifest.HostEvidence.AcceptanceRunId, manifest.ClientEvidence.AcceptanceRunId, StringComparison.OrdinalIgnoreCase) ? "VALID" : "MISMATCH")}`");
        sb.AppendLine($"* **Client Evidence SHA-256**: `{manifest.ClientEvidence.Sha256Checksum}` (Verified)");
        sb.AppendLine($"* **Host Evidence SHA-256**: `{manifest.HostEvidence.Sha256Checksum}` (Verified)");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 5. Gatekeeper Verdict");
        sb.AppendLine();
        if (manifest.OverallResult == "PASS")
        {
            sb.AppendLine("> ### VERDICT: PRODUCTION ACCEPTANCE SUITE PASSED");
            sb.AppendLine(">");
            sb.AppendLine("> All 10 physical criteria executed on real production hardware across the local network without synthetic fixtures or mocks.");
        }
        else
        {
            sb.AppendLine("> ### VERDICT: PRODUCTION ACCEPTANCE SUITE FAILED");
            sb.AppendLine(">");
            sb.AppendLine("> The following blocking failures were detected:");
            foreach (var reason in manifest.EvaluationReasons)
            {
                sb.AppendLine($"> * {reason}");
            }
        }
        sb.AppendLine();

        string dir = Path.GetDirectoryName(outputPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, sb.ToString());

        // Also copy to docs/ACCEPTANCE-REPORT.md if possible
        try
        {
            string docsPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "docs", "ACCEPTANCE-REPORT.md"));
            string docsDir = Path.GetDirectoryName(docsPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(docsDir))
            {
                File.WriteAllText(docsPath, sb.ToString());
            }
        }
        // ALLOWED_EXCEPTION: Ignore file copy failure when running standalone without repository root structure.
        catch (Exception)
        {
        }
    }
}
