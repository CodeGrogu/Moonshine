using Moonshine.Core.Runtime;

namespace Moonshine.App;

public enum AppCommandType
{
    Help,
    Interactive,
    LegacyRole,
    Host,
    Client,
    Pair,
    Discover,
    Probe,
    Loopback,
    AcceptanceTest
}

public sealed record ClientHandshakeRequest(
    string ClientName,
    int ClientVideoPort,
    int ClientAudioPort,
    int ClientControlPort,
    uint DesiredWidth,
    uint DesiredHeight,
    uint Fps,
    uint BitrateKbps,
    bool EnableHdr10,
    string Codec = "hevc"
);

public sealed record HostHandshakeResponse(
    string Status,
    ulong SessionId,
    int HostVideoPort,
    int HostAudioPort,
    int HostControlPort
);

public sealed class CliOptions
{
    public AppCommandType Command { get; set; } = AppCommandType.Interactive;
    public ApplicationRole LegacyRole { get; set; } = ApplicationRole.None;

    // Host / Client common streaming parameters
    public string HostAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 48010;
    public string Codec { get; set; } = "hevc";
    public int BitrateKbps { get; set; } = 20000;
    public int Fps { get; set; } = 60;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public bool EnableHdr { get; set; }
    public bool UseVirtualAudio { get; set; }
    public int DisplayIndex { get; set; }

    // Acceptance Test parameters
    public bool AutoConfirm { get; set; }

    // Pairing parameters
    public string Pin { get; set; } = string.Empty;

    // Probe / Discovery / Loopback parameters
    public int TimeoutMs { get; set; } = 3000;
    public int DurationSeconds { get; set; } = 10;
    public bool Verbose { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        if (args.Length == 0)
        {
            options.Command = AppCommandType.Interactive;
            return options;
        }

        string verb = args[0].TrimStart('-').ToLowerInvariant();

        // Check for legacy --role argument
        if (verb == "role" || verb == "r")
        {
            if (args.Length > 1 && MoonshineApplication.TryParseRole(args, out ApplicationRole legacyRole))
            {
                options.Command = AppCommandType.LegacyRole;
                options.LegacyRole = legacyRole;
                return options;
            }
        }

        // Help commands
        if (verb is "help" or "h" or "?" or "/?")
        {
            options.Command = AppCommandType.Help;
            return options;
        }

        // Command routing
        switch (verb)
        {
            case "host":
                options.Command = AppCommandType.Host;
                break;
            case "client":
                options.Command = AppCommandType.Client;
                break;
            case "pair":
                options.Command = AppCommandType.Pair;
                break;
            case "discover":
                options.Command = AppCommandType.Discover;
                break;
            case "probe" or "info":
                options.Command = AppCommandType.Probe;
                break;
            case "loopback" or "benchmark":
                options.Command = AppCommandType.Loopback;
                break;
            case "test" or "acceptance":
                options.Command = AppCommandType.AcceptanceTest;
                break;
            case "interactive" or "tui" or "menu":
                options.Command = AppCommandType.Interactive;
                return options;
            default:
                options.Command = AppCommandType.Help;
                return options;
        }

        // Parse key-value flags for the command
        for (int i = 1; i < args.Length; i++)
        {
            string rawArg = args[i];
            string normalized = rawArg.TrimStart('-').ToLowerInvariant();
            string next = (i + 1 < args.Length) ? args[i + 1] : string.Empty;

            switch (normalized)
            {
                case "host" or "h" or "ip":
                    if (!string.IsNullOrWhiteSpace(next)) { options.HostAddress = next; i++; }
                    break;
                case "port" or "p":
                    if (int.TryParse(next, out int port)) { options.Port = port; i++; }
                    break;
                case "codec" or "c":
                    if (!string.IsNullOrWhiteSpace(next)) { options.Codec = next.ToLowerInvariant(); i++; }
                    break;
                case "bitrate" or "b":
                    if (int.TryParse(next, out int bitrate)) { options.BitrateKbps = bitrate; i++; }
                    break;
                case "fps" or "f":
                    if (int.TryParse(next, out int fps)) { options.Fps = fps; i++; }
                    break;
                case "res" or "resolution":
                    if (!string.IsNullOrWhiteSpace(next) && next.Contains('x'))
                    {
                        string[] parts = next.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        {
                            options.Width = w;
                            options.Height = h;
                        }
                        i++;
                    }
                    break;
                case "pin":
                    if (!string.IsNullOrWhiteSpace(next)) { options.Pin = next; i++; }
                    break;
                case "hdr":
                    options.EnableHdr = true;
                    break;
                case "virtual-audio" or "audio-driver":
                    options.UseVirtualAudio = true;
                    break;
                case "display" or "d":
                    if (int.TryParse(next, out int disp)) { options.DisplayIndex = disp; i++; }
                    break;
                case "timeout" or "t":
                    if (int.TryParse(next, out int timeout)) { options.TimeoutMs = timeout; i++; }
                    break;
                case "duration":
                    if (int.TryParse(next, out int duration)) { options.DurationSeconds = duration; i++; }
                    break;
                case "verbose" or "v":
                    options.Verbose = true;
                    break;
                case "auto-confirm" or "yes" or "y":
                    options.AutoConfirm = true;
                    break;
            }
        }

        return options;
    }
}
