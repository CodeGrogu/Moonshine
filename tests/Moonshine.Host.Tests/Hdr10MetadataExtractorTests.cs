using FluentAssertions;
using Moonshine.Host.Color;
using Moonshine.Interop;
using Xunit;

namespace Moonshine.Host.Tests;

public class Hdr10MetadataExtractorTests
{
    [Fact]
    public void Hdr10MetadataExtractor_TryExtractMetadata_ExecutesWithoutError()
    {
        bool success = Hdr10MetadataExtractor.TryExtractMetadata(IntPtr.Zero, out var metadata);
        // Returns true if output found, or false on headless
        if (success)
        {
            metadata.ColorSpace.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void Hdr10MetadataExtractor_ParseCapabilities_MatchesExpectedValues()
    {
        var meta = Hdr10MetadataExtractor.ParseCapabilities(12);
        meta.HdrEnabled.Should().Be(1);
        meta.ColorSpace.Should().Be(1);
        meta.MaxMasteringLuminance.Should().Be(10000000);
        meta.MaxContentLightLevel.Should().Be(1000);
    }

    [Fact]
    public unsafe void Hdr10MetadataExtractor_GenerateMasteringDisplaySeiPayload_CreatesExact28Bytes()
    {
        var meta = Hdr10MetadataExtractor.ParseCapabilities(12);
        byte[] payload = Hdr10MetadataExtractor.GenerateMasteringDisplaySeiPayload(meta);

        payload.Should().HaveCount(28);
        // Check green primary high byte
        payload[0].Should().Be((byte)(meta.GreenPrimary[0] >> 8));
        payload[1].Should().Be((byte)(meta.GreenPrimary[0] & 0xFF));
    }

    [Fact]
    public void Hdr10MetadataExtractor_FormatSdpHdrAttributes_GeneratesValidSdp()
    {
        var meta = Hdr10MetadataExtractor.ParseCapabilities(12);
        string sdp = Hdr10MetadataExtractor.FormatSdpHdrAttributes(meta, payloadType: 96);

        sdp.Should().Contain("color-primaries=9");
        sdp.Should().Contain("transfer-characteristics=16");
        sdp.Should().Contain("mastering-display-color-volume=");
        sdp.Should().Contain("content-light-level=");
    }
}
