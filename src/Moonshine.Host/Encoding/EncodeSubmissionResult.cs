using Moonshine.Interop;
using Moonshine.Protocol.Contracts;

namespace Moonshine.Host.Encoding;

/// <summary>
/// Result of submitting a video frame surface to an asynchronous video encoder.
/// </summary>
public readonly record struct EncodeSubmissionResult(
    bool Submitted,
    bool OutputAvailable,
    bool KeyFrame,
    int BytesWritten,
    MoonshineEncodedPacketDesc PacketDesc,
    EncoderResult Result = EncoderResult.Success);
