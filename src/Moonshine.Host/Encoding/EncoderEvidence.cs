namespace Moonshine.Host.Encoding;

/// <summary>
/// Authoritative execution evidence collected from the physical video encoder pipeline.
/// Used to deterministically evaluate <c>ComponentReadiness</c>.
/// <para>
/// Hardware Encoder Operational Invariant:
/// No encoder may report Operational based solely on device discovery, API availability,
/// session creation, successful configuration, or frame submission. Operational requires a successfully
/// validated encoded bitstream produced from a real input frame by the selected vendor backend.
/// </para>
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
    bool DecoderAcceptanceHealthy = false,
    bool HasDecoderAcceptance = false,
    bool HasValidFrame = false
);
