using BenchmarkDotNet.Attributes;
using Moonshine.Protocol.Contracts;
using Moonshine.Protocol.Discovery;

namespace Moonshine.Benchmarks;

[InProcess]
[MemoryDiagnoser]
public class DiscoveryBenchmarks
{
    private MoonshineDiscoveryProbePayload _probePayload;
    private MoonshineDiscoveryAnnouncementPayload _announcementPayload;
    private byte[] _probeBuffer = null!;
    private byte[] _announcementBuffer = null!;

    [GlobalSetup]
    public unsafe void Setup()
    {
        _probePayload = new MoonshineDiscoveryProbePayload
        {
            ClientVersionMajor = 1,
            ClientVersionMinor = 0,
            ClientUuid = new MoonshineUuid128(Guid.NewGuid()),
            DesiredCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.Hdr10,
            Reserved = 0,
            ProbeNonce = 0x1234567890ABCDEF
        };

        _announcementPayload = new MoonshineDiscoveryAnnouncementPayload
        {
            HostVersionMajor = 1,
            HostVersionMinor = 0,
            HostUuid = new MoonshineUuid128(Guid.NewGuid()),
            SupportedCapabilities = MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264 | MoonshineCapabilities.Hdr10 | MoonshineCapabilities.ReedSolomonFec,
            ControlTcpPort = 48010,
            DiscoveryUdpPort = 48010,
            VideoUdpPort = 47998,
            AudioUdpPort = 48000,
            ControlFeedbackUdpPort = 47999,
            MicUdpPort = 48002,
            MaxBitrateKbps = 150000,
            SupportsHdr10 = 1,
            SupportsVirtualAudio = 1,
            SupportsMicBackchannel = 1,
            IsPaired = 0,
            AdvertisementNonce = 0x9876543210FEDCBA
        };

        fixed (MoonshineDiscoveryAnnouncementPayload* p = &_announcementPayload)
        {
            MoonshineDiscoveryCodec.SetFixedUtf8String(p->Hostname, 64, "GAMING-HOST-RIG");
            MoonshineDiscoveryCodec.SetFixedUtf8String(p->GpuName, 64, "NVIDIA GeForce RTX 4090");
        }

        _probeBuffer = new byte[MoonshineDiscoveryCodec.ProbePacketSize];
        _announcementBuffer = new byte[MoonshineDiscoveryCodec.AnnouncementPacketSize];

        MoonshineDiscoveryCodec.TryWriteProbe(_probePayload, _probeBuffer, out _);
        MoonshineDiscoveryCodec.TryWriteAnnouncement(_announcementPayload, _announcementBuffer, out _);
    }

    [Benchmark]
    public bool DiscoveryCodec_WriteProbe_DirectHotPath()
    {
        return MoonshineDiscoveryCodec.TryWriteProbe(_probePayload, _probeBuffer, out _);
    }

    [Benchmark]
    public MoonshineErrorCode DiscoveryCodec_ReadProbe_DirectHotPath()
    {
        return MoonshineDiscoveryCodec.TryReadProbe(_probeBuffer, out _, out _);
    }

    [Benchmark]
    public bool DiscoveryCodec_WriteAnnouncement_DirectHotPath()
    {
        return MoonshineDiscoveryCodec.TryWriteAnnouncement(_announcementPayload, _announcementBuffer, out _);
    }

    [Benchmark]
    public MoonshineErrorCode DiscoveryCodec_ReadAnnouncement_DirectHotPath()
    {
        return MoonshineDiscoveryCodec.TryReadAnnouncementOrResponse(_announcementBuffer, out _, out _);
    }
}
