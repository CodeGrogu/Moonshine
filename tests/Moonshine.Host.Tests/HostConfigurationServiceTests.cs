using FluentAssertions;
using Moonshine.Core.Security;
using Moonshine.Host.Capture;
using Moonshine.Host.Control;
using Moonshine.Protocol.Contracts;
using Xunit;

namespace Moonshine.Host.Tests;

public class HostConfigurationServiceTests
{
    [Fact]
    public void Constructor_DefaultInitialization_SetsCanonicalDefaults()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);

        service.ConfigVersion.Should().Be(1);
        service.Capabilities.MaxEncodeWidth.Should().Be(3840);
        service.Capabilities.MaxEncodeHeight.Should().Be(2160);
        service.Capabilities.MaxEncodeFps.Should().Be(240);
        service.Capabilities.SupportsHdr10.Should().Be(1);
        service.Capabilities.SupportsVirtualAudio.Should().Be(1);
        service.Capabilities.SupportsMicBackchannel.Should().Be(1);
        service.Capabilities.MaxBitrateKbps.Should().Be(150000);

        MoonshineHostConfigurationPayload current = service.CurrentConfiguration;
        current.ConfigVersion.Should().Be(1);
        current.DisplayWidth.Should().Be(1920);
        current.DisplayHeight.Should().Be(1080);
        current.RefreshRateHz.Should().Be(60);
        current.TargetBitrateKbps.Should().Be(20000);
        current.MaxBitrateKbps.Should().Be(50000);
        current.PreferredCodec.Should().Be(MoonshineVideoCodec.Hevc);
        current.Hdr10Enabled.Should().Be(0);
        current.AudioChannels.Should().Be(2);
        current.AudioBitrateKbps.Should().Be(128);
        current.MicPassthroughEnabled.Should().Be(1);
        current.VirtualAudioDriverEnabled.Should().Be(1);
        current.Reserved1.Should().Be(0);
        current.Reserved2.Should().Be(0);
        current.Reserved3.Should().Be(0);
    }

    [Fact]
    public void Constructor_Parameterless_InitialisesWithProbedLiveCapabilities()
    {
        var service = new HostConfigurationService();

        service.ConfigVersion.Should().Be(1);
        service.Capabilities.MaxBitrateKbps.Should().Be(150000);
        service.Capabilities.SupportedAudioCodecs.Should().Be((uint)MoonshineAudioCodec.Opus);
        service.CurrentConfiguration.DisplayWidth.Should().BeGreaterThan(0);
        service.CurrentConfiguration.DisplayHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Constructor_CustomCapabilitiesAndConfiguration_InitialisesCorrectly()
    {
        var capabilities = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.Av1,
            SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
            MaxEncodeWidth = 2560,
            MaxEncodeHeight = 1440,
            MaxEncodeFps = 144,
            SupportsHdr10 = 0,
            SupportsVirtualAudio = 0,
            SupportsMicBackchannel = 0,
            MaxBitrateKbps = 80000
        };

        var initialConfig = new MoonshineHostConfigurationPayload
        {
            ConfigVersion = 10,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            RefreshRateHz = 144,
            TargetBitrateKbps = 40000,
            MaxBitrateKbps = 75000,
            PreferredCodec = MoonshineVideoCodec.Av1,
            Hdr10Enabled = 0,
            AudioChannels = 6,
            AudioBitrateKbps = 256,
            MicPassthroughEnabled = 0,
            VirtualAudioDriverEnabled = 0,
            Reserved1 = 999
        };

        var service = new HostConfigurationService(capabilities, initialConfig);

        service.ConfigVersion.Should().Be(10);
        service.Capabilities.MaxEncodeWidth.Should().Be(2560);
        service.Capabilities.SupportsHdr10.Should().Be(0);

        MoonshineHostConfigurationPayload current = service.CurrentConfiguration;
        current.ConfigVersion.Should().Be(10);
        current.DisplayWidth.Should().Be(2560);
        current.DisplayHeight.Should().Be(1440);
        current.PreferredCodec.Should().Be(MoonshineVideoCodec.Av1);
        current.AudioChannels.Should().Be(6);
        current.Reserved1.Should().Be(0); // Reserved fields must be zeroed
    }

    [Fact]
    public void TryApplyConfiguration_ValidMutation_IncrementsVersionAndDispatchesEvent()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        uint initialVersion = service.ConfigVersion;

        MoonshineHostConfigurationPayload proposed = service.CurrentConfiguration;
        proposed.DisplayWidth = 2560;
        proposed.DisplayHeight = 1440;
        proposed.RefreshRateHz = 120;
        proposed.TargetBitrateKbps = 35000;
        proposed.MaxBitrateKbps = 60000;
        proposed.PreferredCodec = MoonshineVideoCodec.Av1;
        proposed.Hdr10Enabled = 1;
        proposed.AudioChannels = 6;
        proposed.AudioBitrateKbps = 320;

        MoonshineHostConfigurationPayload? eventPayload = null;
        uint eventVersion = 0;
        int eventCount = 0;

        service.ConfigurationApplied += (payload, version) =>
        {
            eventPayload = payload;
            eventVersion = version;
            eventCount++;
        };

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        errorMessage.Should().BeNull();

        effective.ConfigVersion.Should().Be(initialVersion + 1);
        effective.DisplayWidth.Should().Be(2560);
        effective.DisplayHeight.Should().Be(1440);
        effective.RefreshRateHz.Should().Be(120);
        effective.TargetBitrateKbps.Should().Be(35000);
        effective.MaxBitrateKbps.Should().Be(60000);
        effective.PreferredCodec.Should().Be(MoonshineVideoCodec.Av1);
        effective.Hdr10Enabled.Should().Be(1);
        effective.AudioChannels.Should().Be(6);
        effective.AudioBitrateKbps.Should().Be(320);

        service.ConfigVersion.Should().Be(initialVersion + 1);
        service.CurrentConfiguration.DisplayWidth.Should().Be(2560);

        eventCount.Should().Be(1);
        eventVersion.Should().Be(initialVersion + 1);
        eventPayload.Should().NotBeNull();
        eventPayload!.Value.DisplayWidth.Should().Be(2560);
    }

    [Fact]
    public void TryApplyConfiguration_ControllerAuthorisation_IsPermitted()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload proposed = service.CurrentConfiguration;
        proposed.RefreshRateHz = 144;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Controller,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        errorMessage.Should().BeNull();
        effective.RefreshRateHz.Should().Be(144);
    }

    [Theory]
    [InlineData(AuthorisationLevel.None)]
    [InlineData(AuthorisationLevel.Viewer)]
    public void TryApplyConfiguration_InsufficientAuthorisation_RejectsAndRetainsState(AuthorisationLevel authLevel)
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.DisplayWidth = 2560;
        proposed.DisplayHeight = 1440;

        bool eventFired = false;
        service.ConfigurationApplied += (_, _) => eventFired = true;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            authLevel,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.UnauthorizedConfiguration);
        errorMessage.Should().NotBeNullOrWhiteSpace();
        eventFired.Should().BeFalse();

        effective.DisplayWidth.Should().Be(baseline.DisplayWidth);
        effective.ConfigVersion.Should().Be(baseline.ConfigVersion);

        service.CurrentConfiguration.DisplayWidth.Should().Be(baseline.DisplayWidth);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_OutOfBoundsResolution_RejectsAtomically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.DisplayWidth = 8000;
        proposed.DisplayHeight = 5000;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("exceeds maximum supported encode width");

        effective.DisplayWidth.Should().Be(baseline.DisplayWidth);
        effective.DisplayHeight.Should().Be(baseline.DisplayHeight);
        service.CurrentConfiguration.DisplayWidth.Should().Be(baseline.DisplayWidth);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_ZeroDimension_RejectsAtomically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.DisplayWidth = 0;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("greater than zero");
        effective.DisplayWidth.Should().Be(baseline.DisplayWidth);
    }

    [Fact]
    public void TryApplyConfiguration_OutOfBoundsFps_RejectsAtomically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.RefreshRateHz = 500;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("exceeds maximum supported encode frame rate");

        effective.RefreshRateHz.Should().Be(baseline.RefreshRateHz);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_OutOfBoundsBitrate_RejectsAtomically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.TargetBitrateKbps = 500000;
        proposed.MaxBitrateKbps = 600000;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("exceeds maximum host bitrate capability");

        effective.TargetBitrateKbps.Should().Be(baseline.TargetBitrateKbps);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_TargetBitrateExceedingMaxBitrate_RejectsAtomically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.TargetBitrateKbps = 60000;
        proposed.MaxBitrateKbps = 40000;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("cannot exceed maximum bitrate");

        effective.TargetBitrateKbps.Should().Be(baseline.TargetBitrateKbps);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_UnsupportedVideoCodec_RejectsAtomically()
    {
        var capabilities = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.H264,
            MaxEncodeWidth = 1920,
            MaxEncodeHeight = 1080,
            MaxEncodeFps = 60,
            MaxBitrateKbps = 50000,
            SupportsHdr10 = 1,
            SupportsMicBackchannel = 1,
            SupportsVirtualAudio = 1
        };

        var service = new HostConfigurationService(capabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.PreferredCodec = MoonshineVideoCodec.Av1;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.UnsupportedCodec);
        errorMessage.Should().Contain("not supported by host capabilities");

        effective.PreferredCodec.Should().Be(baseline.PreferredCodec);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_UnsupportedHdr10_RejectsAtomically()
    {
        var capabilities = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)(MoonshineCapabilities.H264 | MoonshineCapabilities.Hevc),
            MaxEncodeWidth = 1920,
            MaxEncodeHeight = 1080,
            MaxEncodeFps = 60,
            MaxBitrateKbps = 50000,
            SupportsHdr10 = 0,
            SupportsMicBackchannel = 1,
            SupportsVirtualAudio = 1
        };

        var service = new HostConfigurationService(capabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.Hdr10Enabled = 1;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("host hardware does not support HDR10 encoding");

        effective.Hdr10Enabled.Should().Be(baseline.Hdr10Enabled);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    public void TryApplyConfiguration_InvalidAudioChannels_RejectsAtomically(byte invalidChannels)
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.AudioChannels = invalidChannels;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("Audio channel count");

        effective.AudioChannels.Should().Be(baseline.AudioChannels);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public void TryApplyConfiguration_ValidAudioChannels_AcceptedSuccessfully(byte validChannels)
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);

        MoonshineHostConfigurationPayload proposed = service.CurrentConfiguration;
        proposed.AudioChannels = validChannels;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        errorMessage.Should().BeNull();
        effective.AudioChannels.Should().Be(validChannels);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(1025)]
    [InlineData(2000)]
    public void TryApplyConfiguration_InvalidAudioBitrate_RejectsAtomically(uint invalidBitrate)
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.AudioBitrateKbps = invalidBitrate;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("Audio bitrate");

        effective.AudioBitrateKbps.Should().Be(baseline.AudioBitrateKbps);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_UnsupportedMicBackchannel_RejectsAtomically()
    {
        var capabilities = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.Hevc,
            MaxEncodeWidth = 1920,
            MaxEncodeHeight = 1080,
            MaxEncodeFps = 60,
            MaxBitrateKbps = 50000,
            SupportsMicBackchannel = 0,
            SupportsVirtualAudio = 1
        };

        var service = new HostConfigurationService(capabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.MicPassthroughEnabled = 1;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("host does not support microphone backchannel");

        effective.MicPassthroughEnabled.Should().Be(baseline.MicPassthroughEnabled);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void TryApplyConfiguration_UnsupportedVirtualAudio_RejectsAtomically()
    {
        var capabilities = new MoonshineHostCapabilitiesResponsePayload
        {
            SupportedVideoCodecs = (uint)MoonshineCapabilities.Hevc,
            MaxEncodeWidth = 1920,
            MaxEncodeHeight = 1080,
            MaxEncodeFps = 60,
            MaxBitrateKbps = 50000,
            SupportsMicBackchannel = 1,
            SupportsVirtualAudio = 0
        };

        var service = new HostConfigurationService(capabilities);
        MoonshineHostConfigurationPayload baseline = service.CurrentConfiguration;

        MoonshineHostConfigurationPayload proposed = baseline;
        proposed.VirtualAudioDriverEnabled = 1;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeFalse();
        errorCode.Should().Be(MoonshineErrorCode.InvalidConfigurationParameter);
        errorMessage.Should().Contain("host does not support virtual audio driver");

        effective.VirtualAudioDriverEnabled.Should().Be(baseline.VirtualAudioDriverEnabled);
        service.ConfigVersion.Should().Be(baseline.ConfigVersion);
    }

    [Fact]
    public void GetSanitizedConfiguration_AlwaysReturnsZeroedReservedFields()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);

        MoonshineHostConfigurationPayload proposed = service.CurrentConfiguration;
        proposed.DisplayWidth = 2560;
        proposed.DisplayHeight = 1440;
        proposed.Reserved1 = 0xDEADBEEF;
        proposed.Reserved2 = 0xCAFEBABE;
        proposed.Reserved3 = 0x12345678;

        bool applied = service.TryApplyConfiguration(
            in proposed,
            AuthorisationLevel.Administrator,
            out MoonshineHostConfigurationPayload effective,
            out MoonshineErrorCode errorCode,
            out string? errorMessage);

        applied.Should().BeTrue();
        errorCode.Should().Be(MoonshineErrorCode.Success);
        errorMessage.Should().BeNull();

        effective.Reserved1.Should().Be(0);
        effective.Reserved2.Should().Be(0);
        effective.Reserved3.Should().Be(0);

        MoonshineHostConfigurationPayload sanitized = service.GetSanitizedConfiguration();
        sanitized.DisplayWidth.Should().Be(2560);
        sanitized.DisplayHeight.Should().Be(1440);
        sanitized.Reserved1.Should().Be(0);
        sanitized.Reserved2.Should().Be(0);
        sanitized.Reserved3.Should().Be(0);
    }

    [Fact]
    public void SequentialValidUpdates_IncrementVersionMonotonically()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        uint v1 = service.ConfigVersion;

        MoonshineHostConfigurationPayload p1 = service.CurrentConfiguration;
        p1.RefreshRateHz = 90;
        service.TryApplyConfiguration(in p1, AuthorisationLevel.Administrator, out _, out _, out _);

        uint v2 = service.ConfigVersion;
        v2.Should().Be(v1 + 1);

        MoonshineHostConfigurationPayload p2 = service.CurrentConfiguration;
        p2.RefreshRateHz = 120;
        service.TryApplyConfiguration(in p2, AuthorisationLevel.Administrator, out _, out _, out _);

        uint v3 = service.ConfigVersion;
        v3.Should().Be(v2 + 1);
    }

    [Fact]
    public void RefreshCapabilities_WithTopologyOverride_UpdatesCapabilitiesUnderLock()
    {
        var service = new HostConfigurationService(HostConfigurationService.DefaultCapabilities);
        service.Capabilities.MaxEncodeWidth.Should().Be(3840);

        var customDisplay = new DisplayOutputInfo(
            DisplayIndex: 0,
            AdapterIndex: 0,
            Width: 1920,
            Height: 1080,
            RefreshRateNumerator: 120,
            RefreshRateDenominator: 1,
            Rotation: 0,
            IsAttachedToDesktop: true,
            IsHdr: true,
            BitsPerColor: 10
        );

        var customTopology = new DisplayTopology(
            Adapters: Array.Empty<DisplayAdapterInfo>(),
            Displays: new[] { customDisplay },
            PrimaryDisplay: customDisplay,
            VirtualScreenBounds: new DesktopBounds(0, 0, 1920, 1080),
            IsHeadless: false,
            TimestampQpc: 0
        );

        service.RefreshCapabilities(customTopology);

        service.Capabilities.SupportsHdr10.Should().Be(1);
        service.Capabilities.MaxEncodeFps.Should().Be(120);
    }
}
