using Moonshine.Core.Discovery;
using Moonshine.Core.Pairing;
using Moonshine.Interop;

namespace Moonshine.Client;

/// <summary>
/// High-level orchestration engine for Moonshine streaming client.
/// </summary>
public sealed class MoonshineClientEngine : IDisposable
{
    private readonly MoonshineDiscoveryService _discovery = new();
    private readonly MoonshinePairingManager _pairing = new();

    public async Task<HostServerInfo?> DiscoverHostAsync(string ip, int port = 47989)
    {
        return await _discovery.QueryServerInfoAsync(ip, port).ConfigureAwait(false);
    }

    public async Task<PairingResult> PairWithHostAsync(string ip, int port, string pin)
    {
        string uniqueId = Guid.NewGuid().ToString("N");
        return await _pairing.PairAsync(ip, port, pin, uniqueId).ConfigureAwait(false);
    }

    public static MoonshineDecoderCaps QueryHardwareCaps()
    {
        MoonshineNativeMethods.VideoQueryCaps(out var caps);
        return caps;
    }

    public void Dispose()
    {
        _discovery.Dispose();
    }
}
