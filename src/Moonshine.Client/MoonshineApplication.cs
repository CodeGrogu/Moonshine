using Moonshine.Core.Runtime;
using Moonshine.Host;

namespace Moonshine.App;

public readonly record struct ApplicationStartResult(bool IsStarted, string Message);

/// <summary>
/// Single Windows application composition root. Coordinates the unified executable's runtime lifecycle,
/// delegating role activations to the <see cref="IRuntimeCoordinator"/> while preserving fail-closed baseline.
/// </summary>
public sealed class MoonshineApplication : IDisposable
{
    private readonly IRuntimeCoordinator _coordinator;

    public MoonshineApplication()
        : this(new MoonshineRuntimeCoordinator(
            hostServiceFactory: () => new MoonshineHostCoordinator(),
            clientServiceFactory: () => new MoonshineClientCoordinator()))
    {
    }

    public MoonshineApplication(IRuntimeCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public IRuntimeCoordinator Coordinator => _coordinator;

    public static bool TryParseRole(string[] arguments, out ApplicationRole role)
    {
        role = ApplicationRole.None;
        if (arguments.Length != 2 || !string.Equals(arguments[0], "--role", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        role = arguments[1].ToLowerInvariant() switch
        {
            "host" => ApplicationRole.Host,
            "client" => ApplicationRole.Client,
            "host-client" => ApplicationRole.HostAndClient,
            _ => ApplicationRole.None
        };
        return role != ApplicationRole.None;
    }

    public ApplicationStartResult Start(ApplicationRole role)
    {
        RoleTransitionResult result = _coordinator.StartAsync(role).AsTask().GetAwaiter().GetResult();
        return new ApplicationStartResult(
            IsStarted: result.State == RuntimeState.Running,
            result.Message ?? "Streaming role transitioned.");
    }

    public async Task<int> RunAsync(CliOptions options, CancellationToken ct)
    {
        switch (options.Command)
        {
            case AppCommandType.Help:
                PrintHelp();
                return 0;

            case AppCommandType.Interactive:
                await InteractiveMenuRunner.RunMenuAsync(ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.LegacyRole:
                ApplicationStartResult legacyResult = Start(options.LegacyRole);
                Console.WriteLine($"Moonshine role: {options.LegacyRole.FormatRole()}");
                Console.WriteLine(legacyResult.Message);
                return legacyResult.IsStarted ? 0 : 1;

            case AppCommandType.Probe:
                HardwareProbeRunner.Run(options);
                return 0;

            case AppCommandType.Discover:
                await DiscoveryAndPairingRunner.RunDiscoveryAsync(options, ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.Pair:
                await DiscoveryAndPairingRunner.RunPairingAsync(options, ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.Host:
                await HostServerRunner.RunHostAsync(options, ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.Client:
                await ClientStreamRunner.RunClientAsync(options, ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.Loopback:
                await LoopbackTestRunner.RunLoopbackAsync(options, ct).ConfigureAwait(false);
                return 0;

            case AppCommandType.AcceptanceTest:
                int soakSecs = options.SmokeMode ? 30 : options.SoakDurationSeconds;
                return await ClientAcceptanceTestRunner.RunAcceptanceSuiteAsync(
                    options.HostAddress,
                    options.Port,
                    options.AutoConfirm,
                    soakSecs,
                    ct).ConfigureAwait(false);

            default:
                PrintHelp();
                return 1;
        }
    }

    public static void PrintHelp()
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine Ultra-Low-Latency Streaming Engine (Windows 11)");
        Console.WriteLine("==========================================================");
        Console.WriteLine("Usage: Moonshine [command] [options]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  host         Start the host streaming server and discovery advertiser");
        Console.WriteLine("  client       Connect to a Moonshine host server and stream video/audio");
        Console.WriteLine("  test         Execute two-device production acceptance test suite (TODO-049)");
        Console.WriteLine("  discover     Search for Moonshine host servers on the local network");
        Console.WriteLine("  pair         Execute cryptographic pairing with a Moonshine host");
        Console.WriteLine("  loopback     Run in-process host + client loopback streaming benchmark");
        Console.WriteLine("  probe        Inspect physical GPU, encoder, decoder, audio, and SIMD hardware");
        Console.WriteLine("  interactive  Launch interactive console menu (default when no args provided)\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --host <ip>          Target host IP address (default: 127.0.0.1)");
        Console.WriteLine("  --port <int>         Host control port (default: 48010)");
        Console.WriteLine("  --auto-confirm, -y   Automatically confirm human observation step in test runner");
        Console.WriteLine("  --codec <name>       Video codec: hevc | h264 | av1 (default: hevc)");
        Console.WriteLine("  --bitrate <kbps>     Streaming bitrate in Kbps (default: 20000)");
        Console.WriteLine("  --fps <int>          Target frame rate (default: 60)");
        Console.WriteLine("  --res <WxH>          Target resolution e.g. 1920x1080 (default: 1920x1080)");
        Console.WriteLine("  --hdr                Enable HDR10 colorimetry pipeline");
        Console.WriteLine("  --virtual-audio      Use dedicated virtual audio driver sink");
        Console.WriteLine("  --pin <string>       Pairing PIN code (for pair command)");
        Console.WriteLine("  --duration <sec>     Benchmark duration in seconds (for loopback command)");
        Console.WriteLine("  --timeout <ms>       Discovery timeout in milliseconds (default: 3000)");
        Console.WriteLine("  --help, -h           Show this help text");
        Console.WriteLine("==========================================================\n");
    }

    public void Dispose() => _coordinator.Dispose();
}
