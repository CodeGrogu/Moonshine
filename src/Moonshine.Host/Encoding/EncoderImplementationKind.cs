namespace Moonshine.Host.Encoding;

/// <summary>
/// Specifies the implementation authenticity and capability classification of a video encoder pipeline.
/// </summary>
public enum EncoderImplementationKind
{
    /// <summary>
    /// Native hardware accelerated GPU encoder executing real hardware encoding on physical GPU hardware (NVENC, AMF, QuickSync).
    /// </summary>
    HardwareAccelerated = 0,

    /// <summary>
    /// Managed test synthetic or mock encoder emitting bitstreams for test fixtures and integration test harness.
    /// </summary>
    SyntheticTest = 1,

    /// <summary>
    /// Native or managed encoder lacking real hardware session backend or failing to emit valid bitstream payloads.
    /// </summary>
    Unimplemented = 2,

    /// <summary>
    /// Null encoder that discards input frames.
    /// </summary>
    Null = 3
}

