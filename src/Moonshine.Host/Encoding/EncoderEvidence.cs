namespace Moonshine.Host.Encoding;

/// <summary>
/// Authoritative execution evidence collected from the physical video encoder pipeline.
/// Used to deterministically evaluate ComponentReadiness.
/// </summary>
public readonly record struct EncoderEvidence(
    bool ApiAvailable,
    bool HardwareSupported,
    bool SessionInitialised,
    bool FrameSubmitted,
    bool OutputReceived,
    bool BitstreamStructurallyValid,
    bool AccessUnitValid,
    bool DecoderAccepted,
    ulong FirstValidFrameId,
    ulong LastValidFrameId,
    ulong LastDecoderAcceptedFrameId = 0,
    bool DecoderAcceptedLatestFrame = false,
    bool DecoderAcceptanceHealthy = false
);
