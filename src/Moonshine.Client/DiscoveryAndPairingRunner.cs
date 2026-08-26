using System.Net;
using Moonshine.Core.Discovery;
using Moonshine.Core.Pairing;

namespace Moonshine.App;

public static class DiscoveryAndPairingRunner
{
    public static async Task RunDiscoveryAsync(CliOptions options, CancellationToken ct)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine LAN Host Discovery");
        Console.WriteLine("==========================================================");
        Console.WriteLine($"[*] Broadcasting discovery probe on port {options.Port} (timeout: {options.TimeoutMs} ms)...");

        await using var discovery = new MoonshineLanDiscoveryEngine(discoveryPort: options.Port);
        discovery.Start();

        try
        {
            // Send standard broadcast and multicast probes across all active network subnets
            await discovery.SendProbeAsync(cancellationToken: ct).ConfigureAwait(false);

            // If a specific host was targeted, also send a direct unicast probe
            if (!string.IsNullOrWhiteSpace(options.HostAddress) &&
                options.HostAddress != "127.0.0.1" &&
                IPAddress.TryParse(options.HostAddress, out var directIp))
            {
                Console.WriteLine($"[*] Sending direct unicast discovery probe to {directIp}:{options.Port}...");
                await discovery.SendProbeAsync(new IPEndPoint(directIp, options.Port), cancellationToken: ct).ConfigureAwait(false);
            }

            await Task.Delay(options.TimeoutMs, ct).ConfigureAwait(false);

            var hosts = discovery.ActiveHosts.ToList();

            if (hosts.Count == 0)
            {
                Console.WriteLine("\n[-] No Moonshine host servers responded on the local network.");
                Console.WriteLine($"    Ensure a host server is running (e.g. 'Moonshine host --port {options.Port}').");
                Console.WriteLine("    Note: Windows Firewall may require allowing UDP/TCP on the host.");
                return;
            }

            Console.WriteLine($"\n[+] Discovered {hosts.Count} Moonshine host(s):\n");
            for (int i = 0; i < hosts.Count; i++)
            {
                var h = hosts[i];
                Console.WriteLine($"  [{i + 1}] Host Name:    {h.Hostname}");
                Console.WriteLine($"      Address:      {h.EndpointAddress}:{h.ControlTcpPort}");
                Console.WriteLine($"      UUID:         {h.HostUuid}");
                Console.WriteLine($"      GPU Backend:  {h.GpuName}");
                Console.WriteLine($"      Capabilities: {h.Capabilities} (HDR10: {h.SupportsHdr10})");
                Console.WriteLine($"      Last Seen:    {h.LastSeenUtc:u}");
                Console.WriteLine();
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[*] Discovery cancelled.");
        }
        // ALLOWED_EXCEPTION: Report user-facing diagnostic error when LAN discovery probe fails.
        catch (Exception ex)
        {
            Console.WriteLine($"\n[-] Discovery error: {ex.Message}");
        }
    }

    public static async Task RunPairingAsync(CliOptions options, CancellationToken ct)
    {
        Console.WriteLine("==========================================================");
        Console.WriteLine("Moonshine Cryptographic Pairing");
        Console.WriteLine("==========================================================");

        string pin = options.Pin;
        if (string.IsNullOrWhiteSpace(pin))
        {
            Console.Write("Enter Host Pairing PIN (4 digits): ");
            pin = Console.ReadLine()?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            Console.WriteLine("[-] Error: A valid 4-digit PIN is required to pair with the host.");
            return;
        }

        Console.WriteLine($"[*] Initiating 5-step authenticated pairing exchange with {options.HostAddress}:{options.Port}...");
        var pairingManager = new MoonshinePairingManager();

        try
        {
            string clientUniqueId = Guid.NewGuid().ToString("N");
            var result = await pairingManager.PairAsync(options.HostAddress, options.Port, pin, clientUniqueId, ct).ConfigureAwait(false);

            if (result.Success)
            {
                Console.WriteLine("\n[+] Cryptographic Pairing Succeeded!");
                Console.WriteLine($"    Message: {result.Message}");
                Console.WriteLine("    Pairing certificate and session keys securely stored in SecureFileStore.");
            }
            else
            {
                Console.WriteLine($"\n[-] Pairing Failed: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[*] Pairing cancelled.");
        }
        // ALLOWED_EXCEPTION: Report user-facing diagnostic error when cryptographic pairing challenge fails.
        catch (Exception ex)
        {
            Console.WriteLine($"\n[-] Pairing error: {ex.Message}");
        }
    }
}
