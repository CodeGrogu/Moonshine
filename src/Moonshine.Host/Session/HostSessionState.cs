using System.Net;
using Moonshine.Host.Audio;
using Moonshine.Host.Encoding;
using Moonshine.Interop;

namespace Moonshine.Host.Session;

/// <summary>
/// State machine states for a Moonshine host streaming session.
/// </summary>
public enum HostSessionState
{
    /// <summary>Session has been created and configured but not yet started.</summary>
    Created = 0,

    /// <summary>Session negotiation and capability exchange in flight.</summary>
    Negotiating = 1,

    /// <summary>Initializing hardware capture, encoder, audio, and transport backends.</summary>
    InitializingBackends = 2,

    /// <summary>All required hardware and network backends are operational and streaming.</summary>
    Streaming = 3,

    /// <summary>Streaming active, but experiencing non-fatal fallback or degradation.</summary>
    Degraded = 4,

    /// <summary>A required backend failed, GPU device lost, or unrecoverable error occurred.</summary>
    Faulted = 5,

    /// <summary>Gracefully draining pending frames and stopping workers.</summary>
    Draining = 6,

    /// <summary>Session completely terminated and all resources deterministically released.</summary>
    Terminated = 7
}

/// <summary>
/// Telemetry metrics snapshot for an active host streaming session.
/// </summary>
public readonly record struct HostSessionMetrics(
    ulong TotalFramesCaptured,
    ulong TotalFramesEncoded,
    ulong TotalPacketsSent,
    ulong TotalBytesSent,
    ulong TotalAudioFramesCaptured,
    ulong TotalAudioPacketsSent,
    ulong TotalInputPacketsProcessed,
    double AverageCaptureToNetworkLatencyUs,
    uint CurrentBitrateKbps,
    ulong KeyframesRequested,
    HostSessionState State,
    string? LastError = null);

/// <summary>
/// Configuration parameters for a host streaming session.
/// </summary>
public sealed record HostSessionConfig
{
    public uint Width { get; init; } = 1920;
    public uint Height { get; init; } = 1080;
    public uint Fps { get; init; } = 60;
    public uint BitrateKbps { get; init; } = 20000;
    public VideoCodec Codec { get; init; } = VideoCodec.HevcMain10;
    public RateControlMode RateControl { get; init; } = RateControlMode.ConstantBitrate;
    public AudioChannelTopology AudioTopology { get; init; } = AudioChannelTopology.Stereo;
    public uint AudioBitrate { get; init; } = 128000;
    public bool EnableHdr10 { get; init; }
    public bool EnableFec { get; init; } = true;
    public int FecDataShards { get; init; } = 10;
    public int FecParityShards { get; init; } = 2;
    public int MtuPayloadSize { get; init; } = 1188;
    public ulong SessionId { get; init; } = 1;
    public uint StreamId { get; init; } = 1;
    public IPAddress ClientAddress { get; init; } = IPAddress.Loopback;
    public int ClientVideoPort { get; init; } = 48011;
    public int ClientAudioPort { get; init; } = 48012;
    public int ClientControlFeedbackPort { get; init; } = 48013;
    public int LocalVideoPort { get; init; }
    public int LocalAudioPort { get; init; }
    public int LocalControlFeedbackPort { get; init; }

    public static HostSessionConfig Default => new();
}
