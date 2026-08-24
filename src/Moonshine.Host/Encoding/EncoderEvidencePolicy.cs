namespace Moonshine.Host.Encoding;

/// <summary>
/// Authoritative evidence evaluation policy for video encoder operational health.
/// </summary>
public static class EncoderEvidencePolicy
{
    /// <summary>
    /// The current acceptance tolerance is four frames, reflecting the configured pipeline's expected in-flight depth and scheduling tolerance. This value is centralised so all hardware encoder implementations apply the same evidence policy.
    /// </summary>
    public const ulong DecoderAcceptanceLagWindow = 4;

    /// <summary>
    /// Evaluates whether the decoder frame acceptance evidence indicates healthy pipeline operation.
    /// </summary>
    /// <param name="isDisposed">Whether the encoder instance is disposed.</param>
    /// <param name="hasHandle">Whether the native encoder handle is valid and active.</param>
    /// <param name="lastValidFrameId">The identifier of the latest valid encoded frame emitted by the pipeline.</param>
    /// <param name="lastDecoderAcceptedFrameId">The identifier of the latest frame acknowledged and accepted by the remote decoder.</param>
    /// <param name="lagWindow">The maximum acceptable lag window in frames between encoded and accepted frames.</param>
    /// <returns><c>true</c> if decoder acceptance is healthy; otherwise, <c>false</c>.</returns>
    public static bool IsDecoderAcceptanceHealthy(bool isDisposed, bool hasHandle, ulong lastValidFrameId, ulong lastDecoderAcceptedFrameId, ulong lagWindow = DecoderAcceptanceLagWindow)
    {
        return !isDisposed && hasHandle && lastDecoderAcceptedFrameId != 0 && lastDecoderAcceptedFrameId <= lastValidFrameId && (lastValidFrameId - lastDecoderAcceptedFrameId) <= lagWindow;
    }
}
