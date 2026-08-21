using System.Text;

namespace Moonshine.Core.Network;

public record FirewallRuleDefinition(
    string Name,
    string Protocol,
    int Port,
    string Direction = "Inbound",
    string Action = "Allow",
    string Description = "Moonshine Host streaming endpoint rule");

/// <summary>
/// Manages explicit, minimal, and reversible Windows Firewall configurations for Moonshine Host network endpoints.
/// </summary>
public static class MoonshineFirewallManager
{
    public const string RulePrefix = "Moonshine Host";

    /// <summary>
    /// Obtains the structured definitions of all required Windows Firewall rules for the host endpoints.
    /// Throws <see cref="InvalidOperationException"/> if invoked with an ephemeral testing configuration.
    /// </summary>
    public static IReadOnlyList<FirewallRuleDefinition> GetRequiredRules(HostEndpointConfig? config = null)
    {
        config ??= HostEndpointConfig.ProductionDefault;

        if (config.IsEphemeral)
        {
            throw new InvalidOperationException("Firewall rules cannot be generated for an ephemeral test endpoint configuration. Specify an explicit production or custom configuration with non-zero ports.");
        }

        return
        [
            new($"{RulePrefix} - Control (TCP {config.ControlTcpPort})", "TCP", config.ControlTcpPort, Description: "Moonshine Host Control & Session TCP Listener"),
            new($"{RulePrefix} - Discovery (UDP {config.DiscoveryUdpPort})", "UDP", config.DiscoveryUdpPort, Description: "Moonshine Host Discovery UDP Responder"),
            new($"{RulePrefix} - Video Stream (UDP {config.VideoUdpPort})", "UDP", config.VideoUdpPort, Description: "Moonshine Host Video RTP/MNBP Stream"),
            new($"{RulePrefix} - Control Feedback (UDP {config.ControlFeedbackUdpPort})", "UDP", config.ControlFeedbackUdpPort, Description: "Moonshine Host QoS Feedback & Loss Stats"),
            new($"{RulePrefix} - Audio Stream (UDP {config.AudioUdpPort})", "UDP", config.AudioUdpPort, Description: "Moonshine Host Audio RTP Stream"),
            new($"{RulePrefix} - Microphone Sink (UDP {config.MicUdpPort})", "UDP", config.MicUdpPort, Description: "Moonshine Host Microphone Input Sink")
        ];
    }

    /// <summary>
    /// Generates a PowerShell script to explicitly create minimal inbound firewall rules.
    /// </summary>
    public static string GenerateEnableFirewallScript(HostEndpointConfig? config = null, string programPath = "")
    {
        var rules = GetRequiredRules(config);
        var sb = new StringBuilder();
        sb.AppendLine("# Explicit Moonshine Host Firewall Configuration Script");
        sb.AppendLine("# Requires Administrator elevation");
        sb.AppendLine();

        foreach (var rule in rules)
        {
            string programArg = string.IsNullOrWhiteSpace(programPath) ? string.Empty : $" -Program \"{programPath}\"";
            sb.AppendLine($"New-NetFirewallRule -DisplayName \"{rule.Name}\" -Direction Inbound -Action Allow -Protocol {rule.Protocol} -LocalPort {rule.Port}{programArg} -Description \"{rule.Description}\"");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a PowerShell script to reversibly remove all Moonshine Host firewall rules.
    /// </summary>
    public static string GenerateDisableFirewallScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Reversible Moonshine Host Firewall Teardown Script");
        sb.AppendLine("# Requires Administrator elevation");
        sb.AppendLine();
        sb.AppendLine($"Get-NetFirewallRule -DisplayName \"{RulePrefix}*\" | Remove-NetFirewallRule");
        return sb.ToString();
    }
}
