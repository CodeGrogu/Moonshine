using Moonshine.Core.Security;
using Moonshine.Host.Capture;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Control;

using PhysicalAdapterInfo = DisplayAdapterInfo;

/// <summary>
/// Thread-safe host configuration management service.
/// Coordinates capability validation, configuration mutation, and atomic state updates.
/// </summary>
public sealed class HostConfigurationService
{
    private readonly Lock _gate = new();
    private MoonshineHostCapabilitiesResponsePayload _capabilities;
    private MoonshineHostConfigurationPayload _currentConfig;
    private uint _configVersion;

    /// <summary>
    /// Event triggered whenever a valid configuration is successfully applied.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Explicit protocol-aligned delegate signature requested for high-performance zero-allocation dispatch.")]
    public event Action<MoonshineHostConfigurationPayload, uint>? ConfigurationApplied;

    /// <summary>
    /// Gets the host capabilities advertised by this host.
    /// </summary>
    public MoonshineHostCapabilitiesResponsePayload Capabilities
    {
        get
        {
            lock (_gate)
            {
                return _capabilities;
            }
        }
    }

    /// <summary>
    /// Gets a thread-safe snapshot of the current active host configuration.
    /// </summary>
    public MoonshineHostConfigurationPayload CurrentConfiguration
    {
        get
        {
            lock (_gate)
            {
                return _currentConfig;
            }
        }
    }

    /// <summary>
    /// Gets the current monotonically increasing configuration version counter.
    /// </summary>
    public uint ConfigVersion
    {
        get
        {
            lock (_gate)
            {
                return _configVersion;
            }
        }
    }

    /// <summary>
    /// Canonical default capabilities for a standard Moonshine host instance.
    /// </summary>
    public static MoonshineHostCapabilitiesResponsePayload DefaultCapabilities => new()
    {
        SupportedVideoCodecs = (uint)(MoonshineCapabilities.Av1 | MoonshineCapabilities.Hevc | MoonshineCapabilities.H264),
        SupportedAudioCodecs = (uint)MoonshineAudioCodec.Opus,
        MaxEncodeWidth = 3840,
        MaxEncodeHeight = 2160,
        MaxEncodeFps = 240,
        SupportsHdr10 = 1,
        SupportsVirtualAudio = 1,
        SupportsMicBackchannel = 1,
        Reserved = 0,
        MaxBitrateKbps = 150000,
        Reserved2 = 0
    };

    /// <summary>
    /// Canonical default configuration for a standard Moonshine host instance.
    /// </summary>
    public static MoonshineHostConfigurationPayload DefaultConfiguration => new()
    {
        ConfigVersion = 1,
        DisplayWidth = 1920,
        DisplayHeight = 1080,
        RefreshRateHz = 60,
        TargetBitrateKbps = 20000,
        MaxBitrateKbps = 50000,
        PreferredCodec = MoonshineVideoCodec.Hevc,
        Hdr10Enabled = 0,
        AudioChannels = 2,
        AudioQualityMode = 0,
        AudioBitrateKbps = 128,
        InputPollingRateHz = 1000,
        MicPassthroughEnabled = 1,
        VirtualAudioDriverEnabled = 1,
        Reserved1 = 0,
        Reserved2 = 0,
        Reserved3 = 0
    };

    /// <summary>
    /// Initialises a new instance of the <see cref="HostConfigurationService"/> class using live probed host capabilities.
    /// </summary>
    public HostConfigurationService() : this(HostCapabilityProbeEngine.ProbeLiveCapabilities())
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="HostConfigurationService"/> class.
    /// </summary>
    /// <param name="capabilities">Optional host capabilities payload; if omitted, defaults are applied.</param>
    /// <param name="initialConfiguration">Optional initial configuration payload; if omitted, defaults are applied.</param>
    public HostConfigurationService(
        MoonshineHostCapabilitiesResponsePayload? capabilities,
        MoonshineHostConfigurationPayload? initialConfiguration = null)
    {
        MoonshineHostCapabilitiesResponsePayload caps = capabilities ?? DefaultCapabilities;
        caps.Reserved = 0;
        caps.Reserved2 = 0;
        _capabilities = caps;
        MoonshineHostConfigurationPayload config = initialConfiguration ?? DefaultConfiguration;

        _configVersion = config.ConfigVersion != 0 ? config.ConfigVersion : 1;
        config.ConfigVersion = _configVersion;
        config.Reserved1 = 0;
        config.Reserved2 = 0;
        config.Reserved3 = 0;
        _currentConfig = config;
    }

    /// <summary>
    /// Refreshes the advertised host capabilities by performing a live hardware and topology probe.
    /// </summary>
    /// <param name="topology">Optional display topology override.</param>
    /// <param name="adapters">Optional physical adapters override.</param>
    public void RefreshCapabilities(DisplayTopology? topology = null, IReadOnlyList<PhysicalAdapterInfo>? adapters = null)
    {
        MoonshineHostCapabilitiesResponsePayload probed = HostCapabilityProbeEngine.ProbeLiveCapabilities(topology, adapters);
        probed.Reserved = 0;
        probed.Reserved2 = 0;

        lock (_gate)
        {
            _capabilities = probed;
        }
    }

    /// <summary>
    /// Validates a proposed configuration against the advertised host capabilities and protocol constraints.
    /// </summary>
    /// <param name="proposed">The proposed configuration payload to validate.</param>
    /// <param name="errorMessage">When validation fails, contains a descriptive error message; otherwise null.</param>
    /// <returns>A <see cref="MoonshineErrorCode"/> indicating success or the specific validation failure reason.</returns>
    public MoonshineErrorCode ValidateConfiguration(in MoonshineHostConfigurationPayload proposed, out string? errorMessage)
    {
        MoonshineHostCapabilitiesResponsePayload caps;
        lock (_gate)
        {
            caps = _capabilities;
        }

        // 1. Dimensions validation
        if (proposed.DisplayWidth == 0 || proposed.DisplayHeight == 0)
        {
            errorMessage = "Display dimensions must be greater than zero.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (caps.MaxEncodeWidth > 0 && proposed.DisplayWidth > caps.MaxEncodeWidth)
        {
            errorMessage = $"Requested display width ({proposed.DisplayWidth}) exceeds maximum supported encode width ({caps.MaxEncodeWidth}).";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (caps.MaxEncodeHeight > 0 && proposed.DisplayHeight > caps.MaxEncodeHeight)
        {
            errorMessage = $"Requested display height ({proposed.DisplayHeight}) exceeds maximum supported encode height ({caps.MaxEncodeHeight}).";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 2. Refresh rate validation
        if (proposed.RefreshRateHz == 0)
        {
            errorMessage = "Refresh rate must be greater than zero.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (caps.MaxEncodeFps > 0 && proposed.RefreshRateHz > caps.MaxEncodeFps)
        {
            errorMessage = $"Requested refresh rate ({proposed.RefreshRateHz} Hz) exceeds maximum supported encode frame rate ({caps.MaxEncodeFps} fps).";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 3. Bitrate validation
        if (proposed.TargetBitrateKbps == 0 || proposed.MaxBitrateKbps == 0)
        {
            errorMessage = "Target and maximum bitrates must be greater than zero.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (proposed.TargetBitrateKbps > proposed.MaxBitrateKbps)
        {
            errorMessage = $"Target bitrate ({proposed.TargetBitrateKbps} kbps) cannot exceed maximum bitrate ({proposed.MaxBitrateKbps} kbps).";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        if (caps.MaxBitrateKbps > 0)
        {
            if (proposed.TargetBitrateKbps > caps.MaxBitrateKbps)
            {
                errorMessage = $"Target bitrate ({proposed.TargetBitrateKbps} kbps) exceeds maximum host bitrate capability ({caps.MaxBitrateKbps} kbps).";
                return MoonshineErrorCode.InvalidConfigurationParameter;
            }

            if (proposed.MaxBitrateKbps > caps.MaxBitrateKbps)
            {
                errorMessage = $"Maximum bitrate ({proposed.MaxBitrateKbps} kbps) exceeds maximum host bitrate capability ({caps.MaxBitrateKbps} kbps).";
                return MoonshineErrorCode.InvalidConfigurationParameter;
            }
        }

        // 4. Video codec support validation
        if (!IsCodecSupported(caps.SupportedVideoCodecs, proposed.PreferredCodec))
        {
            errorMessage = $"Requested video codec ({proposed.PreferredCodec}) is not supported by host capabilities.";
            return MoonshineErrorCode.UnsupportedCodec;
        }

        // 5. HDR10 support validation
        if (proposed.Hdr10Enabled != 0 && caps.SupportsHdr10 == 0)
        {
            errorMessage = "HDR10 mode is requested but host hardware does not support HDR10 encoding.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 6. Audio channels validation (strictly 2, 6, or 8 channels)
        if (proposed.AudioChannels is not (2 or 6 or 8))
        {
            errorMessage = $"Audio channel count ({proposed.AudioChannels}) is invalid. Only 2, 6, or 8 channels are supported.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 7. Audio bitrate validation (32 kbps to 1024 kbps)
        if (proposed.AudioBitrateKbps is < 32 or > 1024)
        {
            errorMessage = $"Audio bitrate ({proposed.AudioBitrateKbps} kbps) is out of range. Allowed range is 32 to 1024 kbps.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 8. Microphone backchannel validation
        if (proposed.MicPassthroughEnabled != 0 && caps.SupportsMicBackchannel == 0)
        {
            errorMessage = "Microphone passthrough backchannel is requested but host does not support microphone backchannel.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        // 9. Virtual audio driver validation
        if (proposed.VirtualAudioDriverEnabled != 0 && caps.SupportsVirtualAudio == 0)
        {
            errorMessage = "Virtual audio driver is requested but host does not support virtual audio driver.";
            return MoonshineErrorCode.InvalidConfigurationParameter;
        }

        errorMessage = null;
        return MoonshineErrorCode.Success;
    }

    /// <summary>
    /// Attempts to validate and atomically apply a proposed configuration given the caller's authorisation level.
    /// </summary>
    /// <param name="proposed">The proposed configuration payload.</param>
    /// <param name="callerAuth">The authorisation level of the requesting peer.</param>
    /// <param name="effective">Receives the effective configuration (newly applied on success, or unchanged on failure).</param>
    /// <param name="errorCode">Receives the outcome error code.</param>
    /// <param name="errorMessage">Receives an error description if validation or authorisation failed; otherwise null.</param>
    /// <returns>True if the configuration was successfully validated and applied; otherwise false.</returns>
    public bool TryApplyConfiguration(
        in MoonshineHostConfigurationPayload proposed,
        AuthorisationLevel callerAuth,
        out MoonshineHostConfigurationPayload effective,
        out MoonshineErrorCode errorCode,
        out string? errorMessage)
    {
        // Enforce RBAC: only Controller or Administrator can modify host configuration
        if (callerAuth < AuthorisationLevel.Controller)
        {
            lock (_gate)
            {
                effective = _currentConfig;
            }
            errorCode = MoonshineErrorCode.UnauthorizedConfiguration;
            errorMessage = "Caller authorisation level is insufficient to modify host configuration.";
            return false;
        }

        MoonshineErrorCode validationResult = ValidateConfiguration(in proposed, out errorMessage);
        if (validationResult != MoonshineErrorCode.Success)
        {
            lock (_gate)
            {
                effective = _currentConfig;
            }
            errorCode = validationResult;
            return false;
        }

        lock (_gate)
        {
            _configVersion++;
            MoonshineHostConfigurationPayload sanitized = proposed;
            sanitized.ConfigVersion = _configVersion;
            sanitized.Reserved1 = 0;
            sanitized.Reserved2 = 0;
            sanitized.Reserved3 = 0;
            _currentConfig = sanitized;
            effective = _currentConfig;
        }

        ConfigurationApplied?.Invoke(effective, effective.ConfigVersion);
        errorCode = MoonshineErrorCode.Success;
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Returns a sanitized snapshot of the current active configuration with all reserved fields zeroed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Method performs defensive copying and sanitation operations.")]
    public MoonshineHostConfigurationPayload GetSanitizedConfiguration()
    {
        lock (_gate)
        {
            MoonshineHostConfigurationPayload sanitized = _currentConfig;
            sanitized.Reserved1 = 0;
            sanitized.Reserved2 = 0;
            sanitized.Reserved3 = 0;
            return sanitized;
        }
    }

    private static bool IsCodecSupported(uint supportedVideoCodecsMask, MoonshineVideoCodec codec)
    {
        if (codec is MoonshineVideoCodec.Unknown or > MoonshineVideoCodec.H264)
        {
            return false;
        }

        uint capBit = codec switch
        {
            MoonshineVideoCodec.Av1 => (uint)MoonshineCapabilities.Av1,
            MoonshineVideoCodec.Hevc => (uint)MoonshineCapabilities.Hevc,
            MoonshineVideoCodec.H264 => (uint)MoonshineCapabilities.H264,
            _ => 0
        };

        uint directBit = 1u << (byte)codec;

        return (supportedVideoCodecsMask & capBit) != 0 || (supportedVideoCodecsMask & directBit) != 0;
    }
}
