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
            Console.WriteLine("  [3] Discover Moonshine Hosts on LAN");
            Console.WriteLine("  [4] Pair with a Remote Host");
            Console.WriteLine("  [5] Run In-Process Loopback Performance Test");
            Console.WriteLine("  [6] Probe Hardware & Pipeline Capabilities");
            Console.WriteLine("  [7] Exit");
            Console.WriteLine("==========================================================");
            Console.Write("Enter selection (1-7): ");

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
                    await DiscoveryAndPairingRunner.RunDiscoveryAsync(options, ct).ConfigureAwait(false);
                    break;
                case "4":
                    Console.Write("Enter Host IP Address: ");
                    string? pairHost = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pairHost)) options.HostAddress = pairHost;
                    await DiscoveryAndPairingRunner.RunPairingAsync(options, ct).ConfigureAwait(false);
                    break;
                case "5":
                    Console.Write("Enter Test Duration in Seconds [default 5]: ");
                    string? durStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(durStr, out int dur)) options.DurationSeconds = dur;
                    else options.DurationSeconds = 5;
                    await LoopbackTestRunner.RunLoopbackAsync(options, ct).ConfigureAwait(false);
                    break;
                case "6":
                    HardwareProbeRunner.Run(options);
                    break;
                case "7" or "q" or "exit":
                    return;
                default:
                    Console.WriteLine("[-] Invalid selection. Please choose an option from 1 to 7.\n");
                    break;
            }

            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadLine();
        }
    }
}
