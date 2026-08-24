using FluentAssertions;
using Moonshine.Core.Control;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Core.Tests;

/// <summary>
/// Comprehensive unit tests verifying client capability validation logic in <see cref="MoonshineRemoteHostControlClient.ValidateProposedConfiguration"/>.
/// Verifies valid configurations as well as deterministic rejection of out-of-bounds dimensions, refresh rates, bitrates, codecs, HDR, microphone, and virtual audio.
/// </summary>
public class ClientCapabilityValidationTests
{
    private static MoonshineHostCapabilitiesResponsePayload CreateDefaultCapabilities(
        uint supportedVideoCodecs = (uint)(MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264),
        uint maxEncodeWidth = 3840,
        uint maxEncodeHeight = 2160,
        uint maxEncodeFps = 144,
        byte supportsHdr10 = 1,
        byte supportsVirtualAudio = 1,
        byte supportsMicBackchannel = 1,
        uint maxBitrateKbps = 150000)
    {
        return new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = supportedVideoCodecs,
            SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
            MaxEncodeWidth = maxEncodeWidth,
            MaxEncodeHeight = maxEncodeHeight,
            MaxEncodeFps = maxEncodeFps,
            SupportsHdr10 = supportsHdr10,
            SupportsVirtualAudio = supportsVirtualAudio,
            SupportsMicBackchannel = supportsMicBackchannel,
            Reserved = 0,
            MaxBitrateKbps = maxBitrateKbps,
            Reserved2 = 0
        };
    }

    private static MoonshineHostConfigurationPayload CreateDefaultConfiguration(
        uint displayWidth = 1920,
        uint displayHeight = 1080,
        uint refreshRateHz = 60,
        uint targetBitrateKbps = 20000,
        uint maxBitrateKbps = 40000,
        MoonshineVideoCodec preferredCodec = MoonshineVideoCodec.Hevc,
        byte hdr10Enabled = 1,
        byte audioChannels = 2,
        uint audioBitrateKbps = 128,
        byte micPassthroughEnabled = 1,
        byte virtualAudioDriverEnabled = 1)
    {
        return new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 1,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight,
            RefreshRateHz = refreshRateHz,
            TargetBitrateKbps = targetBitrateKbps,
            MaxBitrateKbps = maxBitrateKbps,
            PreferredCodec = preferredCodec,
            Hdr10Enabled = hdr10Enabled,
            AudioChannels = audioChannels,
            AudioQualityMode = 0,
            AudioBitrateKbps = audioBitrateKbps,
            InputPollingRateHz = 1000,
            MicPassthroughEnabled = micPassthroughEnabled,
            VirtualAudioDriverEnabled = virtualAudioDriverEnabled,
            Reserved1 = 0,
            Reserved2 = 0,
            Reserved3 = 0
        };
    }

    [Fact]
    public void ValidateProposedConfiguration_ValidConfigurationMatchingCapabilities_PassesWithSuccess()
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration();

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        failureReason.Should().BeNull();
    }

    [Theory]
    [InlineData(MoonshineVideoCodec.Av1, (byte)0, (byte)2, 128u)]
    [InlineData(MoonshineVideoCodec.Hevc, (byte)1, (byte)6, 256u)]
    [InlineData(MoonshineVideoCodec.H264, (byte)0, (byte)8, 512u)]
    public void ValidateProposedConfiguration_VariousValidConfigurations_PassWithSuccess(
        MoonshineVideoCodec codec,
        byte hdr10,
        byte channels,
        uint audioBitrate)
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(
            preferredCodec: codec,
            hdr10Enabled: hdr10,
            audioChannels: channels,
            audioBitrateKbps: audioBitrate);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        failureReason.Should().BeNull();
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedWidthExceedsMaxEncodeWidth_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(maxEncodeWidth: 3840);
        var proposed = CreateDefaultConfiguration(displayWidth: 4096);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Requested display width (4096) exceeds maximum supported encode width (3840)");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedHeightExceedsMaxEncodeHeight_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(maxEncodeHeight: 2160);
        var proposed = CreateDefaultConfiguration(displayHeight: 2161);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Requested display height (2161) exceeds maximum supported encode height (2160)");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedRefreshRateExceedsMaxEncodeFps_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(maxEncodeFps: 144);
        var proposed = CreateDefaultConfiguration(refreshRateHz: 240);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Requested refresh rate (240 Hz) exceeds maximum supported encode frame rate (144 fps)");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedBitrateExceedsMaxBitrateKbps_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(maxBitrateKbps: 150000);
        var proposed = CreateDefaultConfiguration(targetBitrateKbps: 160000, maxBitrateKbps: 180000);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("exceeds maximum host bitrate capability (150000 kbps)");
    }

    [Fact]
    public void ValidateProposedConfiguration_TargetBitrateExceedsMaxBitrate_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(maxBitrateKbps: 150000);
        var proposed = CreateDefaultConfiguration(targetBitrateKbps: 50000, maxBitrateKbps: 40000);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Target bitrate (50000 kbps) cannot exceed maximum bitrate (40000 kbps)");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedUnsupportedCodec_FailsWithUnsupportedCodec()
    {
        // Host only advertises H264 support
        var capabilities = CreateDefaultCapabilities(supportedVideoCodecs: (uint)MoonshineCapabilities.H264);
        // Client proposes AV1
        var proposed = CreateDefaultConfiguration(preferredCodec: MoonshineVideoCodec.Av1);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.UnsupportedCodec);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Requested video codec (Av1) is not supported by host capabilities");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedHdr10WhenUnsupported_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(supportsHdr10: 0);
        var proposed = CreateDefaultConfiguration(hdr10Enabled: 1);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("HDR10 mode is requested but host hardware does not support HDR10 encoding");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedMicPassthroughWhenUnsupported_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(supportsMicBackchannel: 0);
        var proposed = CreateDefaultConfiguration(micPassthroughEnabled: 1);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Microphone passthrough backchannel is requested but host does not support microphone backchannel");
    }

    [Fact]
    public void ValidateProposedConfiguration_ProposedVirtualAudioDriverWhenUnsupported_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities(supportsVirtualAudio: 0);
        var proposed = CreateDefaultConfiguration(virtualAudioDriverEnabled: 1);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Virtual audio driver is requested but host does not support virtual audio driver");
    }

    [Theory]
    [InlineData(0u, 1080u)]
    [InlineData(1920u, 0u)]
    public void ValidateProposedConfiguration_ZeroDimensions_FailsWithInvalidConfigurationParameter(uint width, uint height)
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(displayWidth: width, displayHeight: height);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Display dimensions must be greater than zero");
    }

    [Fact]
    public void ValidateProposedConfiguration_ZeroRefreshRate_FailsWithInvalidConfigurationParameter()
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(refreshRateHz: 0);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Refresh rate must be greater than zero");
    }

    [Theory]
    [InlineData(0u, 20000u)]
    [InlineData(20000u, 0u)]
    public void ValidateProposedConfiguration_ZeroBitrate_FailsWithInvalidConfigurationParameter(uint targetBitrate, uint maxBitrate)
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(targetBitrateKbps: targetBitrate, maxBitrateKbps: maxBitrate);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Target and maximum bitrates must be greater than zero");
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    [InlineData((byte)7)]
    public void ValidateProposedConfiguration_InvalidAudioChannels_FailsWithInvalidConfigurationParameter(byte channels)
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(audioChannels: channels);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Only 2, 6, or 8 channels are supported");
    }

    [Theory]
    [InlineData(16u)]
    [InlineData(2048u)]
    public void ValidateProposedConfiguration_AudioBitrateOutOfRange_FailsWithInvalidConfigurationParameter(uint audioBitrate)
    {
        var capabilities = CreateDefaultCapabilities();
        var proposed = CreateDefaultConfiguration(audioBitrateKbps: audioBitrate);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        failureReason.Should().NotBeNullOrWhiteSpace();
        failureReason.Should().Contain("Allowed range is 32 to 1024 kbps");
    }

    [Theory]
    [InlineData(MoonshineVideoCodec.Av1, 1u << (int)MoonshineVideoCodec.Av1)]
    [InlineData(MoonshineVideoCodec.Hevc, 1u << (int)MoonshineVideoCodec.Hevc)]
    [InlineData(MoonshineVideoCodec.H264, 1u << (int)MoonshineVideoCodec.H264)]
    public void ValidateProposedConfiguration_DirectShiftedCodecMask_PassesValidation(MoonshineVideoCodec codec, uint codecMask)
    {
        var capabilities = CreateDefaultCapabilities(supportedVideoCodecs: codecMask);
        var proposed = CreateDefaultConfiguration(preferredCodec: codec);

        bool valid = MoonshineRemoteHostControlClient.ValidateProposedConfiguration(
            in proposed,
            in capabilities,
            out MoonshineErrorCode errorCode,
            out string? failureReason);

        valid.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        failureReason.Should().BeNull();
    }
}

