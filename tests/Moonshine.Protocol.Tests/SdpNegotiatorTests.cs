using FluentAssertions;
using Moonshine.Protocol.RTSP;
using Xunit;

namespace Moonshine.Protocol.Tests;

public class SdpNegotiatorTests
{
    [Fact]
    public void BuildClientSdp_H264_1080p60_BuildsValidSdpLines()
    {
        var config = new MoonshineStreamConfiguration(
            Width: 1920,
            Height: 1080,
            FrameRate: 60,
            BitrateKbps: 20000,
            Codec: VideoCodec.H264,
            EnableHdr: false
        );

        string sdp = SdpNegotiator.BuildClientSdp(config);

        sdp.Should().Contain("m=video 47998 RTP/AVP 96");
        sdp.Should().Contain("a=rtpmap:96 H264/90000");
        sdp.Should().Contain("a=x-nv-video[0].clientViewportWd:1920");
        sdp.Should().Contain("a=x-nv-video[0].clientViewportHt:1080");
        sdp.Should().Contain("a=x-nv-video[0].fps:60");
        sdp.Should().Contain("a=x-nv-video[0].initialBitrateKbps:20000");
        sdp.Should().Contain("a=x-nv-video[0].dynamicRangeMode:0");
        sdp.Should().Contain("m=audio 48000 RTP/AVP 97");
    }

    [Fact]
    public void BuildClientSdp_Hevc_4K120_BuildsExpectedPayloadTypeAndAttributes()
    {
        var config = new MoonshineStreamConfiguration(
            Width: 3840,
            Height: 2160,
            FrameRate: 120,
            BitrateKbps: 80000,
            Codec: VideoCodec.Hevc
        );

        string sdp = SdpNegotiator.BuildClientSdp(config);

        sdp.Should().Contain("m=video 47998 RTP/AVP 98");
        sdp.Should().Contain("a=rtpmap:98 H265/90000");
        sdp.Should().Contain("a=x-nv-video[0].clientViewportWd:3840");
        sdp.Should().Contain("a=x-nv-video[0].clientViewportHt:2160");
        sdp.Should().Contain("a=x-nv-video[0].fps:120");
        sdp.Should().Contain("a=x-nv-video[0].initialBitrateKbps:80000");
    }

    [Fact]
    public void BuildClientSdp_Av1_1440p240_BuildsAv1PayloadType()
    {
        var config = new MoonshineStreamConfiguration(
            Width: 2560,
            Height: 1440,
            FrameRate: 240,
            BitrateKbps: 50000,
            Codec: VideoCodec.Av1
        );

        string sdp = SdpNegotiator.BuildClientSdp(config);

        sdp.Should().Contain("m=video 47998 RTP/AVP 100");
        sdp.Should().Contain("a=rtpmap:100 AV1/90000");
        sdp.Should().Contain("a=x-nv-video[0].fps:240");
    }

    [Fact]
    public void BuildClientSdp_Hdr10Enabled_EmbedsMasteringMetadata()
    {
        var hdr = new Hdr10MasteringMetadata(
            MaxMasteringLuminance: 1000,
            MinMasteringLuminance: 1,
            MaxCll: 1000,
            MaxFall: 400
        );

        var config = new MoonshineStreamConfiguration(
            Width: 3840,
            Height: 2160,
            FrameRate: 60,
            BitrateKbps: 60000,
            Codec: VideoCodec.Hevc,
            EnableHdr: true,
            HdrMetadata: hdr
        );

        string sdp = SdpNegotiator.BuildClientSdp(config);

        sdp.Should().Contain("a=x-nv-video[0].dynamicRangeMode:1");
        sdp.Should().Contain("a=x-nv-video[0].hdr.displayPrimaries:34000,16000,13250,34500,7500,3000");
        sdp.Should().Contain("a=x-nv-video[0].hdr.masteringLuminance:1000,1");
        sdp.Should().Contain("a=x-nv-video[0].hdr.maxCll:1000");
        sdp.Should().Contain("a=x-nv-video[0].hdr.maxFall:400");
    }

    [Fact]
    public void ParseServerSdp_ValidServerSdp_ExtractsPortsAndSessionId()
    {
        string serverSdp = """
            v=0
            o=Sunshine 0 0 IN IP4 192.168.1.50
            s=Sunshine Stream Session
            a=x-nv-session-id: sess-987654321
            m=video 47998 RTP/AVP 98
            c=IN IP4 192.168.1.50
            m=audio 48000 RTP/AVP 97
            c=IN IP4 192.168.1.50
            m=application 47999 RTP/AVP 101
            c=IN IP4 192.168.1.50
            """;

        var result = SdpNegotiator.ParseServerSdp(serverSdp);

        result.Success.Should().BeTrue();
        result.VideoPayloadType.Should().Be(98);
        result.AudioPayloadType.Should().Be(97);
        result.VideoPort.Should().Be(47998);
        result.AudioPort.Should().Be(48000);
        result.ControlPort.Should().Be(47999);
        result.SessionId.Should().Be("sess-987654321");
    }

    [Fact]
    public void ParseServerSdp_EmptyString_ReturnsFailure()
    {
        var result = SdpNegotiator.ParseServerSdp("");
        result.Success.Should().BeFalse();
    }
}
