namespace Moonshine.Host.Encoding;

/// <summary>
/// Authoritative evidence evaluation policy for video encoder operational health.
/// </summary>
public static class EncoderEvidencePolicy
{
    /// <summary>
    /// Default maximum acceptable lag window in frames between encoded and decoder-acknowledged frames.
    /// <para>
    /// The four-frame latency model accounts for the following pipeline stages under normal low-latency streaming conditions:
    /// <list type="bullet">
    /// <item><description>Capture staging [1 frame]: Desktop duplication acquisition, format conversion, and surface handoff.</description></item>
    /// <item><description>Hardware encoder in-flight queue [1 frame]: Asynchronous GPU encoding hardware pipeline depth.</description></item>
    /// <item><description>Network RTP transport / jitter buffer pacing [1-2 frames]: Packet transmission, transit, and receiver pacing.</description></item>
    /// <item><description>Decoder display / presentation feedback [1 frame]: Client-side hardware decoding, display swap chain present, and RTCP feedback round-trip.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public const ulong DefaultDecoderAcceptanceLagWindow = 4;

    /// <summary>
    /// Alias for <see cref="DefaultDecoderAcceptanceLagWindow"/> to maintain compatibility across hardware encoder implementations.
    /// </summary>
    public const ulong DecoderAcceptanceLagWindow = DefaultDecoderAcceptanceLagWindow;

    /// <summary>
    /// Evaluates whether the decoder frame acceptance evidence indicates healthy pipeline operation.
    /// </summary>
    /// <param name="isDisposed">Whether the encoder instance is disposed.</param>
    /// <param name="hasHandle">Whether the native encoder handle is valid and active.</param>
    /// <param name="lastValidFrameId">The identifier of the latest valid encoded frame emitted by the pipeline.</param>
    /// <param name="lastDecoderAcceptedFrameId">The identifier of the latest frame acknowledged and accepted by the remote decoder.</param>
    /// <param name="maxAcceptableLagWindow">The maximum acceptable lag window in frames between encoded and accepted frames. Defaults to <see cref="DefaultDecoderAcceptanceLagWindow"/>.</param>
    /// <returns><c>true</c> if decoder acceptance is healthy; otherwise, <c>false</c>.</returns>
    public static bool IsDecoderAcceptanceHealthy(
        bool isDisposed,
        bool hasHandle,
        ulong lastValidFrameId,
        ulong lastDecoderAcceptedFrameId,
        ulong maxAcceptableLagWindow = DefaultDecoderAcceptanceLagWindow)
    {
        return !isDisposed
            && hasHandle
            && lastDecoderAcceptedFrameId != 0
            && lastDecoderAcceptedFrameId <= lastValidFrameId
            && (lastValidFrameId - lastDecoderAcceptedFrameId) <= maxAcceptableLagWindow;
    }
}

