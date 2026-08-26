using Moonshine.Client;

namespace Moonshine.App;

public static class InteractiveMenuRunner
{
    public static async Task RunMenuAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine("Moonshine Ultra-Low-Latency Streaming Engine (Windows 11)");
            Console.WriteLine("==========================================================");
            Console.WriteLine("Select an operation mode to start testing:");
            Console.WriteLine("  [1] Start Host Streaming Server");
            Console.WriteLine("  [2] Connect as Client");
            Console.WriteLine("  [3] Run Two-Device Production Acceptance Suite (TODO-049)");
            Console.WriteLine("  [4] Discover Moonshine Hosts on LAN");
            Console.WriteLine("  [5] Pair with a Remote Host");
            Console.WriteLine("  [6] Run In-Process Loopback Performance Test");
            Console.WriteLine("  [7] Probe Hardware & Pipeline Capabilities");
            Console.WriteLine("  [8] Exit");
            Console.WriteLine("==========================================================");
            Console.Write("Enter selection (1-8): ");

            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var options = new CliOptions();

            switch (input)
            {
                case "1":
                    Console.Write("Enter Control Port [default 48010]: ");
                    string? portStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(portStr, out int port)) options.Port = port;
                    await HostServerRunner.RunHostAsync(options, ct).ConfigureAwait(false);
                    break;
                case "2":
                    Console.Write("Enter Host IP Address [default 127.0.0.1]: ");
                    string? hostStr = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(hostStr)) options.HostAddress = hostStr;
                    await ClientStreamRunner.RunClientAsync(options, ct).ConfigureAwait(false);
                    break;
                case "3":
                    Console.Write("Enter Host IP Address: ");
                    string? testHost = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(testHost)) options.HostAddress = testHost;
                    Console.Write("Enter Host Control Port [default 48010]: ");
                    string? testPortStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(testPortStr, out int testPort)) options.Port = testPort;
                    await ClientAcceptanceTestRunner.RunAcceptanceSuiteAsync(options.HostAddress, options.Port, autoConfirm: false, ct).ConfigureAwait(false);
                    break;
                case "4":
                    await DiscoveryAndPairingRunner.RunDiscoveryAsync(options, ct).ConfigureAwait(false);
                    break;
                case "5":
                    Console.Write("Enter Host IP Address: ");
                    string? pairHost = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pairHost)) options.HostAddress = pairHost;
                    await DiscoveryAndPairingRunner.RunPairingAsync(options, ct).ConfigureAwait(false);
                    break;
                case "6":
                    Console.Write("Enter Test Duration in Seconds [default 5]: ");
                    string? durStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(durStr, out int dur)) options.DurationSeconds = dur;
                    else options.DurationSeconds = 5;
                    await LoopbackTestRunner.RunLoopbackAsync(options, ct).ConfigureAwait(false);
                    break;
                case "7":
                    HardwareProbeRunner.Run(options);
                    break;
                case "8" or "q" or "exit":
                    return;
                default:
                    Console.WriteLine("[-] Invalid selection. Please choose an option from 1 to 8.\n");
                    break;
            }

            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadLine();
        }
    }
}
